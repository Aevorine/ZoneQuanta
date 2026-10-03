# ZoneQuanta

**中文** · [English](README.en.md)

**Windows 桌面世界时钟 + 任务栏网速 / CPU / 内存监控 + 应用流量统计** · Windows desktop world clock (Los Angeles / local / any city, automatic DST), taskbar network-speed, CPU and RAM monitor, and per-app traffic statistics. Lightweight WPF / .NET 8, single-file exe, signed auto-update.

> 关键词 / Keywords: world clock, time zone, DST, Los Angeles time, desktop widget, taskbar monitor, network speed meter, upload download speed, CPU usage, memory usage, per-app network traffic, bandwidth monitor, Windows 10, Windows 11, WPF, .NET 8, 桌面时钟, 世界时钟, 夏令时, 洛杉矶时间, 任务栏网速, 流量统计

## 特性

| 能力 | 说明 |
|---|---|
| 本地 + 洛杉矶时间 | 夏令时 / 冬令时自动切换，使用系统时区数据库，逐年规则准确；卡片点击翻转，查看当前状态、下次切换时刻、当年夏令时区间与当日日出 / 日落 / 昼长 |
| 24 小时表盘 | 正午在上、午夜在下的 24 小时表盘：按每个城市的经纬度与日期实时计算日出日落（含极昼极夜）着色日照段，指针指向当前时刻，太阳 / 月亮标记带光晕、光线与星光动画；卡片悬停有 3D 倾斜与高光 |
| 任务栏悬停详情 | 鼠标停在任务栏监控条上，弹出详情：上传 / 下载 / 合计速度与实时曲线、今日流量、网卡与链路速率、CPU 使用率与核心 / 进程 / 线程、内存已用 / 可用 / 已提交、开机时长、今日流量排行 |
| 热门国家与城市 | 内置 36 个热门城市（美国、加拿大、中国、日本、欧洲、澳洲等），另可从系统全部时区中搜索，最多同时显示 4 个，可改名、排序 |
| 任务栏监控条 | 默认显示在任务栏最左侧，可选「开始按钮左侧」「通知区左侧」，另可微调；高度与任务栏自适应、四周无留白：上传速度、下载速度、总网速、内存占用率（%）、CPU 占用率（%），每一项可单独开关 |
| 网速单位 | 字节 `B` / 比特 `b`；单位自动（B → KB → MB → GB）或固定 K / M / G |
| 应用流量统计 | 按应用统计上传 / 下载流量；时间范围：今天、24 小时、本周、一月、一年、全部时间；带实时网速曲线与动画柱状图 |
| 桌面组件 | 鼠标穿透、总是置顶、锁定位置、允许超出屏幕边界、全屏程序运行时自动隐藏 |
| 开机自启 | 写入当前用户启动项，无需管理员权限 |
| 外观 | 四套低饱和护眼主题（石墨、暮紫、松针、纸本）；数字滚动、卡片翻转、悬停、页面切换等 2D 动画，均可按需关闭 |
| 托盘与快捷键 | 托盘左键显示 / 隐藏面板，右键含常用开关；全局快捷键默认 `Ctrl + Alt + Z` |
| 在线更新 | 多线路并发探测 + 分段并行下载，ECDSA 签名与 SHA-256 双重校验 |
| 低占用 | 秒对齐计时、只重绘变化内容；预热后空闲 CPU 约 0.3%（单核），内存约 75 MB |

## 安装

在 [Releases](https://github.com/Aevorine/ZoneQuanta/releases/latest) 下载 `ZoneQuanta.exe`（约 165 MB），双击即可使用：**已内置 .NET 运行时和全部依赖，不需要再安装任何东西**。

首次运行会询问安装位置：**可自行选择任意目录，程序始终安装在其中的 `ZoneQuanta` 文件夹内**（例如选择 `D:\Apps`，实际安装到 `D:\Apps\ZoneQuanta`），并可选创建桌面快捷方式与开机自启。程序未做代码签名，SmartScreen 可能提示未知发布者，选择「仍要运行」。

## 应用流量统计说明

Windows 只允许管理员读取按进程划分的网络事件，因此该功能是**可选的**：在「流量」页打开开关后，会弹出一次系统授权，创建名为 `ZoneQuantaTraffic` 的计划任务，用管理员权限运行统计助手（基于 ETW `Microsoft-Windows-Kernel-Network`，只记录进程名与字节数，不读取内容）。关闭开关时删除该任务。不开启时，仍可看到全部流量的总量与实时网速。

使用本地代理（如 TUN / 系统代理）时，流量会归属到代理进程。

## 使用

- 托盘图标 **左键** 打开 / 收起面板，收起后任务栏不显示
- 面板分八页：控制台、显示、时区、监控、流量、窗口、系统、更新
- 设置、日志与统计数据在 `%APPDATA%\ZoneQuanta`

## 架构

```
src/ZoneQuanta
├── Core        纯逻辑，不依赖界面
│   ├── Settings   设置模型与持久化
│   ├── Time       时区计算、夏令时、日出日落（天文算法）、城市目录、秒对齐计时器
│   ├── Monitor    网速 / CPU / 内存采样、单位换算、流量存储
│   ├── Traffic    管理员统计助手（ETW）与计划任务启动器
│   └── Update     清单验证、并行下载、更新协调
├── Platform    Win32 封装：窗口样式、全屏检测、全局热键、开机自启
├── Shell       托盘
├── UI
│   ├── Theme      调色板与统一样式
│   ├── Controls   滚动数字、昼夜弧线、速度曲线、时区卡片
│   ├── Widget     桌面组件窗口
│   ├── Band       任务栏监控条
│   ├── Setup      安装窗口
│   └── Panel      设置面板（页面注册表，新增页面只需加一行）
└── AppController  组合根
```

## 构建

需要 .NET 8 SDK：`dotnet build src/ZoneQuanta -c Release`。发布流程见 `scripts/release.ps1`（构建两个版本、签名、创建 Release、仅保留最近两个版本）。签名私钥不在仓库中。

## 许可

MIT
