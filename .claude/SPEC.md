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
| T-20261105-1 | v1.4.0 流量额度查询 + 快捷面板 | 主线 | 待验收 | 提交 43305aa / 9f886db / 6d2b054 / 0007227（v1.4.4）/ 7ea3356（v1.4.5）/ 074cddf（v1.4.6）/ v1.4.7 见 CHANGELOG；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-05） |
| T-20261101-2 | v1.3.3 页面随窗口填满 | 主线 | 待验收 | 提交 82bd00e；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-01） |

### T-20261105-1 第一轮验收（v1.4.0 / v1.4.1，1–6）见 `history/2026-11_v1.0-1.4.1.md`

### T-20261105-1 第二至第六轮验收（v1.4.2，7–23）见 `history/2026-11_v1.4.2-1.4.4.md`

### T-20261105-1 第七、第八轮验收（v1.4.4 / v1.4.5，24–31）见 `history/2026-11_v1.4.4-1.4.5.md`

### T-20261105-1 第九轮验收（v1.4.6，组合不再被恢复、流量真实图标、页面拖动、导航收起、重启与更新后记住）
32. 用户的分组 / 卡片位置 / 导航顺序不会被测试或程序改回：测试只在 `--data-dir` 隔离实例上进行（独立单实例名，不注册快捷键与右键菜单、不启动统计助手、不安装更新）；启动、退出、更新前、恢复前与改动后 20 秒自动保存历史版本（gzip、去重、保留最近 60 + 30 天每天 1 份）；「系统」页「历史版本」两步确认恢复并自动重启；快捷面板 Ctrl+Z / Ctrl+Y 撤销重做 50 步、右键同样可用；不认识的字段原样保留
33. 「流量」页每个应用显示真实图标：助手在进程启动时记录完整路径与商店应用身份；路径来源依次为助手记录、运行中进程、App Paths / 服务、开始菜单、PATH；自身无图标的程序用所属安装目录的应用图标（xray、sing-box 等内核 → 主程序）；「进程 / 应用」切换，应用模式合并同一应用的进程；右键打开文件位置 / 复制路径 / 属性
34. 所有页面内容可拖动：直接拖动移动面板（单击、滑块、列表、输入框不受影响）；按住约 0.3 秒再拖调整卡片 / 开关行位置，写入 settings.json `Layouts`，重启与更新后保持；空白处右键「恢复默认布局」
35. 导航栏可收起为仅图标（‹ 按钮 / Ctrl+B，平滑过渡，记住状态）；指针停在图标上显示功能名称与该页内容说明
36. 设置在重启与自动更新后保持：已安装版自更新到 v1.4.6 后，用户 settings.json / quick.json 的原有内容不变（只新增字段），分组、卡片位置、导航顺序与更新前一致
37. 任务栏重建（开机登录、资源管理器重启）时程序不退出，监控条重新出现（v1.4.7；源自用户电脑 2026-11-06 09:55:34 的 .NET Runtime 崩溃记录）

## 明确不做
- [2026-11-05] 安卓手机 / 平板版、网页版（React、shadcn/ui、Motion、dnd-kit、Zustand 等前端栈）｜安卓、平板、React、shadcn、Web — 本项目是 WPF 桌面组件（ADR 0001），通用需求模板里的这些条目不适用；对应诉求用 WPF 自带控件实现（Thumb、拖放、动画）

## 问题与待决
- 程序未做 Authenticode 代码签名，SmartScreen 会提示未知发布者（需购买证书，暂不做）
- 镜像线路（ghfast.top、gh-proxy.com）为第三方，可用性随时变化；已有签名校验，失效时自动回退直连

