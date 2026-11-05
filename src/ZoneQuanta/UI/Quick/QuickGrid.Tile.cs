using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Controls;
using Shapes = System.Windows.Shapes;

namespace ZoneQuanta.UI.Quick;

public sealed partial class QuickGrid
{
    // One quick item. Icons and file details load off the UI thread and are cached.
    private sealed class Tile : Border
    {
        private readonly AppSettings _s;
        private readonly Grid _icon = new();
        private readonly TextBlock _name = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly StackPanel _text = new();
        private readonly StackPanel _details = new();
        private readonly ScaleTransform _scale = new(1, 1);
        private string _view = "";
        private int _iconPx;
        private (DateTime At, long Size, DateTime Modified, bool Exists) _meta;
        private bool _metaLoading;
        private TextBox? _editor;

        public Tile(QuickItem item, AppSettings s)
        {
            Item = item;
            _s = s;
            Background = Brushes.Transparent;
            CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            BorderBrush = Brushes.Transparent;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = _scale;
            _text.Children.Add(_name);
            _text.Children.Add(_details);
        }

        public QuickItem Item { get; }
        public bool Renaming => _editor is not null;

        // Board tiles are not inside a ListBoxItem, so they draw their own hover and focus state.
        public void EnableHover()
        {
            Focusable = true;
            FocusVisualStyle = null;
            Cursor = Cursors.Hand;
            MouseEnter += (_, _) => UpdateState();
            MouseLeave += (_, _) => UpdateState();
            GotKeyboardFocus += (_, _) => UpdateState();
            LostKeyboardFocus += (_, _) => UpdateState();
            _hover = true;
        }

        private bool _hover, _drop;

        private void UpdateState()
        {
            if (!_hover) return;
            if (IsMouseOver || IsKeyboardFocused) SetResourceReference(BackgroundProperty, "RaisedBrush");
            else Background = Brushes.Transparent;
            if (_drop || IsKeyboardFocused) SetResourceReference(BorderBrushProperty, "Accent1Brush");
            else BorderBrush = Brushes.Transparent;
        }

        public void Layout(string view)
        {
            bool showIcon = _s.QuickShowIcon || !_s.QuickShowName;
            _view = view;
            Child = null;
            if (_icon.Parent is System.Windows.Controls.Panel p1) p1.Children.Remove(_icon);
            if (_text.Parent is System.Windows.Controls.Panel p2) p2.Children.Remove(_text);

            if (view == "List")
            {
                Width = double.NaN;
                Padding = new Thickness(10, 7, 10, 7);
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _icon.Width = _icon.Height = 24;
                _icon.Margin = new Thickness(0, 0, 12, 0);
                _icon.VerticalAlignment = VerticalAlignment.Center;
                _name.TextAlignment = TextAlignment.Left;
                _name.TextWrapping = TextWrapping.NoWrap;
                _name.MaxHeight = double.PositiveInfinity;
                _details.Orientation = Orientation.Horizontal;
                _details.HorizontalAlignment = HorizontalAlignment.Left;
                _text.VerticalAlignment = VerticalAlignment.Center;
                if (showIcon) g.Children.Add(_icon);
                Grid.SetColumn(_text, 1);
                g.Children.Add(_text);
                Child = g;
                SetIconSize(24);
            }
            else if (view == "Mini")
            {
                // A one-column card: smaller icons only, the name stays in the tooltip.
                Width = MiniCell;
                Padding = new Thickness(4);
                _icon.Width = _icon.Height = MiniCell - 8;
                _icon.Margin = new Thickness(0);
                _icon.HorizontalAlignment = HorizontalAlignment.Center;
                Child = _icon;
                SetIconSize(MiniCell - 8);
            }
            else if (view == "Card")
            {
                // Inside a group card: icon first, short name, no details (they stay in grid and list).
                Width = _s.QuickShowName ? 80 : 64;
                Padding = new Thickness(4, 8, 4, 6);
                var st = new StackPanel();
                _icon.Width = _icon.Height = 42;
                _icon.Margin = new Thickness(0, 0, 0, _s.QuickShowName ? 6 : 0);
                _icon.HorizontalAlignment = HorizontalAlignment.Center;
                _name.TextAlignment = TextAlignment.Center;
                _name.TextWrapping = TextWrapping.NoWrap;
                _name.MaxHeight = double.PositiveInfinity;
                _details.HorizontalAlignment = HorizontalAlignment.Center;
                if (showIcon) st.Children.Add(_icon);
                st.Children.Add(_text);
                Child = st;
                SetIconSize(42);
            }
            else
            {
                Width = 124;
                Padding = new Thickness(6, 12, 6, 10);
                var st = new StackPanel();
                _icon.Width = _icon.Height = 44;
                _icon.Margin = new Thickness(0, 0, 0, 8);
                _icon.HorizontalAlignment = HorizontalAlignment.Center;
                _name.TextAlignment = TextAlignment.Center;
                _name.TextWrapping = TextWrapping.Wrap;
                _name.MaxHeight = 36;
                _details.Orientation = Orientation.Vertical;
                _details.HorizontalAlignment = HorizontalAlignment.Center;
                if (showIcon) st.Children.Add(_icon);
                st.Children.Add(_text);
                Child = st;
                SetIconSize(44);
            }
            _name.FontSize = view == "Card" ? 11.5 : 13;
            _name.Visibility = view != "Mini" && (_s.QuickShowName || !showIcon) ? Visibility.Visible : Visibility.Collapsed;
            // Re-parented tiles can keep a stale hover look until the mouse moves over them again.
            if (_hover && !IsKeyboardFocused) Background = Brushes.Transparent;
            Refresh();
        }

