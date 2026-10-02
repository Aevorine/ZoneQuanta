using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ZoneQuanta.UI.Theme;

public sealed record Palette(string Name, string Display, string Surface, string Card, string Raised, string Text, string Muted, string Line, string Accent1, string Accent2, string Sun, string Moon);

public static class ThemeManager
{
    public static IReadOnlyList<Palette> Palettes { get; } = new[]
    {
        new Palette("Graphite", "石墨", "#1C1E23", "#252830", "#2D313A", "#D8DBE2", "#858B98", "#343843", "#6FB7AC", "#E2AE74", "#EBC27A", "#B9C4DB"),
        new Palette("Dusk", "暮紫", "#1D1B26", "#272437", "#302C44", "#DAD6E8", "#8C87A3", "#38344D", "#A094E3", "#E59A8C", "#EFC38A", "#C2BDE0"),
        new Palette("Forest", "松针", "#1A211E", "#232D29", "#2B3732", "#D5DDD8", "#7F8E86", "#313F39", "#7DBE8F", "#D6B97A", "#E4C87F", "#B8CBC1"),
        new Palette("Paper", "纸本", "#E9E5DC", "#F4F1EA", "#FBF9F4", "#2E3138", "#7A7F8A", "#D3CEC2", "#3F8F86", "#B7803F", "#C9902F", "#6D7A99"),
    };

    public static Palette Find(string name)
    {
        foreach (var p in Palettes) if (p.Name == name) return p;
        return Palettes[0];
    }

    public static void Apply(string name)
    {
        var p = Find(name);
        var r = Application.Current.Resources;
        Put(r, "SurfaceBrush", p.Surface);
        Put(r, "CardBrush", p.Card);
        Put(r, "RaisedBrush", p.Raised);
        Put(r, "TextBrush", p.Text);
        Put(r, "MutedBrush", p.Muted);
        Put(r, "LineBrush", p.Line);
        Put(r, "Accent1Brush", p.Accent1);
        Put(r, "Accent2Brush", p.Accent2);
        Put(r, "SunBrush", p.Sun);
        Put(r, "MoonBrush", p.Moon);
        r["Accent1Color"] = Parse(p.Accent1);
        r["Accent2Color"] = Parse(p.Accent2);
        r["CardColor"] = Parse(p.Card);
    }

    public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Put(ResourceDictionary r, string key, string hex)
    {
        var b = new SolidColorBrush(Parse(hex));
        b.Freeze();
        r[key] = b;
    }
}
