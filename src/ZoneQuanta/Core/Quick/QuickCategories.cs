using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Quick;

// What is known about a launch target beyond its name: the shortcut's target, its comment and the
// program's own description. Filled in by the platform layer; Target is the address for an Internet shortcut.
// The three readers are slower evidence, asked only when the quick evidence settles nothing.
public sealed record TargetHint(string Target, string Arguments, string Description, string Product, string Company = "", string Comment = "")
{
    // What the product says about itself: its Apps & features comments, an Electron app's package description.
    public Func<string>? About { get; init; }
    // File types and link protocols the program is registered to open (".md", "mailto").
    public Func<IReadOnlyCollection<string>>? Handles { get; init; }
    // The opening of a read-me shipped next to the program.
    public Func<string>? Readme { get; init; }

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "chrome_proxy.exe", "msedge.exe", "msedge_proxy.exe", "firefox.exe", "brave.exe", "vivaldi.exe", "opera.exe",
        "thorium.exe", "iexplore.exe", "360se.exe", "360chrome.exe", "quark.exe", "waterfox.exe", "librewolf.exe", "chromium.exe",
    };

    public bool IsWebApp
    {
        get
        {
            string exe = System.IO.Path.GetFileName(Target).ToLowerInvariant();
            if (exe is "chrome_proxy.exe" or "msedge_proxy.exe" || Url is not null) return true;
            return exe is "chrome.exe" or "msedge.exe" or "brave.exe" or "vivaldi.exe"
                && (Arguments.Contains("--app-id", StringComparison.OrdinalIgnoreCase) || Arguments.Contains("--app=", StringComparison.OrdinalIgnoreCase));
        }
    }

    // The website a shortcut opens: an Internet shortcut's address, or the address handed to a browser.
    public Uri? Url
    {
        get
        {
            string raw = Target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Target
                : Browsers.Contains(System.IO.Path.GetFileName(Target)) && Regex.Match(Arguments, @"https?://[^\s""']+", RegexOptions.IgnoreCase) is { Success: true } m ? m.Value
                : "";
            return raw.Length > 0 && Uri.TryCreate(raw, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u : null;
        }
    }
}

// Automatic grouping of quick items. Evidence, nearest first: the item's own name, the website a web
// shortcut opens (host, path and #section), the program behind a shortcut (its file name, description,
// product and the shortcut's comment), what the product says about itself, the file types it opens, the
// folders it is installed in, the read-me shipped with it, then the kind of file. Short texts take the first
// rule that matches (rules run from specific to broad); long texts are scored, longest keyword first, earlier
// and rarer words weighing more, and count only with a clear winner. The result is never empty. Items also
// get a narrower kind (Sub) that names a group whose members all share it.
public static class QuickCategories
{
    public const string Ai = "AI", Browser = "浏览器", Social = "社交通讯", Mail = "邮件", Office = "办公学习",
        Dev = "开发", Engineering = "嵌入式与工程", Design = "设计", Media = "影音", Games = "游戏", Shopping = "购物",
        News = "资讯", Network = "网络与远程", Cloud = "网盘下载", SystemTools = "系统工具", Web = "网站",
        Folders = "文件夹", Documents = "文档", MediaFiles = "媒体文件", Files = "文件", OtherApps = "其他应用",
        Jobs = "招聘", Edu = "学习";

    // Display order of groups created together.
    public static readonly IReadOnlyList<string> Order = new[]
    {
        Ai, Browser, Social, Mail, Office, Edu, Jobs, Dev, Engineering, Design, Media, Games, Shopping, News, Network, Cloud,
        SystemTools, Web, Folders, Documents, MediaFiles, Files, OtherApps,
    };

    // Catch-all kinds: what is known when nothing specific is.
    public static bool IsCatchAll(string kind) => kind is Web or OtherApps or Files;

    // A second name for a kind whose own name another kind's group already carries.
    public static string Alternate(string kind) => kind switch { Edu => "学习考试", Jobs => "求职招聘", _ => kind };

