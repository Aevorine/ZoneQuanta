# 更新日志 · Changelog

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
