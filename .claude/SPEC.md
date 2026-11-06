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

### T-20261105-1 第九轮验收（v1.4.6 / v1.4.7，32–37）见 `history/2026-11_v1.4.6-1.4.7.md`

### T-20261105-1 第十轮验收（v1.4.8，导航图标拖动即移动、流量页真实图标）
38. 按住导航图标一移动（超过系统拖动阈值）就是调整顺序：图标浮起跟随指针、其他图标让位、松开保存，窗口不动；收起为仅图标时同样；单击仍切换页面；拖动导航栏标志 / 空白处 / 版本号移动窗口；Alt + ↑ / ↓ 移动当前页图标；顺序重启后保持
39. 「流量」页应用显示真实图标：已退出程序也能从运行记录（兼容性助手、MuiCache、BAM）、商店清单、系统文件夹、安装目录找到程序文件；路径必须是「进程名.exe」；同目录多快捷方式选最合适的；系统程序不归属开始菜单项；无图标的程序显示资源管理器同款图标；快速来源约 1 秒出图标，安装目录查找在后台低优先级、有上限、结果记一天

## 明确不做
- [2026-11-05] 安卓手机 / 平板版、网页版（React、shadcn/ui、Motion、dnd-kit、Zustand 等前端栈）｜安卓、平板、React、shadcn、Web — 本项目是 WPF 桌面组件（ADR 0001），通用需求模板里的这些条目不适用；对应诉求用 WPF 自带控件实现（Thumb、拖放、动画）

## 问题与待决
- 程序未做 Authenticode 代码签名，SmartScreen 会提示未知发布者（需购买证书，暂不做）
- 镜像线路（ghfast.top、gh-proxy.com）为第三方，可用性随时变化；已有签名校验，失效时自动回退直连

## 恢复点
> 更早的恢复点见 `history/2026-10_11.md`、`history/2026-11_v1.0-1.4.1.md`、`history/2026-11_v1.4.2-1.4.4.md`、`history/2026-11_v1.4.4-1.4.5.md`、`history/2026-11_v1.4.6-1.4.7.md`。
2026-11-06 第十轮（v1.4.8）：用户再次提出导航图标难移动与流量页图标不真实。导航根因：v1.4.4 起导航项上的拖动先交给窗口移动，只有静止 0.3 秒才浮起排序 → 改为越过拖动阈值即浮起排序，窗口改由标志 / 空白 / 版本号拖动，新增窗口级 Alt+↑/↓（导航项 Focusable=False，导航级按键收不到）。图标根因（逐项分析 apps-meta.json 前 80 名）：27 个 v1.4.6 前退出的程序无路径、同目录多快捷方式时归属放弃、系统程序画成 Windows 标志、App Paths / 旧缓存把 wpp 指到 wps.exe → 新增 `Platform/ExecutableIndex`（商店清单、兼容性助手、MuiCache、系统文件夹含 UUS、助手写的 apps-seen.tsv（BAM，仅当前用户、同名取最近运行）、两阶段 + 广度优先有预算的安装目录查找，结果记 exe-index.tsv 一天，预算截断的未找到 1 小时后再查）、`Platform/InstalledPrograms`（应用和功能图标）、`Matching`（必须是 <name>.exe）、Windows 文件不归属开始菜单项、无图标用文件本身的系统图标、未知名称退避重试、Revision 3。测试中发现并修复回归：System32 文件被「任意快捷方式在其中」规则归到「系统信息」。隔离实例真实输入：导航 6 项通过；流量页 88 名 1.15–1.4 秒出图标、后台查找补 10–11 个、剩 7 个已不存在。独立审查 REQUEST_CHANGES 2 条已修。v1.4.6 / v1.4.7 的恢复点见 `history/2026-11_v1.4.6-1.4.7.md`。
<!-- wf:resume:start -->
（机器快照，PreCompact 钩子写，勿手改；恢复时以真实 Git 与代码为准）
- 快照时间：2026-11-05T13:45:38.336Z（PreCompact/auto，会话 e7b7b0cb）
- Git：main → origin/main @ 12f1700（+0 -0）；未提交 0 个
- 最近 3 小时改过的文件（项目根，前 15）：.claude\SPEC.md，src\ZoneQuanta\ZoneQuanta.csproj，CHANGELOG.md，src\ZoneQuanta\Core\Traffic\HelperLauncher.cs，src\ZoneQuanta\Core\Traffic\TrafficHelper.cs，.claude\privacy-allow.txt，src\ZoneQuanta\UI\Quick\QuickGrid.cs，src\ZoneQuanta\AppController.cs，src\ZoneQuanta\UI\Quick\StartPinSync.cs，README.en.md，README.md，src\ZoneQuanta\UI\Panel\NavReorder.cs，src\ZoneQuanta\UI\Panel\PanelWindow.xaml.cs，src\ZoneQuanta\UI\Quick\QuickGrid.Board.cs，src\ZoneQuanta\UI\Theme\Theme.xaml
<!-- wf:resume:end -->

## 学习候选
- [2026-11-06] WPF 同一 Visual 换 HwndSource 宿主：先清旧 RootVisual，再给每个宿主套新外层容器；宿主随任务栏被销毁时根标记可能残留，复用即抛 VisualTarget 异常并退出｜复现 2 次（Windows 事件日志）｜开机登录时程序消失｜范围：全局
- [2026-11-06] 真实数据测试不能「测前备份、测后写回」，会覆盖用户在测试期间的改动；一律在隔离数据目录（副本）上测｜复现 1 次｜用户分组被恢复原位｜范围：全局
- [2026-11-05] WPF 把鼠标捕获从子元素转给父元素时，子元素的 LostMouseCapture 会冒泡到父元素，父元素处理器要判 e.OriginalSource == 自己｜复现 1 次｜长按导航排序第一次实测时刚浮起就被自己取消｜范围：全局
- [2026-11-05] 代码里引用主题资源先 grep 确认键存在，或用 TryFindResource；FindResource 缺键会在窗口构造时让整个程序崩溃｜复现 1 次｜Icon.Refresh 不存在导致 --panel 启动即崩｜范围：项目
- [2026-11-05] 真实输入测试的指针检查要核对目标顶层窗口句柄，不能只核对进程：同进程的置顶窗口会盖住目标｜复现 1 次｜右键落在快捷面板而不是主面板上｜范围：全局
