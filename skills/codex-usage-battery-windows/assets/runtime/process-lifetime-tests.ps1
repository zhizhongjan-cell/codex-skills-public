$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'HostLifetime.cs')
$overload=[CodexBattery.HostLifetime].GetMethods() | Where-Object {$_.Name -eq 'ShouldExit' -and $_.GetParameters().Count -eq 3}
if(-not $overload){Write-Output 'FAIL: process-aware lifecycle overload is missing';exit 1}
function Assert-Equal($actual,$expected,$label){if($actual -ne $expected){throw "FAIL $label : expected $expected, got $actual"};Write-Output "PASS $label"}
$now=[DateTime]::UtcNow
$cold=[CodexBattery.HostLifetime]::new($true,$now)
Assert-Equal ($cold.ShouldExit($false,$true,$now)) $false 'actual host process permits loading'
Assert-Equal ($cold.ShouldExit($false,$true,$now.AddSeconds(45))) $false 'slow window creation does not exit after 30 seconds'
Assert-Equal ($cold.ShouldExit($false,$true,$now.AddMinutes(5))) $false 'loading is tied to actual host process, not an arbitrary timer'
Assert-Equal ($cold.ShouldExit($true,$true,$now.AddMinutes(6))) $false 'late window attaches'
Assert-Equal ($cold.ShouldExit($false,$true,$now.AddMinutes(7))) $false 'closing window has one-second transition grace'
Assert-Equal ($cold.ShouldExit($false,$true,$now.AddMinutes(7).AddSeconds(1))) $true 'close exits even if background host process remains'
$aborted=[CodexBattery.HostLifetime]::new($true,$now)
$null=$aborted.ShouldExit($false,$true,$now)
$null=$aborted.ShouldExit($false,$false,$now.AddSeconds(2))
Assert-Equal ($aborted.ShouldExit($false,$false,$now.AddSeconds(3))) $true 'aborted host startup exits helper'
$absent=[CodexBattery.HostLifetime]::new($true,$now)
Assert-Equal ($absent.ShouldExit($false,$false,$now.AddSeconds(30))) $true 'no real host never causes permanent waiting'
$direct=[CodexBattery.HostLifetime]::new($false,$now)
Assert-Equal ($direct.ShouldExit($false,$false,$now)) $true 'direct start without Codex exits immediately'
Write-Output 'ALL 9 PROCESS-AWARE LIFETIME TESTS PASSED'
