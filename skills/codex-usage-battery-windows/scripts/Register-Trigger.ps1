param([Parameter(Mandatory=$true)][string]$RuntimeDirectory, [string]$AppId, [string]$TaskName='Codex-UsageBattery-AppStart', [switch]$Enable)
. (Join-Path $PSScriptRoot 'Common.ps1')
$dir = Assert-BatteryRuntime $RuntimeDirectory
$app = Get-BatteryAppId $AppId
if ($TaskName -notmatch '^[\p{L}\p{N}_. -]{1,100}$') { throw 'Unsafe task name' }
$channel = 'Microsoft-Windows-AppModel-Runtime/Admin'
$filter = "*[System[EventID=201]] and *[EventData[Data[@Name='ApplicationName']='$app']]"
if (-not (Get-WinEvent -ListLog $channel).IsEnabled) { throw 'Activation event log disabled; no logging settings changed' }
$matched = Get-WinEvent -LogName $channel -FilterXPath $filter -MaxEvents 1 -ErrorAction Stop
$folder = Get-BatteryTaskFolder
$existing = Find-BatteryTask $folder $TaskName $dir
# Avoid silently creating a second automatic launcher for an existing battery installation.
$others = @(Get-BatteryTasksRecursive $folder | Where-Object { $_.Path -ne ('\'+$TaskName) -and $_.Enabled -and (@($_.Definition.Actions | Where-Object { $_.Path -match 'CodexUsageBattery\.exe$' }).Count -gt 0) })
if ($others.Count) { throw 'Another enabled battery task exists; inspect it and resolve migration explicitly before enabling another.' }
$exe = Join-Path $dir 'CodexUsageBattery.exe'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $Enable) { [pscustomobject]@{taskName=$TaskName;taskFolder='\';action=$exe;workingDirectory=$dir;arguments='--attach';appId=$app;subscription=$filter;existing=[bool]$existing;matchingEvent=$matched.RecordId;previewOnly=$true;logonTrigger=$false;startsCodex=$false;user=$identity.Name;userSid=$identity.User.Value;logonType='InteractiveToken';runLevel='LeastPrivilege';delay='PT1S';executionTimeLimit='PT0S';multipleInstances='IgnoreNew';startWhenAvailable=$false;wakeToRun=$false;disallowStartIfOnBatteries=$false;stopIfGoingOnBatteries=$false} | ConvertTo-Json; return }
if ($existing) { $existing.Xml | Set-Content -LiteralPath (Join-Path $dir ('task-before-'+[Guid]::NewGuid().ToString('N')+'.xml')) -Encoding UTF8 }
$service = New-Object -ComObject Schedule.Service
$service.Connect()
$def = $service.NewTask(0)
$def.RegistrationInfo.Description = 'Codex usage battery: app event only, no login waiter, does not launch Codex.'
$def.Principal.UserId = $identity.User.Value
$def.Principal.LogonType = 3
$def.Principal.RunLevel = 0
$def.Settings.Enabled = $true
$def.Settings.AllowDemandStart = $true
$def.Settings.StartWhenAvailable = $false
$def.Settings.DisallowStartIfOnBatteries = $false
$def.Settings.StopIfGoingOnBatteries = $false
$def.Settings.MultipleInstances = 2
$def.Settings.ExecutionTimeLimit = 'PT0S'
$def.Settings.WakeToRun = $false
$trigger = $def.Triggers.Create(0)
$trigger.Delay = 'PT1S'
$trigger.Enabled = $true
$trigger.Subscription = "<QueryList><Query Id='0' Path='$channel'><Select Path='$channel'>$filter</Select></Query></QueryList>"
$action = $def.Actions.Create(0)
$action.Path = $exe
$action.Arguments = '--attach'
$action.WorkingDirectory = $dir
$task = $folder.RegisterTaskDefinition($TaskName,$def,6,$identity.User.Value,$null,3,$null)
if ($task.Definition.Triggers.Count -ne 1 -or $task.Definition.Triggers.Item(1).Type -ne 0) { throw 'Registered task has unexpected trigger type; verify manually' }
$task.Xml | Set-Content -LiteralPath (Join-Path $dir 'registered-task.xml') -Encoding UTF8
[pscustomobject]@{taskName=$task.Name;enabled=$task.Enabled;state=$task.State;registered=$true;logonTrigger=$false;productionStarted=$false} | ConvertTo-Json
