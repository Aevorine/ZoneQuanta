using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.UI.Panel.Pages;
using ZoneQuanta.UI.Theme;

namespace ZoneQuanta.Shell;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _panel, _show, _band, _through, _lock, _top, _fullscreen, _autostart;
    private readonly ToolStripMenuItem _quick, _path, _quickList;
    private readonly Func<IEnumerable<(string Name, Action Open)>> _quickItems;

    public TrayService(AppSettings settings, Action togglePanel, Action resetPosition, Action checkUpdate, Action exit,
        Action toggleQuick, Action showPathBar, Func<IEnumerable<(string Name, Action Open)>> quickItems)
    {
        _settings = settings;
        _quickItems = quickItems;

        _panel = Item("显示 / 隐藏面板", togglePanel);
        _quick = Item("快捷面板", toggleQuick);
        _path = Item("打开路径…", showPathBar);
        _quickList = new ToolStripMenuItem("快捷项");
        _show = Check("显示时钟", nameof(AppSettings.WidgetVisible), () => settings.WidgetVisible = !settings.WidgetVisible);
        _band = Check("任务栏监控条", nameof(AppSettings.BandVisible), () => settings.BandVisible = !settings.BandVisible);
        _through = Check("鼠标穿透", nameof(AppSettings.ClickThrough), () => settings.ClickThrough = !settings.ClickThrough);
        _lock = Check("锁定位置", nameof(AppSettings.LockPosition), () => settings.LockPosition = !settings.LockPosition);
        _top = Check("总是置顶", nameof(AppSettings.Topmost), () => settings.Topmost = !settings.Topmost);
        _fullscreen = Check("全屏时隐藏", nameof(AppSettings.HideOnFullscreen), () => settings.HideOnFullscreen = !settings.HideOnFullscreen);
        _autostart = Check("开机自启", nameof(AppSettings.AutoStart), () => settings.AutoStart = !settings.AutoStart);

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _panel, _quick, _path, _quickList, new ToolStripSeparator(),
            _show, _band, _through, _lock, _top, _fullscreen, _autostart, new ToolStripSeparator(),
            Item("重置位置", resetPosition), Item("检查更新", checkUpdate), new ToolStripSeparator(),
            Item("退出", exit),
        });
        _menu.Font = new Font("Microsoft YaHei UI", 9.5f);
        _menu.ShowImageMargin = false;
        _menu.ShowCheckMargin = true;
        _menu.Opening += (_, _) => Sync();

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "ZoneQuanta",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) togglePanel();
        };
    }

    public void SetTooltip(string text) => _icon.Text = text.Length > 60 ? text[..60] : text;

    public void Notify(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);

    private void Sync()
    {
        var p = ThemeManager.Find(_settings.Theme);
        var back = ColorTranslator.FromHtml(p.Card);
        var fore = ColorTranslator.FromHtml(p.Text);
        var renderer = new ThemedRenderer(new ThemedColors(p));
        _menu.Renderer = renderer;
        _menu.BackColor = back;
        _menu.ForeColor = fore;
        foreach (ToolStripItem i in _menu.Items)
        {
            i.BackColor = back;
            i.ForeColor = fore;
        }

        _panel.ShortcutKeyDisplayString = _settings.HotkeyEnabled ? SystemPage.Format(_settings.HotkeyModifiers, _settings.HotkeyKey) : null;
        _quick.ShortcutKeyDisplayString = _settings.QuickHotkeyEnabled ? SystemPage.Format(_settings.QuickHotkeyModifiers, _settings.QuickHotkeyKey) : null;
        _path.ShortcutKeyDisplayString = _settings.PathHotkeyEnabled ? SystemPage.Format(_settings.PathHotkeyModifiers, _settings.PathHotkeyKey) : null;
        _quickList.DropDownItems.Clear();
        if (_settings.QuickInTray)
            foreach (var (name, open) in _quickItems())
            {
                var item = Item(name, open);
                item.BackColor = back;
                item.ForeColor = fore;
                _quickList.DropDownItems.Add(item);
            }
        _quickList.Visible = _quickList.DropDownItems.Count > 0;
        if (_quickList.DropDown is ToolStripDropDownMenu dd)
        {
            dd.Renderer = renderer;
            dd.BackColor = back;
            dd.ShowImageMargin = false;
            dd.Font = _menu.Font;
        }

        _show.Checked = _settings.WidgetVisible;
        _band.Checked = _settings.BandVisible;
        _through.Checked = _settings.ClickThrough;
        _lock.Checked = _settings.LockPosition;
        _top.Checked = _settings.Topmost;
        _fullscreen.Checked = _settings.HideOnFullscreen;
        _autostart.Checked = _settings.AutoStart;
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private ToolStripMenuItem Check(string text, string _, Action toggle) => Item(text, toggle);

    private static Icon LoadIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using Stream s = info.Stream;
        return new Icon(s);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private sealed class ThemedColors : ProfessionalColorTable
    {
        private readonly Color _back, _hover, _line;

        public ThemedColors(Palette p)
        {
            _back = ColorTranslator.FromHtml(p.Card);
            _hover = ColorTranslator.FromHtml(p.Raised);
            _line = ColorTranslator.FromHtml(p.Line);
        }

        public override Color ToolStripDropDownBackground => _back;
        public override Color ImageMarginGradientBegin => _back;
        public override Color ImageMarginGradientMiddle => _back;
        public override Color ImageMarginGradientEnd => _back;
        public override Color MenuBorder => _line;
        public override Color MenuItemBorder => _hover;
        public override Color MenuItemSelected => _hover;
        public override Color MenuItemSelectedGradientBegin => _hover;
        public override Color MenuItemSelectedGradientEnd => _hover;
        public override Color SeparatorDark => _line;
        public override Color SeparatorLight => _line;
        public override Color CheckBackground => _hover;
        public override Color CheckSelectedBackground => _hover;
        public override Color CheckPressedBackground => _hover;
    }

    private sealed class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public ThemedRenderer(ProfessionalColorTable table) : base(table) => RoundedEdges = false;
    }
}
