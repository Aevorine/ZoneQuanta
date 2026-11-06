# SPEC — ZoneQuanta
> 「现在」的唯一事实源。稳定知识在 README.md，长期方向在 .claude/TECH_ROADMAP.md，规则在 .claude/CLAUDE.md。

## 当前目标
Windows 桌面时钟组件（本地 + 洛杉矶），WPF / .NET 8 单文件 exe。验收标准：
1. 鼠标穿透、总是置顶、锁定位置、允许超出屏幕、全屏时隐藏、开机自启，均有开关且立即生效
2. 托盘左键显示 / 隐藏面板（隐藏后任务栏无图标），右键含常用开关；全局快捷键切换面板
3. 在线更新：多线路并发 + 分段下载，签名与 SHA-256 校验，原地替换重启
4. 界面风格统一、护眼、可扩展（页面注册表、时区列表数据化）
5. 公开仓库 Aevorine/ZoneQuanta，Releases 只保留最近两版

## 计划与进度
| ID | 事项 | 泳道 / 负责 | 状态（未开始 / 进行中 / 待验收 / 完成 / 阻塞） | 证据（命令输出 / 文件 / 提交；日期写绝对日期） |
|---|---|---|---|---|
| T-20261002-2 | 桌面时钟组件全功能实现、打包、发布 | 主线 | 待验收 | 提交 ee62b1c；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-10-02） |
| T-20261002-3 | 时区编辑 | 主线 | 待验收 | 证据见 `history/2026-11_v1.0-1.4.1.md`（2026-10-02） |
| T-20261002-4 | v1.1.0 | 主线 | 待验收 | 证据见 `history/2026-11_v1.0-1.4.1.md`（2026-10-02） |
| T-20261008-1 | v1.3.0 显示速度 | 主线 | 待验收 | 证据见 `history/2026-11_v1.0-1.4.1.md`（2026-10-08） |
| T-20261101-1 | v1.3.2 面板最大化 / 还原按钮 | 主线 | 待验收 | 提交 eb8642d；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-01） |
| T-20261105-1 | v1.4.0 流量额度查询 + 快捷面板 | 主线 | 待验收 | 提交 43305aa / 9f886db / 6d2b054 / 0007227（v1.4.4）/ 7ea3356（v1.4.5）/ 074cddf（v1.4.6）/ 0ff4c46（v1.4.8）/ 39d08e5（v1.4.9）；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-05） |
| T-20261101-2 | v1.3.3 页面随窗口填满 | 主线 | 待验收 | 提交 82bd00e；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-01） |

### T-20261105-1 第一轮验收（v1.4.0 / v1.4.1，1–6）见 `history/2026-11_v1.0-1.4.1.md`

### T-20261105-1 第二至第六轮验收（v1.4.2，7–23）见 `history/2026-11_v1.4.2-1.4.4.md`

### T-20261105-1 第七、第八轮验收（v1.4.4 / v1.4.5，24–31）见 `history/2026-11_v1.4.4-1.4.5.md`

### T-20261105-1 第九轮验收（v1.4.6 / v1.4.7，32–37）见 `history/2026-11_v1.4.6-1.4.7.md`

### T-20261105-1 第十轮验收（v1.4.8，38–39）见 `history/2026-11_v1.4.8.md`

### T-20261105-1 第十一、十二轮验收（v1.4.9，40–48）见 `history/2026-11_v1.4.9.md`

### T-20261105-1 第十三轮验收（v1.5.0，49–56）见 `history/2026-11_v1.5.0.md`

### T-20261105-1 第十四轮验收（v1.6.0，57–61）
| # | 验收条（可检查） | 状态 | 证据 |
|---|---|---|---|
| 57 | 「间距」浮层：横向 / 纵向间距、左右 / 上下边距、分组间距 5 个滑块 + 铺满宽度 + 紧凑 / 标准 / 宽松 + 恢复默认；默认值与 1.5.0 逐像素一致 | 待验收 | 同一数据沙盒截图：1.5.0 与新版卡片边缘列 22–24 / 664–667 / 688–690、组名行完全相同（2026-11-06） |
| 58 | 调小间距 → 同样占格的卡片显示更多图标，图标保持 42；小分组占格变少 | 待验收 | 沙盒「紧凑」截图：4×2 卡片显示 18 个（原 8），AI +17→+7，开发 +27→+17 |
| 59 | 收起分组点「+N」/ 卡片空白处弹出文件夹视图，显示全部、底下不动；Esc / 点外面 / × 关闭；内部拖动排序、拖出、拖入文件、双击改名、右键；Ctrl+单击与右键「原地展开」保留 | 待验收 | UIA 调用「查看全部 AI」：25 项 7 列，卡片位置不变；紧凑下「开发」35 项 8 列；真实鼠标（用户同意）：拖第 1 个到第 4 个前 → 顺序变为 aeonvector、Ming、caelivern…；双击组名粘贴回车 → AI 改名「AI工具」；拖出到暗处停留后放到「邮件」→ 邮件 9→10 项（日志 quick place … in "邮件"） |
| 60 | 快捷键可含 Win：录制走低级钩子；系统占用的 Win 组合由钩子接管；Win+L 拒绝；面板内 Win 组合被占用时提示 | 待验收 | 根因：dotnet/wpf `KeyboardDevice.Modifiers` 只判 Alt/Ctrl/Shift；本机 RegisterHotKey 探测 Win+E/Q/Z/空格/Ctrl+Q/. 均被占用；真实按键（用户同意，沙盒 `--sandbox-hotkeys`）：Win+Shift+Q 系统注册→快捷面板开 / 再按关；Win+Q 钩子接管→打开路径窗口在前台、未弹出搜索 / 开始；录制框点击后按 Win+Alt+K → 显示「Alt + Win + K」并保存（9, 0x4B），重新注册走钩子；独立审查 4 项（录制前按住的键松开被吞导致卡键、绑定失败残留、移除再加文件夹会全导入、扫描中显式导入丢失）全修，卡键场景复测 Ctrl 松开后不再按住 |
| 61 | 监视「开始」菜单两个 Programs + 自选文件夹：新增快捷方式自动导入、高亮、分组（不受「自动分类」开关限制）；首次只记基线；删掉的不回来；卸载 / 说明类跳过；事件 + 10 分钟重扫 | 待验收 | 沙盒：基线 301 个 60 ms 不导入；新增 4 个 → 导入 2（字符映射表→其他应用、Remote Desktop→网络与远程），Uninstall / 说明跳过，132 ms |