    // Checked in this order, so the more specific category wins (QQ邮箱 is mail before QQ is social,
    // 夸克网盘 is cloud storage before 夸克 is a browser, Steam++ is a network tool before Steam is a game).
    // ASCII keywords match whole words (digits count as a break, so "uvision" matches "uVision5");
    // a leading '=' means the whole name must equal it.
    private static readonly (string Group, string[] Keys)[] Rules =
    {
        (Mail, new[] { "邮箱", "邮件", "mail", "email", "e mail", "gmail", "outlook", "foxmail", "thunderbird", "spark mail", "~tempmail" }),
        // Recruitment and schooling before the tools their sites mention (a campus VPN portal is a school site).
        (Jobs, new[] { "~招聘", "~求职", "~就业", "~实习", "~校招", "~job", "~jobs", "~career", "~careers", "~recruit", "~recruiting", "~recruitment",
            "~hiring", "~hotjob", "~wecruit", "~zhaopin", "~智联", "~前程无忧", "~51job", "~boss直聘", "~zhipin", "~猎聘", "~liepin", "~mokahr",
            "~zhiye", "~牛客", "~nowcoder", "~airjobs", "~bysjy" }),
        (Edu, new[] { "~大学", "~university", "~college", "~学院", "~edu", "~教育", "~考试", "~exam", "~neea", "~chsi", "~学信", "~cet", "~四六级",
            "~ncre", "~研究生", "~graduate", "~考研", "~研招", "~教务", "~jwgl", "~教学", "~学工", "~公考", "~公务员", "~考公", "~真题", "~刷题", "~题库",
            "~课程", "~course", "~courses", "~skilljar", "~mooc", "~慕课", "~大纲", "~专业目录", "~学习通", "~超星", "~雨课堂", "~课堂", "~网课", "~素养",
            "~study", "~learn", "~learning", "~tutorial", "~教程", "~学堂", "~xuetangx", "~icourse", "~coursera", "~edx", "~udemy", "~khan academy" }),
        // Coding agents and AI editors are development tools before they are AI chats.
        (Dev, new[] { "codex", "cursor", "windsurf", "trae", "claude code", "github copilot", "copilot cli", "cline", "aider", "harness",
            "opencode", "kiro", "antigravity", "zed", "cc switch", "ccswitch", "mcp server", "coding agent", "ai coding", "vibe coding",
            "csdn", "juejin", "掘金", "cnblogs", "博客园" }),
        // Reading apps named after a messenger (微信读书) are reading.
        (Office, new[] { "微信读书", "weread" }),
        (Ai, new[] { "claude", "chatgpt", "openai", "deepseek", "gemini", "grok", "kimi", "豆包", "doubao", "千问", "qwen", "通义", "tongyi",
            "qianwen", "文心", "ernie", "copilot", "perplexity", "poe", "智谱", "chatglm", "元宝", "秘塔", "metaso", "minimax", "海螺",
            "cherry studio", "lm studio", "ollama", "midjourney", "stable diffusion", "comfyui", "ai助手", "huggingface", "hugging face",
            "openrouter", "dify", "coze", "扣子", "智能体", "~ai", "~可灵", "~kling", "~notebooklm", "~motionsites", "~aigc", "~人工智能", "~大模型",
            "~llm", "~openclaw", "~anthropic" }),
        (Cloud, new[] { "网盘", "云盘", "onedrive", "dropbox", "google drive", "icloud", "坚果云", "nextcloud", "mega", "idm",
            "internet download manager", "迅雷", "qbittorrent", "bitcomet", "motrix", "aria2", "m3u8", "m3u8dl", "下载器", "downloader",
            "free download manager", "abdownloadmanager", "百度网盘", "阿里云盘", "115", "yt dlp", "youtube dl", "streamlink", "you get",
            "bt下载", "磁力" }),
        (Network, new[] { "v2ray", "v2rayn", "clash", "clash verge", "shadowsocks", "shadowsocksr", "xray", "sing box", "nekoray", "hiddify",
            "vpn", "speedtest", "speed test", "测速", "加速器", "steam++", "watt toolkit", "localsend", "wireshark", "fiddler", "charles",
            "proxifier", "远程", "remote", "todesk", "anydesk", "teamviewer", "向日葵", "sunlogin", "rustdesk", "mstsc", "parsec", "网易uu远程",
            "uu远程", "gameviewer", "moonlight", "sunshine",
            "finalshell", "xshell", "putty", "mobaxterm", "termius", "winscp", "filezilla", "tailscale", "zerotier", "网络", "~network",
            "~dns leak", "~leak test", "~webrtc", "~browserleaks", "~ipleak", "~ping0", "~ip检测", "~ip查询", "~加速", "~梯子", "~代理",
            "~proxy", "~hotspot", "~x ui", "~3x ui", "~s ui", "~xui", "~路由器", "~router" }),
        (Browser, new[] { "chrome", "edge", "firefox", "opera", "brave", "vivaldi", "夸克", "quark", "浏览器", "browser", "safari",
            "chromium", "360se", "internet explorer", "maxthon", "傲游", "tor browser", "waterfox", "librewolf", "thorium",
            "~=google", "~google search", "~=bing", "~必应", "~=百度", "~百度一下", "~搜索引擎", "~search engine", "~duckduckgo" }),
        (Shopping, new[] { "闲鱼", "淘宝", "taobao", "天猫", "tmall", "京东", "拼多多", "pinduoduo", "amazon", "亚马逊", "ebay", "美团",
            "饿了么", "得物", "唯品会", "苏宁", "当当", "aliexpress", "支付宝", "alipay", "paypal", "temu", "shein", "1688", "转转",
            "purchase", "购票", "抢票", "火车票", "机票", "12306", "携程", "ctrip", "飞猪", "去哪儿", "qunar", "train ticket",
            "~shop", "~shopping", "~商城", "~购物", "~goofish", "~pay", "~payment", "~wallet", "~钱包", "~发票", "~coinepay", "~stripe" }),
        (Social, new[] { "微信", "wechat", "weixin", "qq", "tim", "telegram", "discord", "钉钉", "dingtalk", "飞书", "feishu", "lark",
            "slack", "teams", "zoom", "whatsapp", "line", "skype", "signal", "企业微信", "wxwork", "腾讯会议", "wemeet", "messenger",
            "kook", "yy语音" }),
        // Communities and feeds read for news (X, Reddit, 微博) sit with news sites, as people sort them.
        (News, new[] { "知乎", "zhihu", "wikipedia", "维基", "百科", "cnn", "bbc", "news", "新闻", "头条", "nytimes", "reuters",
            "bloomberg", "the verge", "hacker news", "36氪", "虎嗅", "少数派", "stack overflow", "stackoverflow", "medium", "quora",
            "微博", "weibo", "=x", "twitter", "facebook", "instagram", "reddit", "小红书", "xiaohongshu", "豆瓣", "douban", "贴吧",
            "linkedin", "threads", "mastodon", "v2ex", "linux do", "product hunt", "producthunt", "ithome", "it之家", "cnbeta", "solidot",
            "blog", "博客", "blogger", "wordpress", "substack", "rss", "feedly", "inoreader", "~npr", "~财联社", "~新华", "~xinhua", "~人民网",
            "~半月谈", "~chinanews", "~论坛", "~forum", "~bbs", "~社区" }),
        (Media, new[] { "抖音", "douyin", "tiktok", "哔哩哔哩", "bilibili", "youtube", "爱奇艺", "iqiyi", "优酷", "youku", "腾讯视频",
            "芒果tv", "netflix", "twitch", "spotify", "网易云", "cloudmusic", "qq音乐", "qqmusic", "酷狗", "kugou", "酷我", "potplayer",
            "vlc", "mpv", "播放器", "player", "media player", "obs", "obs studio", "录音", "recorder", "照片", "photos", "剪映", "capcut",
            "premiere", "davinci", "audacity", "foobar2000", "aimp", "musicbee", "kmplayer", "直播", "斗鱼", "虎牙", "快手", "西瓜视频",
            "camera", "相机", "音乐", "music", "视频", "video", "movies", "电影", "itunes", "apple music", "plex", "jellyfin", "emby",
            "video editing", "视频编辑", "剪辑", "maruko", "小丸", "handbrake", "ffmpeg", "格式工厂", "format factory", "shotcut", "kdenlive",
            "clipchamp", "~media encoder", "~videos", "~movie" }),
        (Games, new[] { "steam", "epic games", "xbox", "battle net", "battle.net", "wegame", "原神", "genshin", "minecraft", "riot",
            "league of legends", "英雄联盟", "ea app", "ubisoft", "uplay", "gog galaxy", "米哈游", "mihoyo", "hoyoplay", "网易游戏",
            "mumu", "bluestacks", "雷电模拟器", "模拟器", "游戏", "game", "games" }),
        (Engineering, new[] { "keil", "uvision", "stm32", "cubemx", "cubeide", "嘉立创", "lceda", "eda", "altium", "kicad", "arduino",
            "vofa+", "vofa", "串口", "serial", "matlab", "simulink", "multisim", "proteus", "labview", "quartus", "vivado", "modelsim",
            "ltspice", "solidworks", "autocad", "catia", "ansys", "comsol", "jlink", "j link", "st link", "platformio", "mplab", "iar",
            "ccs", "code composer", "saleae", "pulseview", "示波器", "立创", "cadence", "orcad", "pads", "fusion 360", "creo", "ug nx",
            "siemens nx", "abaqus", "mathcad", "mathematica", "maple", "origin", "spss" }),
        (Dev, new[] { "visual studio", "vs code", "vscode", "code", "jetbrains", "intellij", "idea", "pycharm", "clion", "webstorm",
            "rider", "goland", "datagrip", "phpstorm", "android studio", "zed", "sublime", "notepad++", "vim", "gvim", "emacs", "git",
            "github", "gitlab", "gitee", "sourcetree", "fork", "terminal", "终端", "powershell", "pwsh", "command prompt", "命令提示符",
            "cmd", "wezterm", "alacritty", "warp", "windows terminal", "wsl", "ubuntu", "debian", "kali", "arch linux", "msys", "msys2",
            "mingw", "cygwin", "python", "anaconda", "jupyter", "node", "nodejs", "docker", "postman", "apifox", "insomnia", "navicat",
            "dbeaver", "mysql", "sql", "redis", "mongodb", "hbuilder", "unity", "unreal", "godot", "vmware", "virtualbox", "hyper v",
            "虚拟机", "developer", "开发", "devtools", "neovim", "hoppscotch", "heidisql", "lazygit", "repositories", "repos", "leetcode",
            "~cloudflare", "~supabase", "~firebase", "~vercel", "~netlify", "~render", "~heroku",
            "~aws", "~azure", "~google cloud", "~gcp", "~阿里云", "~腾讯云", "~华为云", "~autodl", "~算力", "~vps", "~搬瓦工", "~bandwagon", "~clawcloud",
            "~claw cloud", "~clawhub", "~jb51", "~脚本之家", "~beautifier", "~beautify", "~api", "~域名", "~hosting" }),
        (Design, new[] { "画图", "paint", "mspaint", "photoshop", "illustrator", "figma", "gimp", "krita", "inkscape", "blender",
            "sketchup", "canva", "coreldraw", "lightroom", "after effects", "3ds max", "maya", "c4d", "cinema 4d", "美图", "稿定",
            "affinity", "pixso", "即时设计", "xd", "procreate", "clip studio", "darktable", "rawtherapee", "draw io", "drawio", "diagrams",
            "visio", "流程图", "excalidraw", "processon", "mermaid", "~pexels", "~unsplash", "~pixabay", "~素材", "~图库", "~design", "~堆友" }),
        (Office, new[] { "word", "excel", "powerpoint", "onenote", "office", "microsoft 365", "wps", "to do", "todo", "notion",
            "obsidian", "typora", "记事本", "notepad", "acrobat", "pdf", "xmind", "腾讯文档", "石墨", "evernote", "印象笔记", "有道",
            "翻译", "translate", "translator", "词典", "dictionary", "学习通", "mooc", "zotero", "endnote", "mendeley", "anki", "知网",
            "cnki", "日历", "calendar", "便笺", "sticky notes", "语雀", "logseq", "joplin", "marktext", "latex", "texstudio", "overleaf",
            "金山文档", "飞书文档", "幕布", "flomo", "滴答清单", "ticktick", "todoist", "trello", "思维导图", "mindmap", "teams classic",
            "学习", "课堂", "雨课堂", "超星", "钉钉文档", "kindle", "阅读", "reader", "calibre", "ebook", "知识库", "knowledge", "笔记",
            "arxiv", "scholar", "markdown", "markdown editor", "md editor", "~读书", "~weread", "~library", "~zlib", "~z library", "~古诗",
            "~诗文", "~小说", "~novel", "~book", "~books", "~kdocs" }),
        (SystemTools, new[] { "设置", "settings", "immersivecontrolpanel", "控制面板", "control panel", "file explorer", "资源管理器", "explorer",
            "此电脑", "this pc", "计算器", "calculator", "resource monitor", "资源监视器", "task scheduler", "任务计划", "task manager",
            "任务管理器", "时钟", "alarms", "clock", "截图", "screensketch", "snipping", "pixpin", "snipaste", "sharex", "7 zip", "7zip",
            "winrar", "bandizip", "压缩", "360zip", "everything", "quicklook", "listary", "wox", "powertoys", "zonequanta", "uninstall",
            "uninstaller", "卸载", "geek", "repair", "电脑管家", "管家", "扩容", "安全卫士", "卫士", "杀毒", "antivirus", "defender",
            "安全中心", "memory zone", "sandisk", "回收站", "recycle bin", "regedit", "注册表", "device manager", "设备管理器", "磁盘",
            "disk", "diskgenius", "cleaner", "ccleaner", "dism++", "microsoft store", "windowsstore", "应用商店", "store", "驱动",
            "driver", "备份", "backup", "rufus", "ventoy", "cpu z", "gpu z", "hwinfo", "aida64", "crystaldiskinfo", "crystaldiskmark",
            "ditto", "clipboard", "剪贴板", "event viewer", "事件查看器", "services", "^服务", "系统信息", "system information",
            "computer management", "计算机管理", "performance monitor", "性能监视器", "windows tools", "windows 工具", "rainmeter",
            "translucenttb", "startallback", "explorerpatcher", "nilesoft", "f.lux", "monitorian", "twinkle tray", "wallpaper",
            "壁纸", "图吧工具箱", "dism", "火绒", "huorong", "联想电脑管家", "legion", "armoury", "omen", "内容检索", "文件检索", "全文检索",
            "文件搜索", "file search", "desktop search", "~virustotal", "~ifixit", "~修理", "~生成器", "~generator", "~password", "~密码",
            "~1password", "~bitwarden", "~keepass", "~converter", "~转换", "~aconvert", "~在线工具", "~toolbox", "~工具箱", "~工具", "~tools" }),
    };

