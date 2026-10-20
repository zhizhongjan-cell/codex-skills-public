$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'HostLifetime.cs'
if(-not(Test-Path -LiteralPath $source)){ Write-Output 'FAIL: host-bound lifetime is not implemented'; exit 1 }
Add-Type -Path $source
function Assert-Equal($actual,$expected,$label){if($actual -ne $expected){throw "FAIL $label : expected $expected, got $actual"}; Write-Output "PASS $label"}
$now=[DateTime]::UtcNow
$manual=[CodexBattery.HostLifetime]::new($false,$now)
Assert-Equal ($manual.ShouldExit($false,$now)) $true 'no host means immediate exit for direct battery startup'
$launched=[CodexBattery.HostLifetime]::new($true,$now)
Assert-Equal ($launched.ShouldExit($false,$now)) $false 'user launch allows bounded app startup'
Assert-Equal ($launched.ShouldExit($false,$now.AddSeconds(29))) $false 'launch grace lasts less than 30 seconds'
Assert-Equal ($launched.ShouldExit($false,$now.AddSeconds(30))) $true 'failed app launch never waits forever'
$hostLife=[CodexBattery.HostLifetime]::new($false,$now)
Assert-Equal ($hostLife.ShouldExit($true,$now)) $false 'open host keeps battery alive'
Assert-Equal ($hostLife.ShouldExit($true,$now.AddMinutes(30))) $false 'minimized host still counts as open'
Assert-Equal ($hostLife.ShouldExit($false,$now.AddMinutes(31))) $false 'brief window recreation has one-second grace'
Assert-Equal ($hostLife.ShouldExit($false,$now.AddMinutes(31).AddSeconds(1))) $true 'last host close exits battery'
$transient=[CodexBattery.HostLifetime]::new($false,$now)
$null=$transient.ShouldExit($true,$now)
$null=$transient.ShouldExit($false,$now.AddSeconds(1))
Assert-Equal ($transient.ShouldExit($true,$now.AddMilliseconds(1500))) $false 'replacement window within grace preserves battery'
Write-Output 'ALL 9 LIFETIME TESTS PASSED'
