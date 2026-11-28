using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lumina;

/// <summary>Decides whether a click in a page header is on empty space (drag the window) or on a control (leave it alone).</summary>
public static class DragZone
{
    /// <summary>How far down from the top of the pages the header counts as a drag area.</summary>
    public const double HeaderHeight = 72;

    public static bool IsEmptyArea(DependencyObject? source)
    {
        for (var d = source; d != null; d = (d is Visual ? VisualTreeHelper.GetParent(d) : null) ?? LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or TextBoxBase or ComboBox or Slider or ScrollBar or Thumb or Selector or MenuBase) return false;
        return true;
    }
}
