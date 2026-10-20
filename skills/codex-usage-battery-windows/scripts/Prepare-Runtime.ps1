param([Parameter(Mandatory=$true)][string]$Destination, [string]$CodexPath, [switch]$WhatIf)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dir = Resolve-BatteryDirectory $Destination
if (Test-Path -LiteralPath $dir) { throw 'Destination exists; no files overwritten. Choose a new directory.' }
$compiler = Get-BatteryCompiler
if ($CodexPath) {
    $CodexPath = Resolve-BatteryDirectory $CodexPath
    if (-not (Test-Path -LiteralPath $CodexPath -PathType Leaf) -or [IO.Path]::GetFileName($CodexPath) -ine 'codex.exe') { throw 'Supply an existing absolute desktop codex.exe path' }
    $CodexPath = [IO.Path]::GetFullPath($CodexPath)
}
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\runtime'
$names = @('BatteryOverlay.cs','HostLifetime.cs','UsageCore.cs','build.ps1','tests.ps1','host-lifetime-tests.ps1','process-lifetime-tests.ps1','LifecycleTest.cs','ColdStartTest.cs','QuotaConnection.cs','FakeQuotaServer.cs','ConnectionTests.cs','ReuseRegression.cs','LiveConnectionProbe.cs','LICENSE')
foreach ($name in $names) { if (-not (Test-Path -LiteralPath (Join-Path $assets $name))) { throw "Asset missing: $name" } }
if ($WhatIf) { [pscustomobject]@{destination=$dir;files=$names;compiler=$compiler;registersTask=$false;startsCodex=$false;previewOnly=$true} | ConvertTo-Json -Depth 3; return }
New-Item -ItemType Directory -Path $dir -ErrorAction Stop | Out-Null
foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $assets $name) -Destination (Join-Path $dir $name) -ErrorAction Stop }
if ($CodexPath) { [IO.File]::WriteAllText((Join-Path $dir 'codex-executable.txt'), $CodexPath, (New-Object Text.UTF8Encoding($false))); $names += 'codex-executable.txt' }
& (Join-Path $dir 'build.ps1') | Out-Host
$names += 'CodexUsageBattery.exe'
$hashes = @($names | ForEach-Object { [pscustomobject]@{name=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $dir $_) -Algorithm SHA256).Hash} })
[pscustomobject]@{skill='codex-usage-battery-windows';version='1.1.1';files=$hashes} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dir 'runtime-manifest.json') -Encoding UTF8
[pscustomobject]@{runtime=$dir;compiled=$true;taskRegistered=$false;productionStarted=$false} | ConvertTo-Json