## 明确不做
- [2026-11-05] 安卓手机 / 平板版、网页版（React、shadcn/ui、Motion、dnd-kit、Zustand 等前端栈）｜安卓、平板、React、shadcn、Web — 本项目是 WPF 桌面组件（ADR 0001），通用需求模板里的这些条目不适用；对应诉求用 WPF 自带控件实现（Thumb、拖放、动画）
- [2026-11-06] 真实鼠标 / 键盘测试（导航横向拖动、快捷面板角手柄与顶栏右键菜单、点击更新页链接）｜真实输入、注入鼠标、鼠标测试 — 用户未选；只用 UIA、SetWindowPos、日志等不接管输入的方式验证，这几项保持 NOT TESTED

## 问题与待决
- 程序未做 Authenticode 代码签名，SmartScreen 会提示未知发布者（需购买证书，暂不做）
- 镜像线路（ghfast.top、gh-proxy.com）为第三方，可用性随时变化；已有签名校验，失效时自动回退直连

## 恢复点
> 更早的恢复点与已完成轮次的验收见 `history/`（按版本分文件，最新 `2026-11_v1.5.0.md`）。
2026-11-06 第十四轮（v1.6.0）：间距（容量模型，默认与 1.5.0 逐像素一致）、文件夹视图、Win 快捷键（钩子录制 + 接管）、开始菜单自动导入，验收 57–61 均有实测证据，独立审查 4 项已修；发布后看已安装副本自动更新。
2026-11-06 第十三轮（v1.5.0）：已发布（提交 7057d03 + a3dc3e4，Releases 只留 v1.5.0 / v1.4.9，标签同），本地 bin/obj 与旧版产物已删，dist 只剩 v1.5.0。已安装副本 16:26 自动更新到 1.5.0+a3dc3e4，SHA-256 与 dist 一致，统计助手正常重启。已把 iTab 备份导入真实面板：161 个链接，新增 138，跳过 23（已有同名网页应用 / 重复），137 个分组（新增「学习」「招聘」两组），「玄云」留作未分组图标；真实图标后台补齐。未测：真实鼠标拖放（用户不选）。下一步：等用户验收。
<!-- wf:resume:start -->
（机器快照，PreCompact 钩子写，勿手改；恢复时以真实 Git 与代码为准）
- 快照时间：2026-11-06T04:28:40.283Z（PreCompact/auto，会话 a3dad127）
- Git：main → origin/main @ 0ff4c46（+0 -0）；未提交 39 个：.claude/SPEC.md，CHANGELOG.md，README.en.md，README.md，src/ZoneQuanta/App.xaml.cs，src/ZoneQuanta/AppController.cs，src/ZoneQuanta/Core/DataHistory.cs，src/ZoneQuanta/Core/Quick/GroupNamer.cs，src/ZoneQuanta/Core/Quick/QuickCategories.cs，src/ZoneQuanta/Core/Quick/QuickStore.cs，src/ZoneQuanta/Core/Settings/AppSettings.cs，src/ZoneQuanta/Core/Traffic/HelperLauncher.cs，src/ZoneQuanta/Core/Update/UpdateCoordinator.cs，src/ZoneQuanta/Core/Update/UpdateService.cs，src/ZoneQuanta/Platform/AppDirectory.cs…
- 最近 3 小时改过的文件（项目根，前 15）：.claude\history\2026-11_v1.4.8.md，.claude\SPEC.md，README.en.md，README.md，CHANGELOG.md，src\ZoneQuanta\ZoneQuanta.csproj，src\ZoneQuanta\UI\Theme\Theme.xaml，src\ZoneQuanta\UI\Quick\QuickWindow.cs，src\ZoneQuanta\Core\Update\UpdateCoordinator.cs，src\ZoneQuanta\Core\Update\UpdateService.cs，src\ZoneQuanta\Core\Update\ReleaseLinks.cs，src\ZoneQuanta\AppController.cs，src\ZoneQuanta\UI\Quick\QuickActions.cs，src\ZoneQuanta\Core\Quick\QuickStore.cs，src\ZoneQuanta\Core\DataHistory.cs
<!-- wf:resume:end -->

## 学习候选
- [2026-11-06] 注入真实鼠标 / 键盘的测试会抢用户的指针，用户会拒绝；先用 UIA、SetWindowPos、WM_CLOSE、日志等不接管输入的方法，真实输入测试先问｜复现 1 次｜用户中止 tquick2｜范围：全局
