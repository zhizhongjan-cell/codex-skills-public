$ErrorActionPreference = 'Stop'
$core = Join-Path $PSScriptRoot 'UsageCore.cs'
if (-not (Test-Path -LiteralPath $core)) { Write-Output 'FAIL: quota model is not implemented'; exit 1 }
Add-Type -Path $core -ReferencedAssemblies System.Web.Extensions
function Assert-Equal($actual, $expected, $label) {
    if ($actual -ne $expected) { throw "FAIL $label : expected $expected, actual $actual" }
    Write-Output "PASS $label"
}
$sample = '{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":36,"windowDurationMins":300,"resetsAt":1792337124},"secondary":{"usedPercent":6,"windowDurationMins":10080,"resetsAt":1792881805}}}}'
$quota = [CodexBattery.UsageSnapshot]::Parse($sample)
Assert-Equal $quota.PrimaryRemaining 64 'prefer codex bucket and convert used to remaining'
Assert-Equal $quota.SecondaryRemaining 94 'weekly remaining'
$empty = [CodexBattery.UsageSnapshot]::Parse('{"rateLimits":{"primary":null,"secondary":null}}')
Assert-Equal $empty.PrimaryRemaining $null 'missing window stays unknown'
$missingPercent = [CodexBattery.UsageSnapshot]::Parse('{"rateLimits":{"primary":{"windowDurationMins":300}}}')
Assert-Equal $missingPercent.PrimaryRemaining $null 'missing percentage does not become 100 percent'
Assert-Equal ([CodexBattery.UsageSnapshot]::Parse('{"rateLimits":{"primary":{"usedPercent":120}}}').PrimaryRemaining) 0 'clamp exhausted quota'
Assert-Equal ([CodexBattery.UsageSnapshot]::Parse('{"rateLimits":{"primary":{"usedPercent":-3}}}').PrimaryRemaining) 100 'clamp lower bound'
Assert-Equal ([CodexBattery.UsageSnapshot]::Parse('{"rateLimits":{"primary":{"usedPercent":36.9}}}').PrimaryRemaining) 63 'round down remaining quota'
$other = [CodexBattery.UsageSnapshot]::Parse('{"rateLimitsByLimitId":{"other":{"primary":{"usedPercent":10}}}}')
Assert-Equal $other.PrimaryRemaining $null 'do not substitute unrelated bucket'
$legacy = [CodexBattery.UsageSnapshot]::Parse('{"rateLimits":{"primary":{"usedPercent":40}}}')
Assert-Equal $legacy.PrimaryRemaining 60 'legacy single bucket supported'
$rejected = $false
try { [CodexBattery.UsageSnapshot]::Parse('{"error":{"message":"unauthorized"}}') | Out-Null } catch { $rejected = $true }
Assert-Equal $rejected $true 'error responses are rejected'
Assert-Equal ([CodexBattery.UsageSnapshot]::IsStale([DateTime]::UtcNow.AddMinutes(-3))) $true 'stale data indicated after two minutes'
Assert-Equal ([CodexBattery.UsageSnapshot]::IsStale([DateTime]::UtcNow)) $false 'fresh data indicated'
Write-Output 'ALL 12 TESTS PASSED'