    // Narrower kinds inside a group, for naming a second group of the same kind ("系统工具" -> "电脑管家").
    private static readonly (string Group, string Sub, string[] Keys)[] SubRules =
    {
        (SystemTools, "电脑管家", new[] { "电脑管家", "管家", "安全卫士", "卫士", "杀毒", "antivirus", "defender", "火绒", "huorong", "扩容", "清理",
            "cleaner", "ccleaner", "卸载", "uninstall", "uninstaller", "geek", "dism++", "驱动", "driver", "优化", "optimizer", "安全中心", "repair", "修复" }),
        (SystemTools, "截图工具", new[] { "截图", "screensketch", "snipping", "pixpin", "snipaste", "sharex", "录屏" }),
        (SystemTools, "文件工具", new[] { "explorer", "资源管理器", "此电脑", "this pc", "everything", "7 zip", "7zip", "winrar", "bandizip", "压缩",
            "360zip", "quicklook", "listary", "回收站", "recycle bin", "memory zone", "内容检索", "文件检索", "file search", "文件" }),
        (SystemTools, "硬件检测", new[] { "cpu z", "gpu z", "hwinfo", "aida64", "crystaldiskinfo", "crystaldiskmark", "diskgenius", "磁盘", "disk" }),
        (SystemTools, "系统设置", new[] { "设置", "settings", "控制面板", "control panel", "task manager", "任务管理器", "resource monitor",
            "资源监视器", "task scheduler", "任务计划", "regedit", "注册表", "services", "^服务", "event viewer", "device manager", "计算机管理",
            "系统信息", "store", "应用商店", "计算器", "calculator", "时钟", "clock" }),
        (Dev, "终端", new[] { "terminal", "终端", "powershell", "pwsh", "command prompt", "命令提示符", "cmd", "wezterm", "alacritty", "warp",
            "wsl", "git bash", "msys", "msys2", "mingw", "cygwin", "ubuntu", "debian", "kali" }),
        (Dev, "AI 编程", new[] { "codex", "cursor", "windsurf", "trae", "claude code", "github copilot", "copilot cli", "cline", "aider", "harness",
            "opencode", "kiro", "antigravity", "cc switch", "ccswitch", "mcp server", "coding agent", "ai coding", "vibe coding" }),
        (Dev, "编辑器", new[] { "visual studio", "vs code", "vscode", "code", "jetbrains", "intellij", "idea", "pycharm", "clion", "webstorm", "rider",
            "goland", "zed", "sublime", "notepad++", "vim", "emacs", "android studio", "hbuilder", "neovim" }),
        (Dev, "虚拟机", new[] { "vmware", "virtualbox", "hyper v", "虚拟机", "docker" }),
        (Dev, "代码托管", new[] { "github", "gitlab", "gitee", "git", "sourcetree", "fork", "repositories", "repos" }),
        (Dev, "数据库与接口", new[] { "navicat", "dbeaver", "mysql", "sql", "redis", "mongodb", "postman", "apifox", "insomnia", "hoppscotch", "heidisql" }),
        (Media, "视频", new[] { "video", "视频", "剪辑", "剪映", "capcut", "premiere", "davinci", "potplayer", "vlc", "mpv", "播放器", "youtube",
            "bilibili", "哔哩哔哩", "抖音", "douyin", "tiktok", "爱奇艺", "优酷", "腾讯视频", "netflix", "obs", "maruko", "handbrake", "电影" }),
        (Media, "音乐", new[] { "music", "音乐", "网易云", "cloudmusic", "qq音乐", "qqmusic", "酷狗", "酷我", "spotify", "foobar2000", "aimp",
            "musicbee", "录音", "recorder", "audacity" }),
        (Media, "直播", new[] { "直播", "twitch", "斗鱼", "虎牙" }),
        (Media, "照片", new[] { "照片", "photos", "camera", "相机" }),
        (Office, "办公文档", new[] { "word", "excel", "powerpoint", "wps", "acrobat", "pdf", "腾讯文档", "石墨", "金山文档", "飞书文档", "latex", "texstudio", "overleaf" }),
        (Office, "笔记", new[] { "onenote", "notion", "obsidian", "typora", "记事本", "notepad", "evernote", "印象笔记", "logseq", "joplin", "marktext",
            "flomo", "幕布", "xmind", "思维导图", "mindmap", "语雀", "便笺", "sticky notes", "知识库", "笔记", "markdown", "md editor" }),
        (Office, "学习", new[] { "学习通", "mooc", "学习", "课堂", "雨课堂", "超星", "知网", "cnki", "zotero", "endnote", "mendeley", "anki",
            "有道", "翻译", "translate", "词典", "dictionary", "arxiv", "scholar" }),
        (Office, "阅读", new[] { "kindle", "阅读", "reader", "calibre", "ebook", "~读书", "~weread", "~library", "~zlib", "~z library", "~古诗", "~诗文",
            "~小说", "~novel", "~book", "~books" }),
        (Edu, "考试", new[] { "~考试", "~exam", "~neea", "~cet", "~四六级", "~ncre", "~公考", "~公务员", "~考公", "~真题", "~刷题", "~题库", "~考研", "~研招", "~大纲",
            "~专业目录", "~研究生" }),
        (Edu, "院校", new[] { "~大学", "~university", "~college", "~学院", "~edu", "~教务", "~jwgl", "~教学", "~学工", "~chsi", "~学信" }),
        (Edu, "课程", new[] { "~课程", "~course", "~courses", "~skilljar", "~mooc", "~慕课", "~网课", "~课堂", "~学堂", "~coursera", "~edx", "~udemy", "~tutorial", "~教程" }),
        (Shopping, "支付", new[] { "paypal", "支付宝", "alipay", "~pay", "~payment", "~wallet", "~钱包", "~发票", "~coinepay", "~stripe" }),
        (Dev, "云服务", new[] { "~cloudflare", "~supabase", "~firebase", "~vercel", "~netlify", "~render", "~heroku", "~aws", "~azure", "~google cloud", "~gcp",
            "~阿里云", "~腾讯云", "~华为云", "~autodl", "~算力", "~vps", "~搬瓦工", "~bandwagon", "~clawcloud", "~claw cloud", "~域名", "~hosting" }),
        (Network, "网络检测", new[] { "~dns leak", "~leak test", "~webrtc", "~browserleaks", "~ipleak", "~ping0", "~ip检测", "~ip查询", "~speedtest", "~speed test", "~测速" }),
        (SystemTools, "在线工具", new[] { "~生成器", "~generator", "~password", "~密码", "~1password", "~bitwarden", "~keepass", "~converter", "~转换", "~aconvert", "~在线工具" }),
        (Browser, "搜索", new[] { "~=google", "~google search", "~=bing", "~必应", "~=百度", "~百度一下", "~搜索引擎", "~search engine", "~duckduckgo" }),
        (Office, "日程待办", new[] { "to do", "todo", "日历", "calendar", "滴答清单", "ticktick", "todoist", "trello" }),
        (Network, "远程", new[] { "远程", "remote", "todesk", "anydesk", "teamviewer", "向日葵", "sunlogin", "rustdesk", "mstsc", "parsec", "finalshell",
            "xshell", "putty", "mobaxterm", "termius", "winscp", "filezilla", "moonlight" }),
        (Network, "代理加速", new[] { "v2ray", "v2rayn", "clash", "shadowsocks", "xray", "sing box", "nekoray", "hiddify", "vpn", "加速器", "steam++",
            "watt toolkit", "proxifier", "tailscale", "zerotier" }),
        (Network, "测速传输", new[] { "speedtest", "speed test", "测速", "localsend", "wireshark", "fiddler", "charles" }),
        (Social, "会议", new[] { "zoom", "teams", "腾讯会议", "wemeet", "meet" }),
        (News, "社区", new[] { "=x", "twitter", "reddit", "微博", "weibo", "小红书", "xiaohongshu", "豆瓣", "douban", "贴吧", "facebook", "instagram",
            "threads", "mastodon", "linkedin", "知乎", "zhihu", "quora", "v2ex", "linux do" }),
        (News, "百科", new[] { "wikipedia", "维基", "百科" }),
        (News, "新闻", new[] { "cnn", "bbc", "news", "新闻", "头条", "nytimes", "reuters", "bloomberg", "the verge", "36氪", "虎嗅", "ithome", "cnbeta" }),
        (Ai, "AI 绘图", new[] { "midjourney", "stable diffusion", "comfyui" }),
        (Ai, "本地模型", new[] { "ollama", "lm studio" }),
        (Cloud, "网盘", new[] { "网盘", "云盘", "onedrive", "dropbox", "google drive", "icloud", "坚果云", "nextcloud", "mega", "百度网盘", "阿里云盘", "115" }),
        (Cloud, "下载", new[] { "idm", "internet download manager", "迅雷", "qbittorrent", "bitcomet", "motrix", "aria2", "m3u8", "m3u8dl", "下载器",
            "downloader", "yt dlp", "youtube dl", "streamlink", "you get", "free download manager", "abdownloadmanager", "bt下载", "磁力" }),
        (News, "博客", new[] { "blog", "博客", "blogger", "wordpress", "substack", "rss", "feedly", "inoreader" }),
        (Engineering, "嵌入式", new[] { "keil", "uvision", "stm32", "cubemx", "cubeide", "arduino", "vofa+", "vofa", "串口", "serial", "jlink", "j link",
            "st link", "platformio", "mplab", "iar", "ccs", "code composer", "saleae", "pulseview", "示波器", "quartus", "vivado", "modelsim" }),
        (Engineering, "电路设计", new[] { "嘉立创", "lceda", "eda", "altium", "kicad", "multisim", "proteus", "ltspice", "cadence", "orcad", "pads", "立创" }),
        (Engineering, "仿真计算", new[] { "matlab", "simulink", "labview", "ansys", "comsol", "abaqus", "mathcad", "mathematica", "maple", "origin", "spss" }),
        (Engineering, "机械设计", new[] { "solidworks", "autocad", "catia", "fusion 360", "creo", "ug nx", "siemens nx" }),
        (Shopping, "出行票务", new[] { "购票", "抢票", "火车票", "机票", "12306", "携程", "ctrip", "飞猪", "去哪儿", "qunar", "train ticket", "分流抢票" }),
    };

