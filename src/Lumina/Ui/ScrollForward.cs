using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Lumina;

/// <summary>
/// A list nested in a scrolling page swallows the mouse wheel, so the page stops scrolling whenever the cursor is over a shelf.
/// With Enabled=True the wheel goes to the page instead, and Shift+wheel scrolls the list sideways.
/// </summary>
public static class ScrollForward
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ScrollForward), new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.PreviewMouseWheel -= OnWheel;
        if (e.NewValue is true) el.PreviewMouseWheel += OnWheel;
    }

    static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement el) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && FindScroller(el) is { } sv)
        {
            SmoothScroll.By(sv, -e.Delta, true);
            e.Handled = true;
            return;
        }

        e.Handled = true;
        for (DependencyObject? d = VisualTreeHelper.GetParent(el); d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer page) { SmoothScroll.By(page, -e.Delta); return; }
    }

    public static ScrollViewer? FindScroller(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroller(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
}
