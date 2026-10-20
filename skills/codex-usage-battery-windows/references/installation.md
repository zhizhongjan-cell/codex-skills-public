# 安装与使用

本 ZIP 是 Skill 源包，不是 Codex 原生 UI 插件或已注册的启动项。安装 Skill 后仍需调用并按授权部署 runtime。

最省事的方式：把 ZIP 交给 Codex，说“安装这个 Skill；同名已存在先比较，不要覆盖。安装后先只读检查本机条件。”

手工安装时先验证 ZIP CRC、单根目录和文件列表，确认无路径穿越、凭据或机器日志。将顶层 `codex-usage-battery-windows` 文件夹复制到当前环境实际支持的 Skill 搜索目录，例如用户的 `$env:CODEX_HOME\skills`（若已设置），或 `$env:USERPROFILE\.codex\skills`。不同版本发现机制可能有差异，以当前 Codex 设置与 Skill 列表为准。目标同名存在时先做逐文件哈希比较，完全一致则不用重复安装；不同则报告差异并按用户授权处理，不自动覆盖。

安装后的结构应是 `skills/codex-usage-battery-windows/SKILL.md`，不能多套一层日期目录。重新加载/新会话后检查 Skill 列表能否发现，不把磁盘复制完成称为运行验证完成。

可直接输入：

```text
$codex-usage-battery-windows 先只读检查这台 Windows 的 Codex 用量电池条件，不要登记任务。
```

确定要部署时：

```text
$codex-usage-battery-windows 在新目录构建并验证剩余用量电池。我接受独立悬浮层；要求打开桌面Codex后自动显示、关闭最后窗口后退出，不设置登录常驻等待。请预览并在这个范围完成事件触发部署。
```

包内没有预编译 exe；Prepare 使用 Windows 自带编译器本地构建。没有额外 API Key、Python、npm 或网络下载依赖。查询账户用量需 Codex 现有登录及连接可用，离线回归不需要账户。

仅成包或安装 Skill，不会自动更改当前正在运行的电池。不要移动已部署 runtime；停用请按 workflow 使用 Disable 脚本，保留文件。删除需另按用户批准规则。

公开分享版：可从 https://github.com/zhizhongjan-cell/codex-skills-public 获取。使用和转发前阅读 Skill 根目录 LICENSE；可以在商业工作中免费作为工具使用，但不能收费提供这个 Skill 本身。

1.1.0 在电池旁显示时钟、下一次主动补查时间及真实周额度；同一查询连接每15秒校对，通知可能更早更新。更新时在新目录构建验证，保留旧包与位置；不要直接覆盖正在运行的exe。