    // Every narrower kind's name (a group may be named after one).
    public static IReadOnlyCollection<string> SubNames { get; } = SubRules.Select(r => r.Sub).Distinct().ToArray();

    // File types and link protocols a program opens, by what they say it is for. Weak ones (pictures, PDF,
    // web pages) are opened by many kinds of program and count only when nothing stronger is registered.
    private static readonly (string Group, string Sub, bool Weak, string[] Types)[] TypeRules =
    {
        (Mail, "", false, new[] { "mailto", ".eml", ".msg" }),
        (Browser, "", false, new[] { "http", "https" }),
        (Office, "笔记", false, new[] { ".md", ".markdown", ".mdown", ".one", ".enex", ".xmind", ".mm", ".opml" }),
        (Office, "办公文档", false, new[] { ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".wps", ".et", ".dps", ".odt", ".ods", ".odp", ".rtf", ".ofd" }),
        (Office, "学习", false, new[] { ".epub", ".mobi", ".azw3", ".fb2", ".djvu", ".caj", ".kdh", ".nh", ".ris", ".enw", ".bib" }),
        (Office, "办公文档", true, new[] { ".pdf", ".txt" }),
        (Media, "视频", false, new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m2ts", ".rmvb", ".m3u8", ".ts" }),
        (Media, "音乐", false, new[] { ".mp3", ".flac", ".ape", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".cue" }),
        (Media, "照片", true, new[] { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".bmp", ".gif", ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw" }),
        (Design, "", false, new[] { ".psd", ".ai", ".xcf", ".kra", ".blend", ".fbx", ".c4d", ".max", ".skp", ".drawio", ".vsdx", ".fig", ".sketch",
            ".cdr", ".afdesign", ".afphoto", ".clip", ".svg" }),
        (SystemTools, "文件工具", false, new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".xz", ".bz2", ".zst", ".cab", ".iso" }),
        (Cloud, "下载", false, new[] { ".torrent", "magnet", "thunder", "ed2k", "flashget", "qqdl" }),
        (Dev, "编辑器", false, new[] { ".c", ".cpp", ".h", ".hpp", ".cs", ".py", ".js", ".ts", ".java", ".go", ".rs", ".sln", ".csproj", ".vcxproj",
            ".ipynb", ".php", ".kt", ".swift", ".lua", ".vue", ".jsx", ".tsx" }),
        (Dev, "终端", false, new[] { ".ps1", ".sh", ".bash", "ssh-terminal" }),
        (Engineering, "嵌入式", false, new[] { ".uvprojx", ".uvproj", ".uvmpw", ".ioc", ".ino", ".hex", ".axf", ".elf" }),
        (Engineering, "电路设计", false, new[] { ".eprj", ".epro", ".schdoc", ".pcbdoc", ".prjpcb", ".kicad_pcb", ".kicad_sch", ".brd", ".sch", ".ms14", ".dsn" }),
        (Engineering, "仿真计算", false, new[] { ".m", ".mlx", ".slx", ".mdl", ".mat", ".nb", ".mph", ".opju", ".sav" }),
        (Engineering, "机械设计", false, new[] { ".dwg", ".dxf", ".step", ".stp", ".igs", ".sldprt", ".sldasm", ".prt", ".catpart", ".f3d" }),
        (Network, "远程", false, new[] { "ssh", "rdp", "vnc", "telnet", "sftp", ".rdp", "todesk", "sunlogin", "anydesk" }),
        (Social, "", false, new[] { "tg", "weixin", "tencent", "dingtalk", "feishu", "lark", "slack", "discord", "zoommtg", "msteams", "wemeet" }),
        (Games, "", false, new[] { "steam", "com.epicgames.launcher", "battlenet", "uplay", "origin2" }),
    };

    // Every type worth asking the system about (the platform layer looks up which programs open them).
    public static IReadOnlyCollection<string> InformativeTypes { get; } = TypeRules.SelectMany(r => r.Types).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // Sites whose address names no keyword above.
    private static readonly (string Host, string Group)[] Sites =
    {
        ("jd.com", Shopping), ("12306.cn", Shopping), ("music.163.com", Media), ("drive.google.com", Cloud), ("docs.google.com", Office),
        ("sheets.google.com", Office), ("slides.google.com", Office), ("meet.google.com", Social), ("t.me", Social), ("tieba.baidu.com", News),
        ("pan.baidu.com", Cloud), ("aliyundrive.com", Cloud), ("alipan.com", Cloud), ("163.com", News), ("qq.com", Social),
    };

    // Folder names that say nothing about what a program does.
    private static readonly HashSet<string> PlainFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "program files", "program files (x86)", "apps", "app", "applications", "files", "bin", "x64", "x86", "amd64", "win64", "win32",
        "windows", "system32", "users", "appdata", "local", "roaming", "programs", "current", "latest", "resources", "tools", "software",
        "portable", "release", "common files",
    };

    private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".pdf", ".txt", ".md", ".csv", ".rtf", ".odt", ".ods", ".odp",
        ".wps", ".et", ".dps", ".epub", ".mobi", ".tex", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".html", ".htm",
    };

