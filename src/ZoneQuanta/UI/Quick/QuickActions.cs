using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Quick;

// Operations on quick items shared by the launcher window, the path bar, the panel page and the tray.
public sealed class QuickActions
{
    public QuickActions(QuickStore store, AppSettings settings)
    {
        Store = store;
        Settings = settings;
    }

    public QuickStore Store { get; }
    public AppSettings Settings { get; }
    public QuickItem? Highlight { get; private set; }
    public event Action? Opened;
    public event Action<string>? Failed;

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public void Open(QuickItem item)
    {
        var sw = Stopwatch.StartNew();
        if (!Exists(item.Path) && !PathEngine.IsShell(item.Path))
        {
            Failed?.Invoke(item.Path);
            return;
        }
        if (!ShellLauncher.Open(item.Path))
        {
            Failed?.Invoke(item.Path);
            return;
        }
        Store.Touch(item);
        Log.Write($"quick open {sw.Elapsed.TotalMilliseconds:0.0}ms {Path.GetExtension(item.Path)}");
        Opened?.Invoke();
    }

    // Typed or pasted text: path, quoted path, %VAR%, ~, shell:, URL or a folder alias.
    public bool OpenText(string text)
    {
        var target = PathEngine.Resolve(text);
        if (target.Kind is TargetKind.None or TargetKind.Missing)
        {
            Failed?.Invoke(target.Value);
            return false;
        }
        if (!ShellLauncher.Open(target.Value))
        {
            Failed?.Invoke(target.Value);
            return false;
        }
        if (target.Kind != TargetKind.Url) Store.Remember(target.Value);
        if (Store.Find(target.Value) is { } item) Store.Touch(item);
        Log.Write($"path open {target.Kind}");
        Opened?.Invoke();
        return true;
    }

    public void RunAsAdmin(QuickItem item)
    {
        if (ShellLauncher.RunAsAdmin(item.Path))
        {
            Store.Touch(item);
            Opened?.Invoke();
        }
    }

    public void OpenWith(QuickItem item) => ShellLauncher.OpenWith(item.Path);
    public void Reveal(QuickItem item) => ShellLauncher.Reveal(item.Path);
    public void Properties(QuickItem item) => ShellLauncher.Properties(item.Path);
    public void Remove(QuickItem item) => Store.Remove(item);
    public void Rename(QuickItem item, string name) => Store.Rename(item, name);
    public void Move(QuickItem item, int index) => Store.Move(item, index);

    public void CopyPath(QuickItem item)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(item.Path, true);
                return;
            }
            catch (COMException) { System.Threading.Thread.Sleep(30); } // clipboard held by another process
        }
    }

    public List<QuickItem> Add(IEnumerable<string> paths, int index = -1)
    {
        var added = new List<QuickItem>();
        foreach (string raw in paths)
        {
            string p = PathEngine.Clean(raw);
            if (p.Length == 0 || !(Exists(p) || PathEngine.IsShell(p))) continue;
            if (!PathEngine.IsShell(p) && !Path.IsPathFullyQualified(p)) p = Path.GetFullPath(p);
            var (item, isNew) = Store.Add(p, index < 0 ? -1 : index + added.Count);
            if (isNew) added.Add(item);
            Highlight = item;
        }
        if (added.Count > 0) Log.Write($"quick add {added.Count}");
        return added;
    }

    public void ClearHighlight() => Highlight = null;

    public IEnumerable<QuickItem> View(string filter)
    {
        var items = Store.Items;
        string f = filter.Trim();
        if (f.Length > 0)
            return items.Select(i => (i, s: Math.Max(Fuzzy.Score(f, i.DisplayName), Fuzzy.Score(f, i.Path))))
                .Where(x => x.s >= 0).OrderByDescending(x => x.s).Select(x => x.i);
        long now = QuickStore.Now;
        return Settings.QuickSort switch
        {
            "Name" => items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "Recent" => items.OrderByDescending(i => i.LastOpenedUtc).ThenByDescending(i => i.AddedUtc),
            "Frequent" => items.OrderByDescending(i => i.Score(now)).ThenByDescending(i => i.OpenCount),
            "Type" => items.OrderBy(i => Directory.Exists(i.Path) ? "" : Path.GetExtension(i.Path).ToLowerInvariant()).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => items,
        };
    }

    public static string TypeName(string path)
    {
        if (PathEngine.IsShell(path)) return "系统位置";
        string t = path.TrimEnd('\\');
        if (t.Length == 2 && t[1] == ':') return "磁盘";
        if (Directory.Exists(path)) return "文件夹";
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "" => "文件",
            "lnk" => "快捷方式",
            "exe" => "应用程序",
            "url" => "网址",
            _ => ext.ToUpperInvariant(),
        };
    }
}
