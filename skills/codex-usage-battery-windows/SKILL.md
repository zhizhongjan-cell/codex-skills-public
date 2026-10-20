---
name: codex-usage-battery-windows
license: "Custom - see LICENSE"
description: "Use when a user wants to build, deploy, inspect, repair, or package a remaining-quota battery overlay for the Windows Codex desktop app: Codex 用量电池、剩余百分比、顶部电池、打开 Codex 自动显示、关闭自动退出、开机后电池不出现. Includes source and event-trigger deployment. Not for API billing, context tokens, CLI-only usage, macOS, Linux, or native Codex UI modification."
---

# Windows Codex 用量电池

将真实剩余额度画在电池内部，贴附前台 Codex 窗口顶部。主值取服务返回的主要额度窗口，通常为 5 小时；悬停显示次要窗口和重置时间。时钟旁显示下一次主动补查时间及本周剩余；持续连接接收官方通知，每30秒在同一连接补查。

**它是独立 Windows 悬浮层，不能称为原生顶栏插件。** 优点是不改 Codex 安装包；代价是桌面进程、窗口和应用事件行为变化后可能需要适配。先说明这个权衡。用户已接受悬浮层、并明确要求同启同退时，在原授权范围继续，不重复询问。

## 前提与输入

- Windows 10/11、已用 ChatGPT 账户登录的 Codex 打包桌面版、交互式用户会话、Windows PowerShell 5.1、.NET Framework 4.x 编译器。
- 新的绝对 runtime 目录；实际桌面配套 `codex.exe` 路径；实际 AppID 和可读取的应用启动事件。探测失败就报告缺失项，不能硬套旧机器路径。
- 仅检查、成包、安装 Skill、部署电池是不同范围。Skill 安装不授权创建任务。依用户实际请求与现有批准边界执行；脚本的开关不替代用户授权。

## 执行顺序

1. 读本项目规则和结构。说明拟新增/修改文件，保留现有源码、位置及启动配置。不得删除文件，不覆盖同名安装；保护凭据。
2. 用 `scripts/Inspect-Environment.ps1` 只读检查。它不启动 Codex、不运行电池、不改日志/任务。检查失败时继续可独立完成的准备。
3. 读 [部署与排查](references/workflow.md)。用 `scripts/Prepare-Runtime.ps1 -Destination ... -WhatIf` 预览，再在授权的新目录构建。源码位于 `assets/runtime/`；不要直接在 Skill 安装目录编译。
4. 用 `scripts/Test-Runtime.ps1 -RuntimeDirectory ... -Suite Full` 做80项功能回归（46项原有检查、33项连接检查和1项进程复用回归）。会创建短暂测试窗口并实际等待 32 秒；不关闭用户 Codex。`-Suite Usage` 只读查询账户额度，无模型推理。额度源不可用时显示未知，不造值。
5. 自动部署已获授权时，先运行 `scripts/Register-Trigger.ps1` 查看计划，核对对象后加 `-Enable`。仅注册当前用户普通权限的应用 Event 201 触发；不增加登录/开机等待进程、不主动打开 Codex、不靠替换快捷方式覆盖入口。
6. 验证真实窗口显示、前后台显隐、最后窗口关闭退出及再次打开。需要关闭用户当前应用、重启 Windows 时，按会话授权处理；未执行就明确待验。不要为测试中断当前工作。
7. 报告新文件、任务/触发器、真实验证结果和限制。任务“正在运行”或退出码 0、托管 Visible=true 均不足以证明画面可见。按 [验证标准](references/validation.md) 给证据。

## 故障与停止条件

- 约 30 秒退出：查进程与窗口时间线；进程存在而窗口未就绪时不能仅因 30 秒终止等待。读 [接口和生命周期](references/protocol-and-lifecycle.md)。
- 有进程没画面：检查前台/最小化、原生可见性、层级中心命中和位置；不要只重建快捷方式。
- 不同 runtime 的旧任务、同名 Skill、未知 AppID、事件不可读、哈希变化：停止受影响写入并解释冲突，继续只读检查；不自动提权、不改审计日志、不删除旧任务或文件。
- 停用前预览 `scripts/Disable-Trigger.ps1`，授权后加 `-Apply`。仅停用匹配本 runtime 的任务；保留任务、文件和备份。运行中的电池随 Codex 关闭或电池菜单退出。
- 不读取/复制 auth.json、API Key、Token，不发起模型对话；不把日志、账户额度、用户名、SID、生成的 exe 或本机任务 XML 打入可分发包。

## 交付与复用

安装/调用见 [安装说明](references/installation.md)。先安装 Skill，再由用户请求检查或部署。可调用：`$codex-usage-battery-windows 检查这台电脑，按已确认的同启同退方式部署用量电池。`

本包 1.1.1 为公开分享候选版。来源和验证边界见 [验证记录](references/validation.md)：本机回归与隔离构建可复现；跨机器兼容、下一次真实 Windows 开机另验，不能把模拟冷启动等同实际开机。

许可见 [LICENSE](LICENSE)：允许免费使用、修改、分享及在商业工作中作为工具使用；未经另行书面许可，不得收费提供本 Skill 本身。分发 Skill 或构建的运行目录时保留许可。

连接诊断和更新时间含义见 [持续连接与时钟](references/persistent-connection.md)。正常刷新不重新启动进程；只有断线、协议异常或超时才重连，普通查询错误在原连接上退避重试。
