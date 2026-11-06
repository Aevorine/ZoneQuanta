using System;
using System.Windows;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.UI.Quick;

// Folder view geometry (DIP), independent of the board's spacing: a cell wide enough for a name at each density,
// the icon at its usual size, and the panel size for each size mode. Pure arithmetic; the view applies it.
public readonly record struct FolderMetrics(double Cell, double RowGap, double PadX, double PadY, double Content)
{
    public const double MinWidth = 440, MinHeight = 240, Margin = 24, AutoWidth = 640;
    // Scroll area inside the panel: clear of the right edge and of the rounded bottom corner (resize grip);
    // a scroll bar takes Gutter from the icons' width only while the rows overflow.
    public const double ScrollRight = 4, ScrollBottom = 14, Gutter = 8;
    // Named sizes of the menu: Custom with these values.
    public static readonly (string Name, double W, double H)[] Presets = { ("小", 640, 420), ("中", 880, 580), ("大", 1120, 740) };
    public static readonly (string Key, string Name)[] Densities = { ("Compact", "紧凑"), ("Standard", "标准"), ("Loose", "宽松") };

    public double Pitch => Content + RowGap;
    // Space above the first row (tiles sit RowGap / 2 into their row, so the icons start PadY below the header).
    public double BlockPad => Math.Max(0, PadY - RowGap / 2);
    // Hover box of a tile in a slot: a little narrower than the slot, never wider than a name needs.
    public double TileWidth(double slot) => Math.Clamp(slot - 6, 46, Cell + 28);

    public static FolderMetrics From(AppSettings s, double icon)
    {
        double content = icon + (s.QuickShowName ? BoardMetrics.NameBlock : 0);
        return s.QuickFolderDensity switch
        {
            "Compact" => new(86, 12, 16, 12, content),
            "Loose" => new(124, 30, 30, 22, content),
            _ => new(104, 20, 22, 16, content),
        };
    }

    public int ColsFor(double inner) => Math.Max(1, (int)Math.Floor((inner + 0.5) / Cell));

    // Width the icons get in a panel w wide.
    public double Inner(double w, bool scroll) => Math.Max(0, w - 2 - ScrollRight - 2 * PadX - (scroll ? Gutter : 0));

    // Height of the scroll area in a panel h high under a header.
    public static double ViewHeight(double h, double header) => Math.Max(0, h - 2 - header - ScrollBottom);

    // Columns and rows for n members in a panel w wide whose scroll area is viewH high.
    public (int Cols, int Rows, bool Scroll) Grid(int n, double w, double viewH)
    {
        n = Math.Max(1, n);
        int cols = ColsFor(Inner(w, false));
        int rows = (int)Math.Ceiling(n / (double)cols);
        bool scroll = rows * Pitch + BlockPad > viewH + 0.5;
        if (scroll)
        {
            cols = ColsFor(Inner(w, true));
            rows = (int)Math.Ceiling(n / (double)cols);
        }
        return (cols, rows, scroll);
    }

    // Panel size inside the board area: Fill takes all of it, Custom the remembered size, Auto a block close to
    // 3:2 (at least the card's own columns, at least AutoWidth wide when there is room) as tall as its rows.
    public Size PanelSize(AppSettings s, int n, int cardCols, Size room, double header, double minWidth)
    {
        double maxW = Math.Max(160, room.Width), maxH = Math.Max(120, room.Height);
        double minW = Math.Min(Math.Max(MinWidth, minWidth), maxW), minH = Math.Min(MinHeight, maxH);
        if (s.QuickFolderSize == "Fill") return new Size(maxW, maxH);
        if (s.QuickFolderSize == "Custom" && s.QuickFolderWidth > 0 && s.QuickFolderHeight > 0)
            return new Size(Math.Clamp(s.QuickFolderWidth, minW, maxW), Math.Clamp(s.QuickFolderHeight, minH, maxH));
        n = Math.Max(1, n);
        int want = Math.Max(Math.Min(n, Math.Max(5, cardCols)), (int)Math.Ceiling(Math.Sqrt(n * 1.8)));
        double width = Math.Clamp(Math.Max(want * Cell + 2 * PadX + 2 + ScrollRight + Gutter, AutoWidth), minW, maxW);
        int cols = ColsFor(Inner(width, true));
        int rows = (int)Math.Ceiling(n / (double)cols);
        double height = Math.Clamp(header + rows * Pitch + BlockPad + ScrollBottom + 2, minH, maxH);
        return new Size(width, height);
    }
}