        public void Refresh()
        {
            if (_editor is null) _name.Text = Item.DisplayName;
            ToolTip = _view is "Card" or "Mini" ? Item.DisplayName + Environment.NewLine + Item.Path : Item.Path;
            BuildDetails();
            RefreshMeta(false);
        }

        private void SetIconSize(double dip)
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int px = (int)Math.Round(dip * Math.Max(1, scale));
            if (px == _iconPx && _icon.Children.Count > 0) return;
            _iconPx = px;
            _icon.Children.Clear();
            if (ShellIcons.Cached(Item.Path, px) is { } cached)
            {
                _icon.Children.Add(MakeImage(cached));
                return;
            }
            var ph = new Shapes.Path
            {
                Data = (Geometry)Application.Current.FindResource(Directory.Exists(Item.Path) ? "Icon.Folder" : "Icon.File"),
                Width = dip * 0.7, Height = dip * 0.7,
            };
            ph.SetResourceReference(StyleProperty, "IconPath");
            ph.SetResourceReference(Shapes.Shape.StrokeProperty, "MutedBrush");
            _icon.Children.Add(ph);
            ShellIcons.Request(Item.Path, px, bmp =>
            {
                if (bmp is null) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_iconPx != px) return;
                    _icon.Children.Clear();
                    var img = MakeImage(bmp);
                    img.Opacity = 0;
                    _icon.Children.Add(img);
                    img.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                }));
            });
        }

        private static Image MakeImage(BitmapSource src)
        {
            var img = new Image { Source = src, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        public void RefreshMeta(bool force)
        {
            if (_metaLoading || (!force && (DateTime.UtcNow - _meta.At).TotalSeconds < 20)) return;
            _metaLoading = true;
            string path = Item.Path;
            Task.Run(() =>
            {
                long size = -1;
                DateTime mod = default;
                bool exists = PathEngine.IsShell(path);
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists) { size = fi.Length; mod = fi.LastWriteTime; exists = true; }
                    else if (Directory.Exists(path)) { mod = Directory.GetLastWriteTime(path); exists = true; }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
                return (DateTime.UtcNow, size, mod, exists);
            }).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                _metaLoading = false;
                _meta = t.IsCompletedSuccessfully ? t.Result : (DateTime.UtcNow, -1L, default, false);
                Opacity = _meta.Exists ? 1 : 0.45;
                BuildDetails();
            })), TaskScheduler.Default);
        }

        private void BuildDetails()
        {
            _details.Children.Clear();
            if (_view is "Card" or "Mini")
            {
                _details.Visibility = Visibility.Collapsed;
                return;
            }
            var parts = new List<string>();
            if (_s.QuickShowType) parts.Add(QuickActions.TypeName(Item.Path));
            if (_s.QuickShowSize && _meta.Size >= 0 && _meta.At != default) parts.Add(UnitFormat.Size(_meta.Size, false, "Auto"));
            if (_s.QuickShowModified && _meta.Modified != default) parts.Add(_meta.Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (_s.QuickShowCount) parts.Add($"{Item.OpenCount} 次");
            if (_s.QuickShowPath) parts.Insert(0, PathEngine.Parent(Item.Path) is { Length: > 0 } parent ? parent : Item.Path);
            foreach (string p in parts)
            {
                var t = new TextBlock
                {
                    Text = p,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = _view == "List" ? new Thickness(0, 2, 14, 0) : new Thickness(0, 2, 0, 0),
                    TextAlignment = _view == "List" ? TextAlignment.Left : TextAlignment.Center,
                    MaxWidth = _view == "List" ? 520 : 112,
                };
                t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                _details.Children.Add(t);
            }
            _details.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetDropTarget(bool on)
        {
            _drop = on;
            if (on) SetResourceReference(BorderBrushProperty, "Accent1Brush");
            else BorderBrush = Brushes.Transparent;
            UpdateState();
        }

        // New items pop in so the user sees where they landed.
        public void Pop()
        {
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
            var a = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            SetResourceReference(BorderBrushProperty, "Accent1Brush");
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            t.Tick += (_, _) => { t.Stop(); BorderBrush = Brushes.Transparent; UpdateState(); };
            t.Start();
        }

        public void BeginRename(Action<string> commit)
        {
            if (_editor is not null) return;
            var box = new TextBox { Text = Item.DisplayName, MinWidth = 80 };
            box.SetResourceReference(StyleProperty, "Field");
            _editor = box;
            // A mini tile shows no name, so its editor opens just below it.
            System.Windows.Controls.Primitives.Popup? popup = null;
            if (_view == "Mini")
            {
                box.MinWidth = 160;
                popup = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = this,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                    AllowsTransparency = true,
                    StaysOpen = true,
                    Child = box,
                    IsOpen = true,
                };
            }
            else
            {
                int index = _text.Children.IndexOf(_name);
                _text.Children.Remove(_name);
                _text.Children.Insert(Math.Max(0, index), box);
            }
            bool done = false;
            void Finish(bool save)
            {
                if (done) return;
                done = true;
                string text = box.Text;
                if (popup is not null)
                {
                    popup.IsOpen = false;
                    popup.Child = null;
                }
                else
                {
                    _text.Children.Remove(box);
                    _text.Children.Insert(0, _name);
                }
                _editor = null;
                if (save && text.Trim().Length > 0) commit(text);
                else Refresh();
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            };
            box.LostKeyboardFocus += (_, _) => Finish(true);
            Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
}
