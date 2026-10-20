# 部署、迁移与排查

## 1. 检查与准备

以下命令在 Windows PowerShell 5.1 执行。将 `$skill` 设为实际已解压的 Skill 根目录，`$runtime` 设为新的、稳定保留的绝对目录。它们是操作变量，不要照抄其他用户目录。

```powershell
$skill = 'D:\Tools\Skills\codex-usage-battery-windows' # 替换为实际路径
$runtime = Join-Path $env:LOCALAPPDATA 'CodexUsageBattery\runtime-v1-1-0'
& "$skill\scripts\Inspect-Environment.ps1"
& "$skill\scripts\Prepare-Runtime.ps1" -Destination $runtime -WhatIf
```

检查输出的 `compiler`、`selectedAppId`、`cliCandidates`、`eventLogEnabled`、`matchingEvent`。多 AppID 时用本机实际值传 `-AppId`，不能猜测。配套 CLI 通常在 LocalAppData/OpenAI/Codex/bin 下；不要拿 PATH 上任意同名 CLI 代替。当前没有桌面进程不等于未安装。

如果没有启动事件，请用户正常打开一次 Codex，再检查；不擅自启动或重启用户应用。事件日志关闭或无权限则停止自动触发部署；可继续构建/离线测试，不改全局审计或管理员权限。

确认 CLI 来源可信，查看文件版本、路径来源；然后明确传入本机真实 CLI 路径：

```powershell
$desktopCli = 'D:\REPLACE-WITH-ACTUAL-PATH\codex.exe'
& "$skill\scripts\Prepare-Runtime.ps1" -Destination $runtime -CodexPath $desktopCli
& "$skill\scripts\Test-Runtime.ps1" -RuntimeDirectory $runtime -Suite Full
& "$skill\scripts\Test-Runtime.ps1" -RuntimeDirectory $runtime -Suite Usage
```

目标已存在时 Prepare 拒绝覆盖。编译失败会保留现场，换新目录重试，不自动删除。runtime manifest 检查文件是否变化，是完整性提示，不是签名或可信发行者证明。修改源码/CLI 配置后须重新构建到新目录，不能只改 manifest 绕过检查。显式 CLI 路径失效后重新只读探测，不私自切换未知程序。

## 2. 自动触发

只有用户请求自动部署或批准具体计划时才使用 `-Enable`。

```powershell
# 默认只预览，遇旧安装冲突会中止。
& "$skill\scripts\Register-Trigger.ps1" -RuntimeDirectory $runtime
# 已有授权、预览对象正确后：
& "$skill\scripts\Register-Trigger.ps1" -RuntimeDirectory $runtime -Enable
```

任务默认名 `Codex-UsageBattery-AppStart`。自定义名字后检查/停用都传同一 `-TaskName`。参数 `-AppId` 来自 Inspect，未提供则唯一发现；任务执行本目录 exe `--attach`。注册不立即启动程序，下一次正常激活 Codex 才触发。若用户要求当前窗口马上显示，可在授权范围用 `Start-Process -FilePath "$runtime\CodexUsageBattery.exe" -ArgumentList '--attach' -WindowStyle Hidden`；它不打开 Codex。

配置固定为：当前用户 InteractiveToken、普通权限、唯一 EventTrigger、1 秒延迟、无限运行时限、IgnoreNew、允许使用电池运行、不开启错过后补跑或唤醒。没有 Logon/Boot/定时轮询触发，不保存密码。不要把任务运行身份设为 SYSTEM，UI 会到不可见会话。

本工具任务登记在任务计划根目录；旧同名任务只允许相同 exe 路径及 `--attach`，更新前保存 XML。检查其他电池任务时递归读取根目录及子文件夹，发现其他启用任务以 `CodexUsageBattery.exe` 为动作会阻止新增；子文件夹权限不足则中止登记，不能视为没有旧任务。经 PowerShell、快捷方式或其他包装器间接启动的旧任务不在此自动匹配范围，须另行人工检查。先检查其路径/身份/触发器，再在明确迁移范围内备份停用；不要擅自删除、覆盖或让多个版本争抢单实例。脚本不会改旧登录项或快捷方式，若发现旧方案应单独说明迁移对象。

保持 runtime 路径稳定，移走目录后任务目标失效。更新时保留旧版本与位置文件，先在新目录验证，再按授权迁移任务；不自动覆盖正在运行的 exe。

## 3. 验收与故障定位

| 现象 | 核对证据与下一步 |
|---|---|
| 启动事件有、任务不运行 | 用户身份、事件字段和任务启用状态、action 路径、触发订阅，不按版本化 PackageName 过滤 |
| 任务启动约 30 秒 exit 0 | 对照 GUI 进程启动、窗口首次出现和 run-history phase；不是凭退出码认成功，检查是否旧 exe/旧窗口等待期限 |
| status 可见但看不到 | 核对 nativeVisible、uncoveredAtCenter、前台窗口、屏幕位置/缩放；只在 Codex 前台提升层级，切走时隐藏并撤销 |
| 电池显示 `--` / `?` | 检查 Usage probe、登录状态、数据时间/窗口是否为空；不把缺失值变成100%，不要求用户交出密钥 |
| 关闭后仍有电池 | 核对最后窗口与同会话进程；曾挂接后应在最后窗口关闭约1秒退出，后台残留不应维持电池 |
| 结果267009/0x41301 | 任务仍在运行的状态码，结合进程及窗口判断，不当作错误 |

用 `Test-Runtime -Suite State` 读取 manifest/status；检查 `run-history.jsonl` 的 start/phase/exit；事件、任务结果与画面证据互相对照。`Full` 测试用独立假宿主，不关闭真实 Codex，不登记任务。测试窗口可能短暂出现，先说明。

安全停用：先预览，再在授权范围加 `-Apply`，只改变已核对任务的 Enabled 状态：

```powershell
& "$skill\scripts\Disable-Trigger.ps1" -RuntimeDirectory $runtime
& "$skill\scripts\Disable-Trigger.ps1" -RuntimeDirectory $runtime -Apply
```

没有自动卸载/删除脚本。需要删除时先遵循用户批准规则；停用不等于移除 Skill，也不立即关闭运行中的电池。

## 1.1.0 持续连接验证

Full套件现在包含33项协议/取消/退避检查和进程复用回归，使用隔离的假服务，不读取真实登录。`Test-Runtime -Suite Live`另在真实官方接口上连续运行等待至少3次读取（最长95秒），要求至少3次读取而只启动1个子进程，结束时清理子进程。只有用户授权实际连接检查时执行Live。该检查不等于官方桌面画面零延迟同步。


## 1.1.1 调整

默认补查间隔改为30秒，接受与官方画面有延迟。持续连接、通知优先、进程复用和原应用启动任务保留。1.1.1本轮验证见仓库dist发布说明，以上1.1.0为历史记录。


## 1.1.2 重置时间显示

2026-10-21：按用户要求，时钟旁改为官方5小时额度重置时间，按本机时区显示HH:mm；缺失显示未知，周余量保留。后台30秒补查、持续连接和通知优先不变。以上版本记录为历史，当前以本条及1.1.2发布验证说明为准。
