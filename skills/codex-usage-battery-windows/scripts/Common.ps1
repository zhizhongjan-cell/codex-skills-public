$ErrorActionPreference = 'Stop'
function Get-BatteryCompiler {
    $path = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw '.NET Framework 64-bit compiler not found' }
    return $path
}
function Resolve-BatteryDirectory([string]$Path) {
    if ($Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+\\[^\\/]+(?:\\|$))') { throw 'An absolute runtime directory is required (not a drive-relative path)' }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}
function Assert-BatteryRuntime([string]$Directory) {
    $dir = Resolve-BatteryDirectory $Directory
    $manifestFile = Join-Path $dir 'runtime-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) { throw 'Prepared runtime manifest missing' }
    $manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.skill -ne 'codex-usage-battery-windows' -or $manifest.version -ne '1.0.1') { throw 'Unknown runtime manifest' }
    $entries = @($manifest.files)
    if (@($entries | Where-Object { $_.name -eq 'CodexUsageBattery.exe' }).Count -ne 1) { throw 'Executable hash missing' }
    foreach ($entry in $entries) {
        if ($entry.name -notmatch '^[A-Za-z0-9.-]+$') { throw 'Unsafe manifest filename' }
        $file = Join-Path $dir $entry.name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Runtime file missing: $($entry.name)" }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Runtime hash mismatch: $($entry.name)" }
    }
    return $dir
}
function Get-BatteryAppId([string]$AppId) {
    if (-not $AppId) {
        $apps = @(Get-StartApps | Where-Object { $_.AppID -match '^OpenAI\.Codex_[A-Za-z0-9]+![A-Za-z0-9._-]+$' })
        if ($apps.Count -ne 1) { throw 'Cannot uniquely discover Codex AppID. Inspect environment and supply the actual -AppId.' }
        $AppId = $apps[0].AppID
    }
    if ($AppId -notmatch '^OpenAI\.Codex_[A-Za-z0-9]+![A-Za-z0-9._-]+$') { throw 'Unsupported or unsafe Codex AppID' }
    return $AppId
}
function Get-BatteryTaskFolder {
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    return $service.GetFolder('\')
}
function Get-BatteryTasksRecursive($Folder) {
    foreach ($task in @($Folder.GetTasks(1))) { $task }
    foreach ($child in @($Folder.GetFolders(0))) { Get-BatteryTasksRecursive $child }
}
function Find-BatteryTask($Folder, [string]$Name, [string]$Directory) {
    $tasks = @($Folder.GetTasks(1) | Where-Object { $_.Name -eq $Name })
    if ($tasks.Count -eq 0) { return $null }
    $task = $tasks[0]
    if ($task.Definition.Actions.Count -ne 1 -or
        $task.Definition.Actions.Item(1).Path -ne (Join-Path $Directory 'CodexUsageBattery.exe') -or
        $task.Definition.Actions.Item(1).Arguments -ne '--attach') { throw 'Task name conflict: existing task left unchanged' }
    return $task
}
