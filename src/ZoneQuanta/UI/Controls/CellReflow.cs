using System;
using System.Collections.Generic;
using System.Linq;

namespace ZoneQuanta.UI.Controls;

// A block of grid cells: top-left cell and its width × height in cells.
public readonly record struct CellBox(int Col, int Row, int C, int R)
{
    public int Right => Col + C;
    public int Bottom => Row + R;
    public bool Overlaps(CellBox o) => Col < o.Right && o.Col < Right && Row < o.Bottom && o.Row < Bottom;
    public bool Inside(CellBox o) => Col >= o.Col && Row >= o.Row && Right <= o.Right && Bottom <= o.Bottom;
}

// Making room on a cell grid for one box being dragged: only the boxes it overlaps move. Each goes into the cells the
// dragged box left when it fits there (keeping its offset to the drop, so two cards swapped stay side by side),
// otherwise to the nearest free spot (|Δcol| + |Δrow|, then the higher row, then the nearer column). Pure arithmetic.
public static class CellReflow
{
    public static CellBox[] MakeRoom(int cols, IReadOnlyList<CellBox> others, CellBox target, CellBox? from)
    {
        cols = Math.Max(1, cols);
        var result = others.ToArray();
        var grid = new Occupancy(cols);
        var moving = new List<int>();
        for (int i = 0; i < result.Length; i++)
        {
            if (result[i].Overlaps(target)) moving.Add(i);
            else grid.Mark(result[i]);
        }
        grid.Mark(target);
        if (moving.Count == 0) return result;
        int bottom = Math.Max(target.Bottom, result.Length == 0 ? 0 : result.Max(b => b.Bottom));
        foreach (int i in moving.OrderBy(i => result[i].Row).ThenBy(i => result[i].Col))
        {
            var b = result[i];
            int c = Math.Min(b.C, cols);
            CellBox? spot = null;
            if (from is { } f)
            {
                var mirror = new CellBox(Math.Clamp(f.Col + b.Col - target.Col, 0, cols - c), Math.Max(0, f.Row + b.Row - target.Row), c, b.R);
                spot = mirror.Inside(f) && grid.Free(mirror) ? mirror : Nearest(grid, cols, mirror, f.Row, f.Bottom - b.R, f.Col, f.Right - c);
            }
            spot ??= Nearest(grid, cols, b with { C = c }, 0, bottom + b.R, 0, cols - c);
            var s = spot ?? new CellBox(0, bottom, c, b.R);
            grid.Mark(s);
            bottom = Math.Max(bottom, s.Bottom);
            result[i] = s;
        }
        return result;
    }

    // Free spot for a box of b's size closest to b, top-left within the given rows / columns.
    private static CellBox? Nearest(Occupancy grid, int cols, CellBox b, int row0, int row1, int col0, int col1)
    {
        CellBox? best = null;
        long bestCost = long.MaxValue;
        col1 = Math.Min(col1, cols - b.C);
        for (int row = Math.Max(0, row0); row <= row1; row++)
            for (int col = Math.Max(0, col0); col <= col1; col++)
            {
                long cost = (Math.Abs(col - b.Col) + Math.Abs(row - b.Row)) * 1_000_000L + row * 1_000L + Math.Abs(col - b.Col);
                if (cost >= bestCost) continue;
                var s = new CellBox(col, row, b.C, b.R);
                if (!grid.Free(s)) continue;
                best = s;
                bestCost = cost;
            }
        return best;
    }

    private sealed class Occupancy
    {
        private readonly int _cols;
        private readonly List<bool[]> _rows = new();

        public Occupancy(int cols) => _cols = cols;

        public void Mark(CellBox b)
        {
            for (int y = Math.Max(0, b.Row); y < b.Bottom; y++)
            {
                while (_rows.Count <= y) _rows.Add(new bool[_cols]);
                for (int x = Math.Max(0, b.Col); x < Math.Min(_cols, b.Right); x++) _rows[y][x] = true;
            }
        }

        public bool Free(CellBox b)
        {
            if (b.Col < 0 || b.Row < 0 || b.Right > _cols) return false;
            for (int y = b.Row; y < b.Bottom && y < _rows.Count; y++)
                for (int x = b.Col; x < b.Right; x++)
                    if (_rows[y][x]) return false;
            return true;
        }
    }
}
