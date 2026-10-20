param([Parameter(Mandatory=$true)][string]$RuntimeDirectory, [string]$TaskName='Codex-UsageBattery-AppStart', [switch]$Apply)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dir = Assert-BatteryRuntime $RuntimeDirectory
$folder = Get-BatteryTaskFolder
$task = Find-BatteryTask $folder $TaskName $dir
if (-not $task) { throw 'Task not found; nothing changed' }
if ($Apply) {
    $task.Xml | Set-Content -LiteralPath (Join-Path $dir ('task-before-disable-'+[Guid]::NewGuid().ToString('N')+'.xml')) -Encoding UTF8
    $task.Enabled = $false
}
[pscustomobject]@{taskName=$TaskName;enabled=$task.Enabled;previewOnly=(-not $Apply);filesDeleted=$false;taskDeleted=$false;runningBattery='Close its Codex window or use battery context menu to exit normally.'} | ConvertTo-Json
