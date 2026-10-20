param([string]$AppId)
. (Join-Path $PSScriptRoot 'Common.ps1')
$issues = @()
$compiler = $null
try { $compiler = Get-BatteryCompiler } catch { $issues += $_.Exception.Message }
$apps = @()
try { $apps = @(Get-StartApps | Where-Object { $_.AppID -match '^OpenAI\.Codex_' } | Select-Object Name,AppID) } catch { $issues += 'Get-StartApps unavailable' }
$selected = $null
try { $selected = Get-BatteryAppId $AppId } catch { $issues += $_.Exception.Message }
$gui = @()
$cli = @()
$session = (Get-Process -Id $PID).SessionId
foreach ($name in @('ChatGPT','Codex','codex')) {
    foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
        try {
            $path = $process.Path
            if ($process.SessionId -eq $session -and $path -match 'OpenAI[\\.]Codex') {
                if ([IO.Path]::GetFileName($path) -ieq 'codex.exe' -and $path -match '\\bin\\') { $cli += $path }
                else { $gui += [pscustomobject]@{id=$process.Id;path=$path;mainWindow=$process.MainWindowHandle.ToInt64()} }
            }
        } catch { }
    }
}
$cliRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
if (Test-Path -LiteralPath $cliRoot) {
    $cli += @(Get-ChildItem -LiteralPath $cliRoot -Filter codex.exe -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -ExpandProperty FullName)
}
$channel = 'Microsoft-Windows-AppModel-Runtime/Admin'
$enabled = $null
$eventInfo = $null
try {
    $enabled = (Get-WinEvent -ListLog $channel).IsEnabled
    if ($selected -and $enabled) {
        $filter = "*[System[EventID=201]] and *[EventData[Data[@Name='ApplicationName']='$selected']]"
        $event = Get-WinEvent -LogName $channel -FilterXPath $filter -MaxEvents 1
        $eventInfo = [pscustomobject]@{id=$event.Id;recordId=$event.RecordId;time=$event.TimeCreated}
    }
} catch { $issues += 'Activation event unavailable: manually open Codex, then inspect again; no log settings changed' }
[pscustomobject]@{platform=[Environment]::OSVersion.VersionString;compiler=$compiler;apps=$apps;selectedAppId=$selected;gui=$gui;cliCandidates=@($cli | Select-Object -Unique);eventChannel=$channel;eventLogEnabled=$enabled;matchingEvent=$eventInfo;issues=$issues;readOnly=$true} | ConvertTo-Json -Depth 6
