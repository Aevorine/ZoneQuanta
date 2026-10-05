# 更新日志 · Changelog

## 1.2.16 — 2026-10-05
- 中文：任务栏监控条字体改为根据实际文字尺寸、安全区域宽度与任务栏高度选择最大可用字号；取消固定宽列和只缩小限制，剩余宽度均分到可见项，减少留白。保留图标避让与自动隐藏跟随，缓存字体测量结果。
- English: taskbar text now uses the largest font fitting the measured text, safe gap width and taskbar height. Removed fixed wide columns and shrink-only scaling; remaining width is shared by visible items. Preserved icon avoidance and auto-hide tracking, with cached text measurements.

## 1.2.15 — 2026-10-05
- 中文：任务栏监控条改为根据开始、应用、搜索和通知区按钮的实际边界选择空闲区域，并按可用宽度缩小；位置微调限制在空闲区域，布局变化时先收起再重新计算。布局识别放到后台，空间不足或布局不可靠时暂时隐藏，避免遮住按钮。
- English: the band now selects free taskbar space from actual control bounds, scales to the available width and clamps position offsets to the safe gap. Layout changes hide the old position before background recalculation. Insufficient space or an unrecognized layout hides the band rather than covering buttons.

## 1.2.14 — 2026-10-05
- 中文：下载与总速之间新增默认开启的今日流量（上传 + 下载），监控页增加独立开关与实时预览；流量逐次入账，显示不再等待十秒提交。任务栏跟随从每秒采样改为 Windows 窗口事件 + 16 ms 位置检查，按任务栏所在屏幕裁剪，隐藏后无残留，支持 Explorer 重启后重新连接。补充包元数据、中英文说明与隐私排除规则。
- English: added today’s recorded upload + download usage between download and total speed, enabled by default with its own switch and live preview. Samples update the in-memory totals immediately. Taskbar tracking now uses Windows window events plus a 16 ms geometry fallback instead of the one-second sampling tick, clips to the taskbar monitor and reconnects after Explorer restarts. Updated package metadata, bilingual documentation and privacy exclusions.

## 1.2.13
- 中文：修复锁定位置后桌面组件仍会偏离设定位置的问题——位置改为由「你选定的坐标 / 九宫格贴边」直接决定，文字、缩放、翻面等引起的尺寸变化不再挪动窗口，也不再改写已保存的坐标；多显示器与分辨率变化后按同一设定重新定位。任务栏 ZoneQuanta 显示区现在可右键直接打开设置界面。
- English: fixed the desktop widget drifting away from its chosen spot even when position is locked — the position is now derived from the coordinates or edge/corner you picked, so size changes (text, scale, card flip) no longer move the window or overwrite the saved coordinates; display changes re-place it from the same setting. Right-clicking the ZoneQuanta area on the taskbar now opens the settings window.

## 1.2.12
- 中文：桌面时间组件改为紧凑样式——只保留名称、带秒数的大号时间、一条细 24 小时日轴和关键标记（昨天 / 明天、相对本地的时差、夏令时），窗口面积约缩小一半；完整信息仍可点击卡片翻面查看。修复日轴向顶层窗口索取过宽空间的问题。
- English: the desktop time widget now uses a compact layout — only the name, a large time with seconds, a slim 24-hour ribbon and key marks (yesterday / tomorrow, offset from local, DST) — cutting the window area by about half; the full details are still one click away on the card back. Fixed the ribbon claiming too much width in a top-level window.

## 1.2.11
- 中文：时间组件不再使用圆形钟面，改为横贯卡片的 24 小时日轴——日照段按真实日出日落着色，已过去的时间高亮、未到的时间变暗，太阳 / 月亮标记在当前时刻，日出日落时间标在轴上，鼠标移上去可读出任意时刻。
- English: the time widget no longer uses a round clock face; it now shows a 24-hour day ribbon across the card — the daylight segment follows real sunrise and sunset, the elapsed part is bright and the rest dimmed, a sun / moon marker sits on the current time, sunrise and sunset are printed on the ribbon, and hovering reads any moment.

## 1.2.10
- 中文：找到并修复「更新失败，请稍后重试」的真正根因——单文件程序替换自身之后，旧进程再需要加载尚未载入的系统程序集就会抛 `FileNotFoundException`，导致旧进程无法退出、占着单实例锁。现在先完成全部退出清理，再替换文件，随后立即启动新版本并强制结束旧进程；新版本若等不到旧进程退出，会主动结束残留的旧进程并接管；安装失败时自动重启当前版本并在托盘提示原因。已用「本地签名更新服务器」做过完整的真实更新测试（含分段下载失败后退回单线路下载）。
- English: found and fixed the real cause of "update failed, please retry" — after a single-file app replaces its own executable, the old process throws `FileNotFoundException` as soon as it needs an assembly it has not loaded yet, so it never exits and keeps the single-instance lock. All shutdown work now finishes first, then the files are replaced, the new version is started and the old process is ended immediately; if the new version cannot get the lock it ends the leftover old process and takes over; on failure the current version relaunches and a tray notice explains why. Verified end to end against a local signed update server (including the fallback from segmented to single-stream download).

