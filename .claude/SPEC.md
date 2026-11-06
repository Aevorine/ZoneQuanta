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
| T-20261105-1 | v1.4.0 流量额度查询 + 快捷面板 | 主线 | 待验收 | 提交 c86a36e（v1.6.0）/ 43305aa / 9f886db / 6d2b054 / 0007227（v1.4.4）/ 7ea3356（v1.4.5）/ 074cddf（v1.4.6）/ 0ff4c46（v1.4.8）/ 39d08e5（v1.4.9）；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-05） |
| T-20261101-2 | v1.3.3 页面随窗口填满 | 主线 | 待验收 | 提交 82bd00e；证据见 `history/2026-11_v1.0-1.4.1.md`（2026-11-01） |

### T-20261105-1 第一轮验收（v1.4.0 / v1.4.1，1–6）见 `history/2026-11_v1.0-1.4.1.md`

### T-20261105-1 第二至第六轮验收（v1.4.2，7–23）见 `history/2026-11_v1.4.2-1.4.4.md`

### T-20261105-1 第七、第八轮验收（v1.4.4 / v1.4.5，24–31）见 `history/2026-11_v1.4.4-1.4.5.md`

### T-20261105-1 第九轮验收（v1.4.6 / v1.4.7，32–37）见 `history/2026-11_v1.4.6-1.4.7.md`

### T-20261105-1 第十轮验收（v1.4.8，38–39）见 `history/2026-11_v1.4.8.md`

### T-20261105-1 第十一、十二轮验收（v1.4.9，40–48）见 `history/2026-11_v1.4.9.md`

### T-20261105-1 第十三轮验收（v1.5.0，49–56）见 `history/2026-11_v1.5.0.md`

### T-20261105-1 第十四轮验收（v1.6.0，57–61）见 `history/2026-11_v1.6.0.md`

### T-20261105-1 第十五轮验收（v1.6.1，62–66）
> 用户反馈：快捷面板弹出的界面（文件夹视图）退出后再点，应用卡退。根因：文件夹视图关闭时没交还缓存的图标，再开同一分组时 WPF 拒绝同一图标有两个父级（`Folder.Fill` 抛 InvalidOperationException）；连点 11 次触发「1 分钟 10 次」崩溃保护 → 进程退出。v1.6.0 沙盒 UIA 复现：开 AI → × → 再开即 CRASH，再点 10 次进程消失（2026-11-06）。

| # | 验收条（可检查） | 状态 | 证据 |
|---|---|---|---|
| 62 | 文件夹视图任一方式关闭（×、Esc、点外面、拖出、原地展开、面板隐藏）后再开同一分组反复 20 次、12 个分组轮流开关，均正常显示；app.log 无 CRASH | 待验收 | 沙盒（真实数据副本，UIA + 只发给沙盒窗口的消息）：12 组各开关 2 次、AI 连开 20 次、关闭动画中立刻重开 ×5、原地展开后收起再开、面板隐藏再显示后重开 ×3、Esc 关后重开，共 64 次打开 CRASH 0、兜底 0 次；主面板「快捷」页另 9 次 CRASH 0；审查修复后最终版复跑同样 64 次 0（2026-11-06） |
| 63 | 三层兜底：图标复用前先脱离旧位置（日志记一次）；视图建不起来 → 记日志并改为原地展开；1 分钟内连续出错超过 10 次 → 保存后自动重启一次（不弹主面板），重启后 10 分钟内再连续出错才退出 | 待验收 | v1.6.0 原码 + 新保护逐层实测：仅重启保护 → 第 11 次出错日志「crash burst … restarting」，新进程 `--restart --from <pid> --recovered` 接管、不弹主面板，再次连续出错 →「exiting」且无残留进程；加原地展开兜底 → 「failed, unfolding in place」+ 卡片原格 0,0 展开、CRASH 0；加脱离旧父级 → 日志「taken back (folder)」一次、再开正常 |
| 64 | 文件夹内搜索：头部搜索框；直接打字即筛选；名称 / 路径 / 网址模糊匹配（与主搜索同一算法）；计数显示「匹配 / 总数」；回车打开第一个、↓ 进入图标、Esc 先清空再关闭、Ctrl+F 聚焦；面板大小不随输入跳动；筛选时组内拖动不改顺序 | 待验收 | 打字（WM_CHAR 发到沙盒窗口）「gpt」→ 框内 gpt、计数 1 / 25；无匹配 0 / 25；关闭按钮位置不变（面板不跳）；Esc 先清空（25）再关闭；真实按键（用户同意）：Ctrl+F 聚焦、↓ 回到图标、首行 ↑ 回到搜索框、回车打开第一个（日志 quick open）。NOT TESTED：搜索中拖动 |
| 65 | 不关闭切换分组：头部 ‹ › 与 Ctrl+← / → 循环切到上 / 下一个分组，内容滑入；只有一个分组时不显示 | 待验收 | UIA「下一个分组」×2、「上一个分组」×3：AI→购物→社交通讯→购物→AI→招聘（循环）；真实按键 Ctrl+→ / Ctrl+← / Ctrl+PageDown 同样切换；NOT TESTED：只有一个分组时隐藏 |
| 66 | 文件夹视图里右键头部 / 空白处 → 与卡片相同的分组菜单；「+」→ 添加文件 / 文件夹直接进本组；鼠标后退键关闭 | 待验收 | 右键标题（消息）→ 撤销 / 重做 / 重命名分组 / 卡片大小 / 原地展开 / 按内容命名 / 取消固定位置 / 全部自动排列 / 解散分组 / 自动分类；菜单内「重命名分组」改招聘→求职并写入 quick.json；「+ → 添加文件」对话框（WM_SETTEXT + IDOK）→ 邮件 9→10、分组=邮件、面板未收起；鼠标后退键（消息）关闭；独立审查 APPROVE，2 项小问题（撤销后改名丢失、切换布局时淡出视图未及时释放）已修 |

## 明确不做
- [2026-11-05] 安卓手机 / 平板版、网页版（React、shadcn/ui、Motion、dnd-kit、Zustand 等前端栈）｜安卓、平板、React、shadcn、Web — 本项目是 WPF 桌面组件（ADR 0001），通用需求模板里的这些条目不适用；对应诉求用 WPF 自带控件实现（Thumb、拖放、动画）
- [2026-11-06] 真实鼠标 / 键盘测试（导航横向拖动、快捷面板角手柄与顶栏右键菜单、点击更新页链接）｜真实输入、注入鼠标、鼠标测试 — 用户未选；只用 UIA、SetWindowPos、日志等不接管输入的方式验证，这几项保持 NOT TESTED

## 问题与待决
- 程序未做 Authenticode 代码签名，SmartScreen 会提示未知发布者（需购买证书，暂不做）
- 镜像线路（ghfast.top、gh-proxy.com）为第三方，可用性随时变化；已有签名校验，失效时自动回退直连

## 恢复点
> 更早的恢复点与已完成轮次的验收见 `history/`（按版本分文件，最新 `2026-11_v1.6.0.md`）。
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