## 恢复点
> 更早的恢复点见 `history/2026-10_11.md`、`history/2026-11_v1.0-1.4.1.md`、`history/2026-11_v1.4.2-1.4.4.md`、`history/2026-11_v1.4.4-1.4.5.md`。
2026-11-06 第九轮（v1.4.6）：用户问组合后的内容为何被恢复原位、流量页部分应用图标不对、页面内容不能拖、要导航可收起与悬停说明、要重启与更新后记住设置。组合被恢复根因：第八轮真实输入测试「测前备份、测后逐字节写回」用户数据，覆盖了测试期间用户自己的分组与导航改动（程序无此行为，日志仍有用户操作）；无可恢复副本。修法：新增 `Core/DataPaths`（`--data-dir` / `ZONEQUANTA_DATA` 隔离实例，`WritesFrozen`）、`Core/DataHistory`（历史版本 + 恢复）、QuickStore 撤销 / 重做与 `[JsonExtensionData]`、逐操作日志。流量图标根因：显示时才按进程名找文件，退出进程与 xray / sing-box 等无图标内核找不到或只得到默认图标；修法：TrafficHelper 加 Kernel-Process 启停事件写 `apps-paths.tsv`，新增 `Platform/ProcessImages`、`Platform/AppIdentity`（开始菜单索引、安装目录归属、商店应用清单），`AppDirectory` 多来源重写，TrafficPage 进程 / 应用切换。页面拖动：`UI/Controls/Arrange`（Arrange.Key / Id、FLIP 动画、`ContentDrag`）。导航：`UI/Controls/SideNav` + `AppSettings.NavCollapsed`、导航提示带页面说明。上一会话测完 1–6 项（全部在隔离实例上）后因额度中断，本会话补做独立审查（REQUEST_CHANGES 8 条全修：时区改名后按住行崩溃 + 全局异常记录、撤销丢失「开始」同步项、快照读取阻塞保存、重启误杀其他实例改 `--from <pid>`、历史当前行按哈希、历史卡不可拖入自适应行、提权助手拒写链接路径、`--data-dir` 严格校验），另从日志发现隔离实例与真实实例共用「开始」布局临时文件，已按实例分开。冒烟测试（隔离实例）：坏 `--data-dir` 退出码 2；`--restart --from` 只结束目标 pid，用户实例与助手不受影响。v1.4.6 已发布（提交 074cddf），Releases 只剩 v1.4.6 / v1.4.5，远端标签同；本地 dist 只有 v1.4.6，bin / obj 与旧会话测试数据已删。验收 36：已安装版自更新 1.4.5→1.4.6 约 46 秒，SHA-256 与 dist 一致；与更新前只读基线比对：111 项顺序 / 名称 / 分组 / 手动标记、17 组名称 / 大小 / 位置、导航顺序全部一致，设置无丢失（用户在基线后自己开了流量额度 1500 GB，更新后保留）。更新后首启日志 `CRASH ui … VisualTarget cannot have a parent`（BandWindow.EnsureHost），查 Windows 应用程序日志：同一异常在 09:55:34 已让用户的 v1.4.5 直接退出 → v1.4.7：每个宿主窗口用新外层容器作根、换宿主前分离并清空旧根、失败时释放新窗口重试；隔离实例上外部关闭监控条窗口 + 显示任务栏未能触发重建（新旧版本都没有），重建路径未做端到端（真触发需重启资源管理器）。
<!-- wf:resume:start -->
（机器快照，PreCompact 钩子写，勿手改；恢复时以真实 Git 与代码为准）
- 快照时间：2026-11-05T13:45:38.336Z（PreCompact/auto，会话 e7b7b0cb）
- Git：main → origin/main @ 12f1700（+0 -0）；未提交 0 个
- 最近 3 小时改过的文件（项目根，前 15）：.claude\SPEC.md，src\ZoneQuanta\ZoneQuanta.csproj，CHANGELOG.md，src\ZoneQuanta\Core\Traffic\HelperLauncher.cs，src\ZoneQuanta\Core\Traffic\TrafficHelper.cs，.claude\privacy-allow.txt，src\ZoneQuanta\UI\Quick\QuickGrid.cs，src\ZoneQuanta\AppController.cs，src\ZoneQuanta\UI\Quick\StartPinSync.cs，README.en.md，README.md，src\ZoneQuanta\UI\Panel\NavReorder.cs，src\ZoneQuanta\UI\Panel\PanelWindow.xaml.cs，src\ZoneQuanta\UI\Quick\QuickGrid.Board.cs，src\ZoneQuanta\UI\Theme\Theme.xaml
<!-- wf:resume:end -->

## 学习候选
- [2026-11-06] 真实数据测试不能「测前备份、测后写回」，会覆盖用户在测试期间的改动；一律在隔离数据目录（副本）上测｜复现 1 次｜用户分组被恢复原位｜范围：全局
- [2026-11-05] WPF 把鼠标捕获从子元素转给父元素时，子元素的 LostMouseCapture 会冒泡到父元素，父元素处理器要判 e.OriginalSource == 自己｜复现 1 次｜长按导航排序第一次实测时刚浮起就被自己取消｜范围：全局
- [2026-11-05] 代码里引用主题资源先 grep 确认键存在，或用 TryFindResource；FindResource 缺键会在窗口构造时让整个程序崩溃｜复现 1 次｜Icon.Refresh 不存在导致 --panel 启动即崩｜范围：项目
- [2026-11-05] 真实输入测试的指针检查要核对目标顶层窗口句柄，不能只核对进程：同进程的置顶窗口会盖住目标｜复现 1 次｜右键落在快捷面板而不是主面板上｜范围：全局