## 1.2.9
- 中文：彻底修复「更新失败，请稍后重试」——替换成功后无论发生什么旧进程都会退出（不再残留占用单实例锁与旧文件导致连环失败）；下载增加整段重试、单线路兜底与已下载包复用；文件被杀毒软件短暂占用时自动重试；各阶段失败给出明确原因。降低常驻开销：任务栏位置查找限流、悬停详情去掉阴影特效、置顶重申降频、空闲时自动回收内存；关闭动画循环，改为随秒针节拍驱动。
- English: fixed "update failed, please retry" for good — after the executable is replaced the old process always exits (no more leftover process holding the single-instance lock and old file); downloads gain whole-run retries, a single-stream fallback and reuse of an already downloaded package; transient file locks (e.g. antivirus) are retried; every stage reports a clear reason. Lower resident cost: throttled taskbar position lookup, no shadow effect on the hover details, less frequent z-order refresh, idle memory trimming, and no free-running animation loop (driven by the second tick instead).

## 1.2.8
- 中文：修复在线更新后程序没有自动重新启动的问题——新版本启动时旧版本仍占用单实例锁，导致新进程立即退出；现在新进程会等待旧进程退出后接管，更新完成即自动运行。
- English: fixed the app not restarting after an online update — the new process started while the old one still held the single-instance lock and exited immediately; it now waits for the old process to exit and takes over, so the app is running as soon as the update finishes.

## 1.2.7
- 中文：修复回到桌面（Win+D、三指下滑、点击桌面）后时钟组件被桌面层盖住而不显示的问题——桌面在前台时组件自动浮到桌面之上，离开桌面后恢复原有层级；显示器 / 分辨率变化、解锁、远程重连后自动校正位置与层级；自更新在旧文件被占用时不再中断，日志记录更详细。
- English: fixed the clock widget being hidden behind the desktop layer after returning to the desktop (Win+D, three-finger swipe, clicking the desktop) — it now rises above the desktop while the desktop is in front and returns to its configured layer afterwards; position and layer are re-corrected after display / resolution changes, unlock and remote reconnect; self-update no longer aborts when an old file is locked, and logs are more detailed.

## 1.2.6
- 中文：时间图改为标准 24 小时表盘（0 点在上、顺时针，标 0 / 6 / 12 / 18，每小时一格），时针、分针、秒针精确指示当前时间；日照段仍按真实日出日落着色，表盘放大便于读数。
- English: the time graphic is now a standard 24-hour dial (0 at the top, clockwise, labelled 0 / 6 / 12 / 18, one tick per hour) with hour, minute and second hands for an exact reading; the daylight segment still follows real sunrise and sunset, and the dial is larger for readability.

## 1.2.5
- 中文：全新应用图标——24 小时表盘（日照段 / 夜间段 / 太阳标记 / 指针）加三格信号条角标，对应世界时钟与系统监控；角标位可随后续功能扩展。
- English: new app icon — a 24-hour dial (daylight / night segments, sun marker, hand) with a three-bar signal badge for the world clock and system monitor; the badge slot can grow with future features.

## 1.2.4
- 中文：时间图改为 24 小时表盘（正午在上、午夜在下），日照段按真实日出日落着色，指针指向当前时刻；卡片悬停有 3D 倾斜与高光；鼠标停在任务栏监控条上弹出详细信息（网络、今日流量、CPU、内存、开机时长、流量排行）。
- English: the time graphic is now a 24-hour dial (noon at the top, midnight at the bottom) with the daylight segment from real sunrise and sunset and a hand at the current time; cards tilt in 3D with a highlight on hover; resting the mouse on the taskbar monitor shows detailed information (network, today's traffic, CPU, memory, uptime, top apps).

## 1.2.3
- 中文：昼夜弧线改为按城市经纬度与日期计算真实日出日落（含极昼极夜），不再固定 6:00–18:00；卡片背面显示日出、日落、昼长；弧线新增光晕、光线、星光等 2D 动画（仅在可见时低帧率运行）。
- English: the day-night arc now uses real sunrise and sunset computed from each city's latitude and date (polar day / night included) instead of a fixed 06:00–18:00; card back shows sunrise, sunset and day length; the arc gains glow, rays and twinkling-star 2D animation (low frame rate, only while visible).

## 1.2.2
- 中文：修复三指下滑 / Win+D「显示桌面」后面板被收起的问题，小组件、监控条与面板现在都会保持显示。
- English: fixed the panel being swept away by the "Show desktop" gesture (three-finger swipe down / Win+D); the widget, taskbar monitor and panel now stay visible.

## 1.2.1
- 中文：重新打包发布；清理旧版本与本地备份；完善中英文 README 与仓库检索标签。
- English: rebuilt and republished; old versions and local backups removed; refined bilingual README and repository topics.

## 1.2.0
- 中文：发布包改为自带运行时与全部依赖的单个 `ZoneQuanta.exe`，下载即用；任务栏监控条默认在最左侧，可选位置，高度自适应无留白；新增英文 README。
- English: a single self-contained `ZoneQuanta.exe` (runtime and all dependencies built in); the taskbar monitor defaults to the far left with a selectable position and a height that adapts to the taskbar with no padding; English README added.

## 1.1.0
- 中文：任务栏监控条、应用流量统计、36 个热门城市、夏令时详情翻转卡片、安装位置选择、2D 动画。
- English: taskbar monitor, per-app traffic statistics, 36 popular cities, flip cards with DST details, install-location chooser, 2D animations.

## 1.0.x
- 中文：本地与洛杉矶时间、穿透 / 置顶 / 锁定 / 全屏隐藏、托盘与快捷键、签名校验的自动更新、时区编辑。
- English: local and Los Angeles time, click-through / always-on-top / lock / full-screen hide, tray and hotkey, signed auto-update, time zone editor.
