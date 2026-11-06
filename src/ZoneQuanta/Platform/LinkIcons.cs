using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ZoneQuanta.Platform;

// The picture of a link that has none yet: a tile in the link's own colour (or one picked steadily from its
// address) with its first letters, the way launchers draw a site without an icon. 256 px PNG; IconImages
// rounds the corners when it is shown. STA thread (WPF drawing).
internal static class LinkIcons
{
    public const int Side = 256;
    private const string Font = "Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI";

    // Muted, evenly bright hues that sit well on the dark surfaces and stay readable with white letters.
    private static readonly Color[] Palette =
    {
        Rgb(0x4F7FD9), Rgb(0x3E9C8F), Rgb(0xC0834A), Rgb(0xB0616E), Rgb(0x7A68B8), Rgb(0x3F8DB0),
        Rgb(0x6A8F55), Rgb(0xB8743F), Rgb(0x5D6B85), Rgb(0x9C6F4A), Rgb(0x8A5A9E), Rgb(0x4C8C6A),
    };

    public static void Letter(string file, string title, string? badge, string? color, string key)
    {
        var bg = Parse(color) is { } c && Luminance(c) < 0.86 && c.A > 200 ? c : Palette[Stable(key) % Palette.Length];
        string text = Initials(badge is { Length: > 0 } b && b.Trim().Length > 0 ? b : title);
        var ink = Contrast(Colors.White, bg) >= 3.0 ? Colors.White : Rgb(0x1C1E23);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var fill = new LinearGradientBrush(Shade(bg, 0.10), Shade(bg, -0.08), 90);
            dc.DrawRectangle(fill, null, new Rect(0, 0, Side, Side));
            if (text.Length > 0)
            {
                bool cjk = text.Any(IsCjk);
                double size = text.Length == 1 ? 132 : cjk ? 100 : 108;
                var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(Font), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, new SolidColorBrush(ink), 1.0);
                // Optical centre: the ink box, not the line box with its ascender room.
                var box = ft.BuildGeometry(new Point(0, 0)).Bounds;
                double x = (Side - box.Width) / 2 - box.X, y = (Side - box.Height) / 2 - box.Y;
                dc.DrawText(ft, new Point(x, y));
            }
        }
        var bmp = new RenderTargetBitmap(Side, Side, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using (var fs = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) enc.Save(fs);
        File.Move(file + ".tmp", file, overwrite: true);
    }

    // Two CJK characters ("春秋"), two initials of a multi-word or camel-case Latin name ("GT", "CH"), else one letter.
    public static string Initials(string name)
    {
        string s = Regex.Replace(name, @"[\(（\[【][^\)）\]】]*[\)）\]】]", " ").Trim();
        if (s.Length == 0) return "";
        int first = 0;
        while (first < s.Length && !char.IsLetterOrDigit(s[first])) first++;
        if (first >= s.Length) return "";
        if (IsCjk(s[first])) return new string(s.Skip(first).Where(IsCjk).Take(2).ToArray());
        var words = Regex.Split(Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " "), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0 && !IsCjk(w[0])).ToList();
        if (words.Count >= 2) return (char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[1][0])).Trim();
        return words.Count == 1 ? char.ToUpperInvariant(words[0][0]).ToString() : s[first].ToString();
    }

    private static bool IsCjk(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '가' and <= '힯' or >= '぀' and <= 'ヿ';

    // "#rgb", "#rrggbb" or "#aarrggbb"; anything else (transparent, a typo) is no colour.
    private static Color? Parse(string? s)
    {
        if (s is null) return null;
        string h = s.Trim().TrimStart('#');
        if (!Regex.IsMatch(h, "^([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")) return null;
        if (h.Length == 3) h = string.Concat(h.Select(ch => $"{ch}{ch}"));
        uint v = uint.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return h.Length == 8 ? Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v) : Rgb(v);
    }

    private static Color Rgb(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);

    // FNV-1a: the same address always picks the same colour.
    private static int Stable(string key)
    {
        uint h = 2166136261;
        foreach (char ch in key.ToLowerInvariant()) h = (h ^ ch) * 16777619;
        return (int)(h & 0x7FFFFFFF);
    }

    private static Color Shade(Color c, double k)
    {
        byte F(byte v) => (byte)Math.Clamp(k >= 0 ? v + (255 - v) * k : v * (1 + k), 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    // WCAG relative luminance and contrast ratio.
    private static double Luminance(Color c)
    {
        static double L(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
