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
| T-20261105-1 | v1.4.0 流量额度查询 + 快捷面板 | 主线 | 待验收 | v1.7.0 / 提交 667ada8（v1.6.1）/ c86a36e（v1.6.0）/ 43305aa / 9f886db / 6d2b054 / 0007227（v1.4.4）/ 7ea3356（v1.4.5）/ 074cddf（v1.4.6）/ 0ff4c46（v1.4.8）/ 39d08e5（v1.4.9）；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-05） |
| T-20261101-2 | v1.3.3 页面随窗口填满 | 主线 | 待验收 | 提交 82bd00e；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-01） |

### T-20261105-1 第一轮验收（v1.4.0 / v1.4.1，1–6）见 `history/2026-11_v1.0-1.4.1.md`

### T-20261105-1 第二至第六轮验收（v1.4.2，7–23）见 `history/2026-11_v1.4.2-1.4.4.md`

### T-20261105-1 第七、第八轮验收（v1.4.4 / v1.4.5，24–31）见 `history/2026-11_v1.4.4-1.4.5.md`

### T-20261105-1 第九轮验收（v1.4.6 / v1.4.7，32–37）见 `history/2026-11_v1.4.6-1.4.7.md`

### T-20261105-1 第十轮验收（v1.4.8，38–39）见 `history/2026-11_v1.4.8.md`

### T-20261105-1 第十一、十二轮验收（v1.4.9，40–48）见 `history/2026-11_v1.4.9.md`

### T-20261105-1 第十三轮验收（v1.5.0，49–56）见 `history/2026-11_v1.5.0.md`

### T-20261105-1 第十四轮验收（v1.6.0，57–61）见 `history/2026-11_v1.6.0.md`

### T-20261105-1 第十五轮验收（v1.6.1，62–66）见 `history/2026-11_v1.6.1.md`

### T-20261105-1 第十六轮验收（v1.7.0，67–70）
> 用户反馈：① 快捷面板分组的图标与上下框线之间的距离「没有设置」；② 快捷面板弹出来的界面（文件夹视图）宽度跟着面板变窄、太小看不清，要增大并可手动设置、记住大小；图标大小不变。根因：① 卡片撑满格子、图标行平均分高度并居中，「上下边距」每加 d 行高减 2d、图标回移 d，单行卡片完全无变化（1.6.1 沙盒：边距 4 与 36 时「AI」组名胶囊都在 y=405）；② 文件夹视图沿用面板间距，用户「横向间距」= 0 → 每格 42 宽、视图约 408 宽（2026-11-06）。

| # | 验收条（可检查） | 状态 | 证据 |
|---|---|---|---|
| 67 | 卡片边框到首行图标顶、末行名称底 = 「上下边距」，行距 = 「纵向间距」，卡片高度贴合内容、名称紧贴其下；格子不够先压行距（≥ 8）再多占一行格子，不改设定的边距；默认间距下原布局基本不变；图标 42 不变 | 待验收 | 沙盒（真实数据副本，1100×760）：1.6.1 边距 4 / 36 → 「AI」胶囊 y 都是 405（无变化）；1.7.0 → 355 / 435，36 时截图量得边框到图标顶、名称底到边框约 36 DIP、行距 28；默认 → 两行卡片仅矮 4 px、单行卡片贴合；边距 36 时单行卡片多占一行格子而非缩小边距 |
| 68 | 文件夹视图格宽 / 行距独立于面板间距（标准 104 / 20），默认自动更大（约 3:2、≥ 640 宽），名称可读 | 待验收 | 用户设置（横向间距 0、面板最大化）：「AI」25 个 → 约 787 DIP 宽、7 列、名称完整（1.6.1 约 408 宽、格宽 42） |
| 69 | 拖动任一边 / 角调整大小（以中心对称，拖的边跟随指针），拖动中显示「宽 × 高」，松开记住；换分组、关了再开、重启后同样大小；双击边角恢复自动 | 待验收 | 只改沙盒线程的左键状态 + 只发给沙盒窗口的消息（真实指针未动）：右边 +100 px → 右边缘 +100 px，存 Custom 946×413；右下角 −150 / −100 px → 706×253，关了再开不变；双击 → Auto；截图可见尺寸徽标与滚动条 |
| 70 | 右键「视图大小」自动 / 小 / 中 / 大 / 铺满 / 自定义，「图标间距」紧凑 / 标准 / 宽松，选中即生效并写入设置 | 待验收 | 菜单（UIA）选「大」→ 1120×740，换「开发」(34 个) 同尺寸、重启后同尺寸；「宽松」写入 settings.json。NOT TESTED：小 / 中 / 铺满 / 紧凑逐项点击（与大 / 宽松同一路径） |

