using System.Windows;

namespace ZoneQuanta.UI.Controls;

// Set on the sidebar: nav items below it show their icon only.
public static class SideNav
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached(
        "Compact", typeof(bool), typeof(SideNav), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetCompact(DependencyObject d) => (bool)d.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject d, bool v) => d.SetValue(CompactProperty, v);
}
