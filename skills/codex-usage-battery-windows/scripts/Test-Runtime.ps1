param([Parameter(Mandatory=$true)][string]$RuntimeDirectory, [ValidateSet('State','Unit','Full','Usage')][string]$Suite='State')
. (Join-Path $PSScriptRoot 'Common.ps1')
$dir = Assert-BatteryRuntime $RuntimeDirectory
$exe = Join-Path $dir 'CodexUsageBattery.exe'
if ($Suite -eq 'State') {
    $statusFile = Join-Path $dir 'status.json'
    $status = $null
    if (Test-Path -LiteralPath $statusFile) { $status = Get-Content -LiteralPath $statusFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    [pscustomobject]@{runtime=$dir;integrity='passed';status=$status;readOnly=$true} | ConvertTo-Json -Depth 6
    return
}
function Invoke-BatteryTest([string]$File,[string]$Report,[string]$Argument='') {
    $reportPath = Join-Path $dir $Report
    $before = if (Test-Path -LiteralPath $reportPath) { (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc } else { [DateTime]::MinValue }
    $options = @{FilePath=$File;Wait=$true;PassThru=$true;WindowStyle='Hidden'}
    if ($Argument) { $options.ArgumentList=$Argument }
    $process = Start-Process @options
    if ($process.ExitCode -ne 0) { throw "Test process failed: $Report ($($process.ExitCode))" }
    if (-not (Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTimeUtc -le $before) { throw "Fresh report not produced: $Report" }
    $result = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $result.ok) { throw "Test failed: $Report" }
    $result | ConvertTo-Json -Depth 6
}
if ($Suite -eq 'Usage') { Invoke-BatteryTest $exe 'probe.json' '--probe'; return }
$ps = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
foreach ($test in @('tests.ps1','host-lifetime-tests.ps1','process-lifetime-tests.ps1')) {
    & $ps -NoProfile -File (Join-Path $dir $test)
    if ($LASTEXITCODE -ne 0) { throw "Unit suite failed: $test" }
}
if ($Suite -eq 'Unit') { return }
$compiler = Get-BatteryCompiler
foreach ($test in @('LifecycleTest','ColdStartTest')) {
    & $compiler /nologo /target:winexe /optimize+ /utf8output "/main:CodexBattery.$test" "/out:$(Join-Path $dir ($test+'.exe'))" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $dir 'UsageCore.cs') (Join-Path $dir 'HostLifetime.cs') (Join-Path $dir 'BatteryOverlay.cs') (Join-Path $dir ($test+'.cs'))
    if ($LASTEXITCODE -ne 0) { throw "Test build failed: $test" }
}
Invoke-BatteryTest $exe 'ui-test.json' '--ui-test'
Invoke-BatteryTest (Join-Path $dir 'LifecycleTest.exe') 'lifecycle-test.json'
Invoke-BatteryTest (Join-Path $dir 'ColdStartTest.exe') 'cold-start-test.json'