## 明确不做
- [2026-11-05] 安卓手机 / 平板版、网页版（React、shadcn/ui、Motion、dnd-kit、Zustand 等前端栈）｜安卓、平板、React、shadcn、Web — 本项目是 WPF 桌面组件（ADR 0001），通用需求模板里的这些条目不适用；对应诉求用 WPF 自带控件实现（Thumb、拖放、动画）
- [2026-11-06] 真实鼠标 / 键盘测试（导航横向拖动、快捷面板角手柄与顶栏右键菜单、点击更新页链接）｜真实输入、注入鼠标、鼠标测试 — 用户未选；只用 UIA、SetWindowPos、日志等不接管输入的方式验证，这几项保持 NOT TESTED

## 问题与待决
- 程序未做 Authenticode 代码签名，SmartScreen 会提示未知发布者（需购买证书，暂不做）
- 镜像线路（ghfast.top、gh-proxy.com）为第三方，可用性随时变化；已有签名校验，失效时自动回退直连

## 恢复点
> 更早的恢复点与已完成轮次的验收见 `history/`（按版本分文件，最新 `2026-11_v1.6.1.md`）。
2026-11-06 第十六轮（v1.7.0）：分组卡片「上下边距」精确生效（BoardMetrics.CardRows：卡片贴合内容、先压行距再多占一行格子、不改边距），文件夹视图独立格宽 / 行距（FolderMetrics）、默认更大、边角拖动调整并记住（QuickFolderSize / Width / Height / Density）、右键「视图大小」「图标间距」；验收 67–70 有沙盒实测证据（按用户要求本轮不做独立审查）。下一步：发布 v1.7.0 后等用户验收。
<!-- wf:resume:start -->
（机器快照，PreCompact 钩子写，勿手改；恢复时以真实 Git 与代码为准）
- 快照时间：2026-11-06T04:28:40.283Z（PreCompact/auto，会话 a3dad127）
- Git：main → origin/main @ 0ff4c46（+0 -0）；未提交 39 个：.claude/SPEC.md，CHANGELOG.md，README.en.md，README.md，src/ZoneQuanta/App.xaml.cs，src/ZoneQuanta/AppController.cs，src/ZoneQuanta/Core/DataHistory.cs，src/ZoneQuanta/Core/Quick/GroupNamer.cs，src/ZoneQuanta/Core/Quick/QuickCategories.cs，src/ZoneQuanta/Core/Quick/QuickStore.cs，src/ZoneQuanta/Core/Settings/AppSettings.cs，src/ZoneQuanta/Core/Traffic/HelperLauncher.cs，src/ZoneQuanta/Core/Update/UpdateCoordinator.cs，src/ZoneQuanta/Core/Update/UpdateService.cs，src/ZoneQuanta/Platform/AppDirectory.cs…
- 最近 3 小时改过的文件（项目根，前 15）：.claude\history\2026-11_v1.4.8.md，.claude\SPEC.md，README.en.md，README.md，CHANGELOG.md，src\ZoneQuanta\ZoneQuanta.csproj，src\ZoneQuanta\UI\Theme\Theme.xaml，src\ZoneQuanta\UI\Quick\QuickWindow.cs，src\ZoneQuanta\Core\Update\UpdateCoordinator.cs，src\ZoneQuanta\Core\Update\UpdateService.cs，src\ZoneQuanta\Core\Update\ReleaseLinks.cs，src\ZoneQuanta\AppController.cs，src\ZoneQuanta\UI\Quick\QuickActions.cs，src\ZoneQuanta\Core\Quick\QuickStore.cs，src\ZoneQuanta\Core\DataHistory.cs
<!-- wf:resume:end -->

## 学习候选
- [2026-11-06] 注入真实鼠标 / 键盘的测试会抢用户的指针，用户会拒绝；先用 UIA、SetWindowPos、WM_CLOSE、日志等不接管输入的方法，真实输入测试先问｜复现 1 次｜用户中止 tquick2｜范围：全局
- [2026-11-06] 会缓存 WPF 元素的视图（文件夹视图、卡片）关闭时必须把元素从旧父级移除（Children.Clear），否则再次打开同一对象时 Add 抛「already the logical child」；验收要测「关了再开同一个」，不能每次开不同的｜复现 1 次｜v1.6.0 沙盒测试轮流开不同分组，漏掉同一分组再开即崩，用户反馈「退出后再点卡退」｜范围：项目
- [2026-11-06] 测键盘 / 鼠标交互不必注入全局输入：PostMessage 把 WM_KEYDOWN / WM_CHAR / WM_RBUTTONUP / WM_XBUTTONDOWN 只发给沙盒窗口，WPF 照常处理（Esc、打字、右键菜单、后退键都验证通过），用户指针不动；Ctrl / Shift 组合不行（WPF 读 GetKeyState）；通用对话框用 WM_SETTEXT + WM_COMMAND IDOK｜复现 1 次｜第十五轮 64–66 全部这样验证｜范围：全局
