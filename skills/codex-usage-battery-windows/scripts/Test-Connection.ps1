param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dir=Assert-BatteryRuntime $RuntimeDirectory
$compiler=Get-BatteryCompiler
$run=Join-Path $dir ('connection-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -ErrorAction Stop | Out-Null
$common=@('/nologo','/target:exe','/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll')
$sources=@((Join-Path $dir 'UsageCore.cs'),(Join-Path $dir 'HostLifetime.cs'),(Join-Path $dir 'QuotaConnection.cs'),(Join-Path $dir 'BatteryOverlay.cs'))
& $compiler /nologo /target:exe "/out:$(Join-Path $run 'codex.exe')" /reference:System.Web.Extensions.dll (Join-Path $dir 'FakeQuotaServer.cs')
if($LASTEXITCODE -ne 0){throw 'Fake server compilation failed'}
[IO.File]::WriteAllText((Join-Path $run 'codex-executable.txt'),(Join-Path $run 'codex.exe'))
foreach($name in @('ReuseRegression','ConnectionTests','RecoveryTest')) {
    & $compiler @common "/main:$name" "/out:$(Join-Path $run ($name+'.exe'))" @sources (Join-Path $dir ($name+'.cs'))
    if($LASTEXITCODE -ne 0){throw "Connection test compilation failed: $name"}
    & (Join-Path $run ($name+'.exe'))
    if($LASTEXITCODE -ne 0){throw "Connection test failed: $name"}
}
$result=Get-Content -LiteralPath (Join-Path $run 'connection-tests.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if(-not $result.ok -or $result.count -ne 33){throw 'Connection regression count or status failed'}
$recovery=Get-Content -LiteralPath (Join-Path $run 'recovery-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if(-not $recovery.ok -or $recovery.count -lt 13){throw 'Window recovery regression failed'}
[pscustomobject]@{ok=$true;recoveryChecks=$recovery.count;connectionChecks=$result.count;processReuseRegression=$true;testDirectory=$run;realCodexStarted=$false} | ConvertTo-Json
