using System;
using ZoneQuanta.Core.Settings;

namespace ZoneQuanta.UI.Quick;

// Geometry of the grouped board (DIP). The grid cell keeps the 1.5.0 size (84 × 80 with names, 68 × 58 without)
// and the icon size stays 42; the spacing settings decide how many icons a card of c × r cells holds:
// icon gaps Gx / Gy, icon-to-border distances Px / Py. Tighter spacing = more icons per card and smaller cards
// for small groups (more groups on screen). The gap between cards G changes the grid itself.
// At the defaults (-1) every card holds exactly c × r icons, as before.
public readonly record struct BoardMetrics(
    double Icon, double Name, double GapX, double GapY, double PadX, double PadY, double Group,
    double CellWidth, double CellHeight, double DefaultPitchY, bool Standard)
{
    public const double IconSize = 42;
    // Room for the name line under an icon (6 margin + one 11.5 px line, rounded up).
    public const double NameLine = 24;
    public const double LabelStrip = 34;
    // A card may squeeze its icons by up to this much in total before it shows one column fewer
    // (keeps a 2-column card at two columns in the 1.5.0 grid).
    private const double SqueezeX = 24;
    private const double DefaultGroupGap = 14;

    // The 1.5.0 board measured at its narrowest cells: gaps in a 4 × 2 card.
    public static (int GapX, int GapY) DefaultGaps(bool names) => names ? (48, 28) : (32, 19);

    public static BoardMetrics From(AppSettings s)
    {
        bool names = s.QuickShowName;
        const double icon = IconSize;
        var (dx, dy) = DefaultGaps(names);
        double gx = s.QuickGapX >= 0 ? s.QuickGapX : dx;
        double gy = s.QuickGapY >= 0 ? s.QuickGapY : dy;
        double px = s.QuickPadX >= 0 ? s.QuickPadX : 8 + gx / 2;
        double py = s.QuickPadY >= 0 ? s.QuickPadY : 8 + gy / 2;
        double name = names ? NameLine : 0;
        // The 1.5.0 cell: a 4-column card exact at the default gaps and card padding 8.
        double cellW = icon + dx + (2 * 8 + 2 - 3 * DefaultGroupGap) / 4;
        double cellH = (2 * (icon + name + dy) + LabelStrip + 2 + 2 * 8) / 3;
        bool standard = Math.Abs(gx - dx) < 0.01 && Math.Abs(gy - dy) < 0.01 && Math.Abs(px - (8 + dx / 2.0)) < 0.01 && Math.Abs(py - (8 + dy / 2.0)) < 0.01;
        return new BoardMetrics(icon, name, gx, gy, px, py, s.QuickGroupGap, cellW, cellH, icon + name + dy, standard);
    }

    // Card padding inside the border (negative when the border sits closer than half a gap).
    public double InsetX => PadX - GapX / 2;
    public double InsetY => PadY - GapY / 2;
    public double PitchX => Icon + GapX;
    public double PitchY => Icon + Name + GapY;
    // Widest a loose or card tile is drawn (its hover box).
    public double TileWidth => CellWidth - 4;
    public double MiniCell => 40;
    // Smallest row a tile fits in: icon, name line (6 + 16) and 2 to spare.
    private double MinRow => Icon + (Name > 0 ? 22 : 0) + 2;

    // Icon columns a card c cells wide holds (measured on the 1.5.0 grid, so a wider window never adds columns).
    public int ColsFor(int cells)
    {
        if (Standard) return cells;
        double inner = cells * CellWidth + (cells - 1) * DefaultGroupGap - 2 - 2 * InsetX;
        int cols = (int)Math.Floor((inner + SqueezeX) / PitchX);
        return Math.Clamp(cols, 1, Math.Max(1, (int)Math.Floor(inner / (Icon + 4))));
    }

    // Icon rows a card r cells tall holds (it covers r + 1 grid rows: the extra one holds the border and name).
    public int RowsFor(int rows)
    {
        if (Standard) return rows;
        double inner = (rows + 1) * CellHeight - LabelStrip - 2 - 2 * InsetY;
        int n = (int)Math.Floor((inner + rows * Math.Max(0, DefaultPitchY - CellHeight)) / PitchY);
        return Math.Clamp(n, 1, Math.Max(1, (int)Math.Floor(inner / MinRow)));
    }
}