    private static readonly HashSet<string> MediaExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff", ".heic", ".psd", ".mp4", ".mkv", ".avi",
        ".mov", ".wmv", ".flv", ".webm", ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma",
    };

    private static readonly HashSet<string> AppExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".exe", ".appref-ms", ".bat", ".cmd", ".ps1", ".msc", ".cpl",
    };

    // Everyday words that also turn up in texts about something else: a fraction of a full keyword when scored.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "player", "game", "games", "video", "music", "store", "reader", "disk", "remote", "网络", "学习", "阅读", "视频", "音乐", "游戏",
        "settings", "设置", "backup", "备份", "services", "服务", "driver", "驱动", "clock", "时钟", "calendar", "日历", "translate", "翻译", "news",
        "新闻", "camera", "相机", "photos", "照片", "explorer", "repair", "terminal", "终端", "node", "sql", "office", "word", "edge", "line",
        "teams", "fork", "idea", "origin", "serial", "eda", "iar", "ccs", "pads", "tim", "signal", "medium", "threads", "git", "python",
        "developer", "开发", "截图", "压缩", "文件检索", "录音", "recorder", "直播", "电影", "movies", "pdf", "ebook", "knowledge", "知识库", "笔记",
        "downloader", "下载器", "vpn", "测速", "speed test", "browser", "浏览器", "mail", "email", "邮件", "邮箱", "rss", "blog", "博客", "clipboard",
        "剪贴板", "wallpaper", "壁纸", "cleaner",
    };

    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
    private static readonly Regex Bracketed = new(@"[\(（\[【][^\)）\]】]*[\)）\]】]", RegexOptions.Compiled);

    private static readonly (string Group, Key[] Keys)[] Compiled =
        Rules.Select(r => (r.Group, r.Keys.Select(Key.Of).ToArray())).ToArray();

    private static readonly (string Group, string Sub, Key[] Keys)[] CompiledSubs =
        SubRules.Select(r => (r.Group, r.Sub, r.Keys.Select(Key.Of).ToArray())).ToArray();

    public static string Classify(string path, string displayName, TargetHint? hint) => Explain(path, displayName, hint).Group;

    // The group, the narrower kind inside it ("" when none applies) and the evidence that decided (for the log).
    public static (string Group, string Sub, string Why) Explain(string path, string displayName, TargetHint? hint)
    {
        var (group, why, text, sub) = Decide(path, displayName, hint);
        return (group, sub ?? SubOf(group, text, IsWeb(hint)), why);
    }

    // A site: an Internet shortcut, a link handed to a browser or an installed web app. Keywords marked for
    // sites ("~") only count for these; ones marked for local items ("^") only for the rest.
    private static bool IsWeb(TargetHint? hint) => hint is not null && (hint.Url is not null || hint.IsWebApp);

    // Sub is set when the evidence itself names the narrower kind (a registered file type).
    private static (string Group, string Why, string Text, string? Sub) Decide(string path, string displayName, TargetHint? hint)
    {
        bool isWeb = IsWeb(hint);
        bool app = path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase);
        // "Claude (<account>@gmail.com)" is Claude: account labels and bracketed notes do not name the app,
        // so the bare name decides first and an e-mail address never counts.
        string name = Email.Replace(displayName, " ").Trim();
        string core = Bracketed.Replace(name, " ").Trim();
        if (core.Length > 0 && Match(Text.Of(core), core, isWeb) is { } byCore) return (byCore, "name", core, null);
        string own = name + " " + Email.Replace(System.IO.Path.GetFileNameWithoutExtension(path.TrimEnd('\\')), " ");
        if (app) own += " " + path[17..];
        if (Match(Text.Of(own), name, isWeb) is { } byName) return (byName, "name", own, null);

        // A web shortcut is the site it opens, never the browser that opens it.
        // The address says the most: host, the first path parts and the #section a single-page site opens on
        // (example.com/#blog is a blog).
        if (hint?.Url is { } url)
        {
            string host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;
            string site = host + " " + string.Join(' ', url.AbsolutePath.Trim('/').Split('/').Take(3)) + " " + Section(url);
            string label = Registrable(host).Split('.')[0];
            foreach (var (h, g) in Sites)
                if (host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
                    return (g, "site " + host, own + " " + site, null);
            if (Match(Text.Of(site), label, true) is { } bySite) return (bySite, "site " + host, own + " " + site, null);
            // Where the link was filed (an imported bookmark's folders, innermost first) says what it is for.
            foreach (string folder in (hint.Comment ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
                if (Match(Text.Of(folder), folder, true) is { } byFolder) return (byFolder, $"folder \"{folder}\", site {host}", own + " " + site + " " + folder, null);
            return (Web, "site " + host, own + " " + site, null);
        }
        bool web = hint?.IsWebApp == true;
        if (hint is not null && !web)
        {
            string program = System.IO.Path.GetFileNameWithoutExtension(hint.Target) + " " + hint.Description + " " + hint.Product + " " + hint.Comment;
            if (Match(Text.Of(program), null, false) is { } byProgram) return (byProgram, "program", own + " " + program, null);
            // What the product says about itself, then what the system opens with it.
            string about = ReadText(hint.About);
            if (about.Length > 0 && Score(about) is { } byAbout) return (byAbout, "about", own + " " + about, null);
            if (ByTypes(ReadTypes(hint.Handles)) is { } byTypes) return (byTypes.Group, "opens " + byTypes.Types, own, byTypes.Sub);
            // Where it was put says what it is for (a tool kept in a folder named Video is a media tool).
            string folders = FolderWords(hint.Target);
            if (folders.Length > 0 && Match(Text.Of(folders), null, false) is { } byFolder) return (byFolder, "folder", own + " " + program + " " + folders, null);
            string readme = ReadText(hint.Readme);
            if (readme.Length > 0 && Score(readme) is { } byReadme) return (byReadme, "readme", own + " " + readme, null);
        }

        if (web) return (Web, "web app", own, null);
        if (app) return (OtherApps, "", own, null);
        return (Kind(path, hint), "file", own, null);
    }

    private static string Section(Uri url)
    {
        string f = Uri.UnescapeDataString(url.Fragment.TrimStart('#', '/'));
        int q = f.IndexOf('?');
        return q >= 0 ? f[..q] : f;
    }

    // A reader that fails counts as having nothing to say.
    private static string ReadText(Func<string>? reader)
    {
        try { return reader?.Invoke() ?? ""; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return ""; }
    }

    private static IReadOnlyCollection<string> ReadTypes(Func<IReadOnlyCollection<string>>? reader)
    {
        try { return reader?.Invoke() ?? Array.Empty<string>(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return Array.Empty<string>(); }
    }

    // Votes of the registered types: the kind most of the strong ones point to (weak ones only when there are
    // no strong ones), with a clear lead over the next kind.
    private static (string Group, string Sub, string Types)? ByTypes(IReadOnlyCollection<string> types)
    {
        if (types.Count == 0) return null;
        foreach (bool weak in new[] { false, true })
        {
            var votes = new List<(string Group, string Sub, string Type)>();
            foreach (string t in types)
                foreach (var r in TypeRules)
                    if (r.Weak == weak && r.Types.Contains(t, StringComparer.OrdinalIgnoreCase)) votes.Add((r.Group, r.Sub, t));
            if (votes.Count == 0) continue;
            var ranked = votes.GroupBy(v => v.Group).OrderByDescending(g => g.Count()).ToList();
            if (ranked.Count > 1 && ranked[0].Count() < ranked[1].Count() * 2) return null;
            var top = ranked[0];
            string sub = top.GroupBy(v => v.Sub).OrderByDescending(g => g.Count()).First().Key;
            return (top.Key, sub, string.Join(' ', top.Select(v => v.Type).Distinct().Take(4)));
        }
        return null;
    }

    // Scores a longer text: each keyword claims its span (longest first, so "claude code" is not also "claude"),
    // worth 1 (an everyday word 0.35) and less the later it appears. A kind wins with at least 0.5 and 1.5 times
    // the runner-up; anything closer says nothing.
    private static string? Score(string raw)
    {
        var text = Text.Of(raw.Length > 1500 ? raw[..1500] : raw).Plain;
        var hits = new List<(int At, int Len, string Group, double W)>();
        foreach (var (group, keys) in Compiled)
            foreach (var k in keys)
                if (!k.Exact && !k.Web)
                    foreach (int at in Text.Find(text, k))
                        hits.Add((at, k.Value.Length, group, Common.Contains(k.Value) ? 0.35 : 1.0));
        if (hits.Count == 0) return null;
        var taken = new bool[text.Length];
        var score = new Dictionary<string, double>();
        foreach (var h in hits.OrderByDescending(h => h.Len).ThenBy(h => h.At))
        {
            bool free = true;
            for (int i = h.At; i < h.At + h.Len && free; i++) free = !taken[i];
            if (!free) continue;
            for (int i = h.At; i < h.At + h.Len; i++) taken[i] = true;
            score[h.Group] = score.GetValueOrDefault(h.Group) + h.W / (1 + h.At / 300.0);
        }
        var ranked = score.OrderByDescending(x => x.Value).ToList();
        double second = ranked.Count > 1 ? ranked[1].Value : 0;
        return ranked[0].Value >= 0.5 && ranked[0].Value >= second * 1.5 ? ranked[0].Key : null;
    }

    // example.com from www.example.com / mail.example.co.uk -> example.co.uk.
    public static string Registrable(string host)
    {
        var p = host.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length <= 2) return string.Join('.', p);
        bool shortSecond = p[^2].Length <= 3 && p[^1].Length == 2 && p[^2] is "com" or "co" or "net" or "org" or "edu" or "gov" or "ac";
        return string.Join('.', p[^(shortSecond ? 3 : 2)..]);
    }

    // Up to four folders above a program file, without the ones that say nothing (Program Files, bin, versions).
    private static string FolderWords(string target)
    {
        var words = new List<string>();
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(target);
            for (int i = 0; i < 4 && dir is not null && dir.Length > 3; i++, dir = System.IO.Path.GetDirectoryName(dir))
            {
                string n = System.IO.Path.GetFileName(dir);
                if (n.Length > 1 && !PlainFolders.Contains(n) && !Regex.IsMatch(n, @"^[\d._-]+$")) words.Add(n);
            }
        }
        catch (ArgumentException) { }
        return string.Join(' ', words);
    }

    private static string SubOf(string group, string text, bool web)
    {
        var t = Text.Of(text);
        foreach (var (g, sub, keys) in CompiledSubs)
            if (g == group && keys.Any(k => k.Counts(web) && (!k.Exact && t.Has(k) || k.Exact && t.Plain.Contains(" " + k.Value + " ", StringComparison.OrdinalIgnoreCase))))
                return sub;
        return "";
    }

    private static string Kind(string path, TargetHint? hint)
    {
        string target = hint?.Target is { Length: > 0 } t ? t : path;
        if (PathEngine.IsShell(target)) return Folders;
        string trimmed = target.TrimEnd('\\');
        if (trimmed.Length == 2 && trimmed[1] == ':') return Folders;
        try
        {
            if (Directory.Exists(target)) return Folders;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        string ext = System.IO.Path.GetExtension(target);
        if (string.Equals(System.IO.Path.GetExtension(path), ".url", StringComparison.OrdinalIgnoreCase)) return Web;
        if (DocExt.Contains(ext)) return Documents;
        if (MediaExt.Contains(ext)) return MediaFiles;
        if (AppExt.Contains(ext) || AppExt.Contains(System.IO.Path.GetExtension(path))) return OtherApps;
        return Files;
    }

    private static string? Match(Text text, string? wholeName, bool web)
    {
        foreach (var (group, keys) in Compiled)
            foreach (var k in keys)
                if (k.Counts(web) && (k.Exact ? wholeName is not null && string.Equals(wholeName, k.Value, StringComparison.OrdinalIgnoreCase) : text.Has(k)))
                    return group;
        return null;
    }

    // A leading '~' marks a word that only counts for sites (broad words that would misfile local files and
    // programs: "工具", "book", "api"), '^' one that only counts for local items; then '=' for a whole name.
    private readonly record struct Key(string Value, bool Exact, bool AsciiStart, bool AsciiEnd, bool Web = false, bool Local = false)
    {
        public bool Counts(bool web) => web ? !Local : !Web;

        public static Key Of(string raw)
        {
            bool web = raw.StartsWith('~'), local = raw.StartsWith('^');
            if (web || local) raw = raw[1..];
            if (raw.StartsWith('=')) return new Key(raw[1..], true, false, false, web, local);
            string v = Text.Normalize(raw);
            return new Key(v, false, IsAsciiAlnum(v[0]), IsAsciiAlnum(v[^1]), web, local);
        }
    }

    // Lower-cased name in two spellings: as written ("onedrive") and with camel-case / digit runs split
    // ("one drive"), separators turned into single spaces.
    private readonly record struct Text(string Plain, string Split)
    {
        public static Text Of(string s) => new(" " + Normalize(s) + " ", " " + Normalize(SplitCamel(s)) + " ");

        public bool Has(Key k) => Find(Plain, k).Any() || Find(Split, k).Any();

        // Where the keyword stands as a word of its own (ASCII ends must not run into letters or digits).
        public static IEnumerable<int> Find(string text, Key k)
        {
            for (int at = text.IndexOf(k.Value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(k.Value, at + 1, StringComparison.Ordinal))
            {
                bool startOk = !k.AsciiStart || at == 0 || !IsAsciiLetter(text[at - 1]) && !(char.IsAsciiDigit(text[at - 1]) && char.IsAsciiDigit(k.Value[0]));
                int end = at + k.Value.Length;
                bool endOk = !k.AsciiEnd || end >= text.Length || !IsAsciiLetter(text[end]) && !(char.IsAsciiDigit(text[end]) && char.IsAsciiDigit(k.Value[^1]));
                if (startOk && endOk) yield return at;
            }
        }

        public static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            char prev = ' ';
            foreach (char raw in s)
            {
                char c = char.ToLowerInvariant(raw);
                char o = char.IsLetterOrDigit(c) || c == '+' ? c : ' ';
                // A space between Latin and CJK runs, so "qq邮箱" reads as "qq 邮箱".
                if (o != ' ' && prev != ' ' && IsAsciiAlnum(o) != IsAsciiAlnum(prev) && (o > 0x2E7F || prev > 0x2E7F)) sb.Append(' ');
                if (o == ' ' && prev == ' ') continue;
                sb.Append(o);
                prev = o;
            }
            return sb.ToString().Trim();
        }

        private static string SplitCamel(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0)
                {
                    char p = s[i - 1];
                    bool lowerToUpper = char.IsAsciiLetterLower(p) && char.IsAsciiLetterUpper(c);
                    bool acronymEnd = char.IsAsciiLetterUpper(p) && char.IsAsciiLetterUpper(c) && i + 1 < s.Length && char.IsAsciiLetterLower(s[i + 1]);
                    bool digitToLetter = char.IsAsciiDigit(p) && char.IsAsciiLetter(c);
                    if (lowerToUpper || acronymEnd || digitToLetter) sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    private static bool IsAsciiAlnum(char c) => IsAsciiLetter(c) || char.IsAsciiDigit(c);
}
