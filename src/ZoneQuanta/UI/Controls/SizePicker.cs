using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ZoneQuanta.UI.Controls;

// Columns × rows picker: hover over the squares to span a block from the top-left corner, click to pick.
// The block of the current size stays lit; the caption reads "4 列 × 2 行".
public sealed class SizePicker : StackPanel
{
    private const double Square = 16, Space = 4;
    private readonly Border[,] _squares;
    private readonly TextBlock _caption = new() { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly int _maxCols, _maxRows;
    private readonly int _cols, _rows;

    public SizePicker(int maxCols, int maxRows, int cols, int rows, string emptyCaption)
    {
        _maxCols = maxCols;
        _maxRows = maxRows;
        _cols = cols;
        _rows = rows;
        EmptyCaption = emptyCaption;
        Margin = new Thickness(6, 4, 6, 2);
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;

        var grid = new Canvas { Width = maxCols * (Square + Space) - Space, Height = maxRows * (Square + Space) - Space, Background = Brushes.Transparent };
        _squares = new Border[maxCols, maxRows];
        for (int x = 0; x < maxCols; x++)
            for (int y = 0; y < maxRows; y++)
            {
                var b = new Border { Width = Square, Height = Square, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
                Canvas.SetLeft(b, x * (Square + Space));
                Canvas.SetTop(b, y * (Square + Space));
                grid.Children.Add(b);
                _squares[x, y] = b;
            }
        _caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Children.Add(grid);
        Children.Add(_caption);

        grid.MouseMove += (_, e) => Show(At(e.GetPosition(grid)));
        MouseLeave += (_, _) => Show((_cols, _rows));
        grid.MouseLeftButtonUp += (_, e) =>
        {
            var (c, r) = At(e.GetPosition(grid));
            e.Handled = true;
            Picked?.Invoke(c, r);
        };
        Show((cols, rows));
    }

    public event Action<int, int>? Picked;

    private string EmptyCaption { get; }

    private (int Cols, int Rows) At(Point p) => (
        Math.Clamp((int)(p.X / (Square + Space)) + 1, 1, _maxCols),
        Math.Clamp((int)(p.Y / (Square + Space)) + 1, 1, _maxRows));

    private void Show((int Cols, int Rows) size)
    {
        for (int x = 0; x < _maxCols; x++)
            for (int y = 0; y < _maxRows; y++)
            {
                var b = _squares[x, y];
                bool lit = x < size.Cols && y < size.Rows;
                b.SetResourceReference(Border.BackgroundProperty, lit ? "Accent1Brush" : "RaisedBrush");
                b.SetResourceReference(Border.BorderBrushProperty, lit ? "Accent1Brush" : "LineBrush");
                b.Opacity = lit ? 0.85 : 1;
            }
        _caption.Text = size.Cols > 0 && size.Rows > 0 ? $"{size.Cols} 列 × {size.Rows} 行" : EmptyCaption;
    }
}
