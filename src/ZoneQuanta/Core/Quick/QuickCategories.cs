using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Quick;

// What is known about a launch target beyond its name: the shortcut's target and the program's own
// description. Filled in by the platform layer.
public sealed record TargetHint(string Target, string Arguments, string Description, string Product, string Company = "")
{
    public bool IsWebApp
    {
        get
        {
            string exe = System.IO.Path.GetFileName(Target).ToLowerInvariant();
            if (exe is "chrome_proxy.exe" or "msedge_proxy.exe") return true;
            return exe is "chrome.exe" or "msedge.exe" or "brave.exe" or "vivaldi.exe"
                && (Arguments.Contains("--app-id", StringComparison.OrdinalIgnoreCase) || Arguments.Contains("--app=", StringComparison.OrdinalIgnoreCase));
        }
    }
}

// Automatic grouping of quick items. The item's own name decides first, then the program behind a
// shortcut, then the kind of file; the result is never empty, so each item is classified once.
public static class QuickCategories
{
    public const string Ai = "AI", Browser = "浏览器", Social = "社交通讯", Mail = "邮件", Office = "办公学习",
        Dev = "开发", Engineering = "嵌入式与工程", Design = "设计", Media = "影音", Games = "游戏", Shopping = "购物",
        News = "资讯", Network = "网络与远程", Cloud = "网盘下载", SystemTools = "系统工具", Web = "网站",
        Folders = "文件夹", Documents = "文档", MediaFiles = "媒体文件", Files = "文件", OtherApps = "其他应用";

    // Display order of groups created together.
    public static readonly IReadOnlyList<string> Order = new[]
    {
        Ai, Browser, Social, Mail, Office, Dev, Engineering, Design, Media, Games, Shopping, News, Network, Cloud,
        SystemTools, Web, Folders, Documents, MediaFiles, Files, OtherApps,
    };

