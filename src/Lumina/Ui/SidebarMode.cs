using System.Windows;

namespace Lumina;

/// <summary>Set on the sidebar; every nav item inside reads it (it inherits) to show icons only.</summary>
public static class SidebarMode
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(SidebarMode), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsCompact(DependencyObject d) => (bool)d.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject d, bool v) => d.SetValue(IsCompactProperty, v);
}
