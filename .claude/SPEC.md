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

### T-20261105-1 第十一、十二轮验收（v1.4.9）
40–45（多方法真实图标、助手自愈、导航拖窗、快捷面板大小、自动分组第一轮、更新页网址）见 `history/2026-11_v1.4.9.md`。
第十二轮（同一版本 v1.4.9，用户再次要求分组分类与名称彻底准确）：
46. 分类依据逐级换用：名称 → 网站（主机、路径、# 栏目）→ 程序名 / 描述 / 备注 → 产品自述（应用和功能说明、Electron 包描述）→ 系统登记由它打开的文件类型与协议 → 文件夹 → 附带说明文档开头；长文本打分；学习手动放置的细类。检查：真实数据副本 102 项重分类后无「其他应用」「网站」组、手动项 0 移动、日志写明每项依据（`scratchpad/explain.ps1`、`tgroup3.ps1`）
47. 组名按实际成员：同细类 → 细类名，两段式大类只占一段 → 那一段（办公 / 学习、网盘 / 下载、网络工具 / 远程、嵌入式 / 工程），两类 → 短「A与B」/ 上位词 / 常用；自动组绑定类别，改名后同类新项仍进入，用户输入的名字不改。检查：`tnames3.ps1` 21 例；副本去掉下载工具与学习通后组名自动变「网盘」「办公」，项目加回后进同一组且名字变回（`tgroup3b/3c.ps1`）
48. 审查修正：图标图片先查格式与头部尺寸、卡住的服务器 25 s 内放弃、离线 15 分钟重试、不访问本机与局域网（含跳转）、跨屏窗口尺寸只在原屏保存、流量额度推算日期溢出不再打断启动。检查：`fixtest`（大图 14 ms 拒绝 +0 MB、10 个内网地址拒绝、不可达主机不进 1 天名单、卡住 3 s 放弃）

## 明确不做
- [2026-11-05] 安卓手机 / 平板版、网页版（React、shadcn/ui、Motion、dnd-kit、Zustand 等前端栈）｜安卓、平板、React、shadcn、Web — 本项目是 WPF 桌面组件（ADR 0001），通用需求模板里的这些条目不适用；对应诉求用 WPF 自带控件实现（Thumb、拖放、动画）
- [2026-11-06] 真实鼠标 / 键盘测试（导航横向拖动、快捷面板角手柄与顶栏右键菜单、点击更新页链接）｜真实输入、注入鼠标、鼠标测试 — 用户未选；只用 UIA、SetWindowPos、日志等不接管输入的方式验证，这几项保持 NOT TESTED

## 问题与待决
- 程序未做 Authenticode 代码签名，SmartScreen 会提示未知发布者（需购买证书，暂不做）
- 镜像线路（ghfast.top、gh-proxy.com）为第三方，可用性随时变化；已有签名校验，失效时自动回退直连

## 恢复点
> 更早的恢复点与已完成轮次的验收见 `history/`（按版本分文件，最新 `2026-11_v1.4.9.md`）。
2026-11-06 第十一 / 十二轮（v1.4.9）：已发布（提交 39d08e5，Releases 只留 v1.4.9 / v1.4.8）。已安装副本 13:30 自动更新到 1.4.9，SHA-256 与签名清单一致；1.4.8 留下的统计助手 pid 9264 被自动结束、新助手起来（`helper recent runs 86`）；真实快捷面板重分类 12 项、手动项 10 个未动、「其他应用」「网站」消失，含手动项的 4 个类别组只绑定不改名。两次独立审查共 9 个问题已修并实测。旧 1.4.8 进程退出时仍记一条 System.Diagnostics.Tracing CRASH（旧版缺陷，1.4.9 起不再发生）。未测：真实鼠标拖动 / 手柄 / 菜单（用户不选，见「明确不做」）、双屏窗口尺寸。下一步：等用户验收。
<!-- wf:resume:start -->
（机器快照，PreCompact 钩子写，勿手改；恢复时以真实 Git 与代码为准）
- 快照时间：2026-11-06T04:28:40.283Z（PreCompact/auto，会话 a3dad127）
- Git：main → origin/main @ 0ff4c46（+0 -0）；未提交 39 个：.claude/SPEC.md，CHANGELOG.md，README.en.md，README.md，src/ZoneQuanta/App.xaml.cs，src/ZoneQuanta/AppController.cs，src/ZoneQuanta/Core/DataHistory.cs，src/ZoneQuanta/Core/Quick/GroupNamer.cs，src/ZoneQuanta/Core/Quick/QuickCategories.cs，src/ZoneQuanta/Core/Quick/QuickStore.cs，src/ZoneQuanta/Core/Settings/AppSettings.cs，src/ZoneQuanta/Core/Traffic/HelperLauncher.cs，src/ZoneQuanta/Core/Update/UpdateCoordinator.cs，src/ZoneQuanta/Core/Update/UpdateService.cs，src/ZoneQuanta/Platform/AppDirectory.cs…
- 最近 3 小时改过的文件（项目根，前 15）：.claude\history\2026-11_v1.4.8.md，.claude\SPEC.md，README.en.md，README.md，CHANGELOG.md，src\ZoneQuanta\ZoneQuanta.csproj，src\ZoneQuanta\UI\Theme\Theme.xaml，src\ZoneQuanta\UI\Quick\QuickWindow.cs，src\ZoneQuanta\Core\Update\UpdateCoordinator.cs，src\ZoneQuanta\Core\Update\UpdateService.cs，src\ZoneQuanta\Core\Update\ReleaseLinks.cs，src\ZoneQuanta\AppController.cs，src\ZoneQuanta\UI\Quick\QuickActions.cs，src\ZoneQuanta\Core\Quick\QuickStore.cs，src\ZoneQuanta\Core\DataHistory.cs
<!-- wf:resume:end -->

## 学习候选
- [2026-11-06] SocketsHttpHandler.ConnectCallback 经代理时拿到的是 CONNECT 隧道请求，按目标判断会把本机代理当局域网拒绝；用 Method==CONNECT 识别｜复现 1 次｜联网图标在代理下全部失败｜范围：全局
- [2026-11-06] 全局 DispatcherUnhandledException 吞掉退出期异常会让进程永远不退（单文件 exe 被替换后懒加载程序集失败）；后台 / 助手模式不装吞异常处理器，工作结束直接 Environment.Exit｜复现 1 次｜自更新后提权助手成僵尸、流量停止统计｜范围：全局
- [2026-11-06] 记住的窗口尺寸比工作区还大时边缘够不着、用户无法改尺寸、也就永远不会重新保存；恢复时先按工作区收缩并保存｜复现 1 次｜快捷面板「不记得大小」｜范围：全局
- [2026-11-06] 注入真实鼠标 / 键盘的测试会抢用户的指针，用户会拒绝；先用 UIA、SetWindowPos、WM_CLOSE、日志等不接管输入的方法，真实输入测试先问｜复现 1 次｜用户中止 tquick2｜范围：全局