    // Checked in this order, so the more specific category wins (QQ邮箱 is mail before QQ is social,
    // 夸克网盘 is cloud storage before 夸克 is a browser, Steam++ is a network tool before Steam is a game).
    // ASCII keywords match whole words (digits count as a break, so "uvision" matches "uVision5");
    // a leading '=' means the whole name must equal it.
    private static readonly (string Group, string[] Keys)[] Rules =
    {
        (Mail, new[] { "邮箱", "邮件", "mail", "gmail", "outlook", "foxmail", "thunderbird", "spark mail" }),
        (Ai, new[] { "claude", "chatgpt", "openai", "codex", "deepseek", "gemini", "grok", "kimi", "豆包", "doubao", "千问", "qwen", "通义",
            "文心", "ernie", "copilot", "perplexity", "poe", "智谱", "chatglm", "元宝", "秘塔", "metaso", "minimax", "海螺", "cc switch",
            "cherry studio", "lm studio", "ollama", "midjourney", "stable diffusion", "comfyui", "cursor", "windsurf", "trae", "ai助手" }),
        (Cloud, new[] { "网盘", "云盘", "onedrive", "dropbox", "google drive", "icloud", "坚果云", "nextcloud", "mega", "idm",
            "internet download manager", "迅雷", "qbittorrent", "bitcomet", "motrix", "aria2", "m3u8", "m3u8dl", "下载器", "downloader",
            "free download manager", "abdownloadmanager", "百度网盘", "阿里云盘", "115" }),
        (Network, new[] { "v2ray", "v2rayn", "clash", "clash verge", "shadowsocks", "shadowsocksr", "xray", "sing box", "nekoray", "hiddify",
            "vpn", "speedtest", "speed test", "测速", "加速器", "steam++", "watt toolkit", "localsend", "wireshark", "fiddler", "charles",
            "proxifier", "远程", "remote", "todesk", "anydesk", "teamviewer", "向日葵", "sunlogin", "rustdesk", "mstsc", "parsec",
            "finalshell", "xshell", "putty", "mobaxterm", "termius", "winscp", "filezilla", "tailscale", "zerotier", "网络" }),
        (Browser, new[] { "chrome", "edge", "firefox", "opera", "brave", "vivaldi", "夸克", "quark", "浏览器", "browser", "safari",
            "chromium", "360se", "internet explorer", "maxthon", "傲游", "tor browser", "waterfox", "librewolf", "thorium" }),
        (Shopping, new[] { "闲鱼", "淘宝", "taobao", "天猫", "tmall", "京东", "拼多多", "pinduoduo", "amazon", "亚马逊", "ebay", "美团",
            "饿了么", "得物", "唯品会", "苏宁", "当当", "aliexpress", "支付宝", "alipay", "paypal", "temu", "shein", "1688", "转转" }),
        (Social, new[] { "微信", "wechat", "weixin", "qq", "tim", "telegram", "discord", "钉钉", "dingtalk", "飞书", "feishu", "lark",
            "slack", "teams", "zoom", "whatsapp", "line", "skype", "signal", "企业微信", "wxwork", "腾讯会议", "wemeet", "messenger",
            "微博", "weibo", "=x", "twitter", "facebook", "instagram", "reddit", "小红书", "豆瓣", "贴吧", "linkedin", "threads",
            "mastodon", "kook", "yy语音" }),
        (News, new[] { "知乎", "zhihu", "wikipedia", "维基", "百科", "cnn", "bbc", "news", "新闻", "头条", "nytimes", "reuters",
            "bloomberg", "the verge", "hacker news", "36氪", "虎嗅", "少数派", "stack overflow", "stackoverflow", "medium", "quora" }),
        (Media, new[] { "抖音", "douyin", "tiktok", "哔哩哔哩", "bilibili", "youtube", "爱奇艺", "iqiyi", "优酷", "youku", "腾讯视频",
            "芒果tv", "netflix", "twitch", "spotify", "网易云", "cloudmusic", "qq音乐", "qqmusic", "酷狗", "kugou", "酷我", "potplayer",
            "vlc", "mpv", "播放器", "player", "media player", "obs", "obs studio", "录音", "recorder", "照片", "photos", "剪映", "capcut",
            "premiere", "davinci", "audacity", "foobar2000", "aimp", "musicbee", "kmplayer", "直播", "斗鱼", "虎牙", "快手", "西瓜视频",
            "camera", "相机", "音乐", "music", "视频", "video", "movies", "电影", "itunes", "apple music", "plex", "jellyfin", "emby" }),
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
            "虚拟机", "developer", "开发", "devtools", "neovim", "hoppscotch", "heidisql", "lazygit", "repositories", "repos" }),
        (Design, new[] { "画图", "paint", "mspaint", "photoshop", "illustrator", "figma", "gimp", "krita", "inkscape", "blender",
            "sketchup", "canva", "coreldraw", "lightroom", "after effects", "3ds max", "maya", "c4d", "cinema 4d", "美图", "稿定",
            "affinity", "pixso", "即时设计", "xd", "procreate", "clip studio", "darktable", "rawtherapee" }),
        (Office, new[] { "word", "excel", "powerpoint", "onenote", "office", "microsoft 365", "wps", "to do", "todo", "notion",
            "obsidian", "typora", "记事本", "notepad", "acrobat", "pdf", "xmind", "腾讯文档", "石墨", "evernote", "印象笔记", "有道",
            "翻译", "translate", "translator", "词典", "dictionary", "学习通", "mooc", "zotero", "endnote", "mendeley", "anki", "知网",
            "cnki", "日历", "calendar", "便笺", "sticky notes", "语雀", "logseq", "joplin", "marktext", "latex", "texstudio", "overleaf",
            "金山文档", "飞书文档", "幕布", "flomo", "滴答清单", "ticktick", "todoist", "trello", "思维导图", "mindmap", "teams classic",
            "学习", "课堂", "雨课堂", "超星", "钉钉文档", "kindle", "阅读", "reader", "calibre", "ebook" }),
        (SystemTools, new[] { "设置", "settings", "immersivecontrolpanel", "控制面板", "control panel", "file explorer", "资源管理器", "explorer",
            "此电脑", "this pc", "计算器", "calculator", "resource monitor", "资源监视器", "task scheduler", "任务计划", "task manager",
            "任务管理器", "时钟", "alarms", "clock", "截图", "screensketch", "snipping", "pixpin", "snipaste", "sharex", "7 zip", "7zip",
            "winrar", "bandizip", "压缩", "360zip", "everything", "quicklook", "listary", "wox", "powertoys", "zonequanta", "uninstall",
            "uninstaller", "卸载", "geek", "repair", "电脑管家", "管家", "扩容", "安全卫士", "卫士", "杀毒", "antivirus", "defender",
            "安全中心", "memory zone", "sandisk", "回收站", "recycle bin", "regedit", "注册表", "device manager", "设备管理器", "磁盘",
            "disk", "diskgenius", "cleaner", "ccleaner", "dism++", "microsoft store", "windowsstore", "应用商店", "store", "驱动",
            "driver", "备份", "backup", "rufus", "ventoy", "cpu z", "gpu z", "hwinfo", "aida64", "crystaldiskinfo", "crystaldiskmark",
            "ditto", "clipboard", "剪贴板", "event viewer", "事件查看器", "services", "服务", "系统信息", "system information",
            "computer management", "计算机管理", "performance monitor", "性能监视器", "windows tools", "windows 工具", "rainmeter",
            "translucenttb", "startallback", "explorerpatcher", "nilesoft", "f.lux", "monitorian", "twinkle tray", "wallpaper",
            "壁纸", "图吧工具箱", "dism", "火绒", "huorong", "联想电脑管家", "legion", "armoury", "omen" }),
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

    private static readonly Regex Email = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
    private static readonly Regex Bracketed = new(@"[\(（\[【][^\)）\]】]*[\)）\]】]", RegexOptions.Compiled);

    private static readonly (string Group, Key[] Keys)[] Compiled =
        Rules.Select(r => (r.Group, r.Keys.Select(Key.Of).ToArray())).ToArray();

    public static string Classify(string path, string displayName, TargetHint? hint)
    {
        bool app = path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase);
        // "Claude (<account>@gmail.com)" is Claude: account labels and bracketed notes do not name the app,
        // so the bare name decides first and an e-mail address never counts.
        string name = Email.Replace(displayName, " ").Trim();
        string core = Bracketed.Replace(name, " ").Trim();
        if (core.Length > 0 && Match(Text.Of(core), core) is { } byCore) return byCore;
        string own = name + " " + Email.Replace(System.IO.Path.GetFileNameWithoutExtension(path.TrimEnd('\\')), " ");
        if (app) own += " " + path[17..];
        if (Match(Text.Of(own), name) is { } byName) return byName;

        bool web = hint?.IsWebApp == true;
        if (hint is not null && !web)
        {
            string program = System.IO.Path.GetFileNameWithoutExtension(hint.Target) + " " + hint.Description + " " + hint.Product;
            if (Match(Text.Of(program), null) is { } byProgram) return byProgram;
        }

        if (web) return Web;
        if (app) return OtherApps;
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

    private static string? Match(Text text, string? wholeName)
    {
        foreach (var (group, keys) in Compiled)
            foreach (var k in keys)
                if (k.Exact ? wholeName is not null && string.Equals(wholeName, k.Value, StringComparison.OrdinalIgnoreCase) : text.Has(k))
                    return group;
        return null;
    }

    private readonly record struct Key(string Value, bool Exact, bool AsciiStart, bool AsciiEnd)
    {
        public static Key Of(string raw)
        {
            if (raw.StartsWith('=')) return new Key(raw[1..], true, false, false);
            string v = Text.Normalize(raw);
            return new Key(v, false, IsAsciiAlnum(v[0]), IsAsciiAlnum(v[^1]));
        }
    }

    // Lower-cased name in two spellings: as written ("onedrive") and with camel-case / digit runs split
    // ("one drive"), separators turned into single spaces.
    private readonly record struct Text(string Plain, string Split)
    {
        public static Text Of(string s) => new(" " + Normalize(s) + " ", " " + Normalize(SplitCamel(s)) + " ");

        public bool Has(Key k) => Contains(Plain, k) || Contains(Split, k);

        private static bool Contains(string text, Key k)
        {
            for (int at = text.IndexOf(k.Value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(k.Value, at + 1, StringComparison.Ordinal))
            {
                bool startOk = !k.AsciiStart || !IsAsciiLetter(text[at - 1]) && !(char.IsAsciiDigit(text[at - 1]) && char.IsAsciiDigit(k.Value[0]));
                int end = at + k.Value.Length;
                bool endOk = !k.AsciiEnd || end >= text.Length || !IsAsciiLetter(text[end]) && !(char.IsAsciiDigit(text[end]) && char.IsAsciiDigit(k.Value[^1]));
                if (startOk && endOk) return true;
            }
            return false;
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
