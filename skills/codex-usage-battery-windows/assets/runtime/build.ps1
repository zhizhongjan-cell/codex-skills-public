$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# 编译器不可用' }
$out = Join-Path $PSScriptRoot 'CodexUsageBattery.exe'
& $compiler /nologo /target:winexe /optimize+ /utf8output "/out:$out" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'UsageCore.cs') (Join-Path $PSScriptRoot 'HostLifetime.cs') (Join-Path $PSScriptRoot 'BatteryOverlay.cs')
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output "编译成功: $out"
