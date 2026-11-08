using System.Windows;
using System.Windows.Controls;

namespace Lumina;

/// <summary>
/// A wrapping panel for fixed-size cards that spreads each full row across the whole width, so the grid has no ragged gap on its right edge.
/// The last row keeps its natural spacing and sits at the left.
/// </summary>
public sealed class JustifiedWrapPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double rowW = 0, rowH = 0, width = 0, height = 0;
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var s = c.DesiredSize;
            if (rowW > 0 && rowW + s.Width > available.Width) { width = Math.Max(width, rowW); height += rowH; rowW = rowH = 0; }
            rowW += s.Width; rowH = Math.Max(rowH, s.Height);
        }
        width = Math.Max(width, rowW); height += rowH;
        return new Size(double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = new List<List<UIElement>>();
        var row = new List<UIElement>();
        double rowW = 0;
        foreach (UIElement c in InternalChildren)
        {
            var s = c.DesiredSize;
            if (row.Count > 0 && rowW + s.Width > finalSize.Width) { rows.Add(row); row = new(); rowW = 0; }
            row.Add(c); rowW += s.Width;
        }
        if (row.Count > 0) rows.Add(row);

        double y = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            var items = rows[r];
            double used = items.Sum(i => i.DesiredSize.Width);
            double gap = r < rows.Count - 1 && items.Count > 1 ? Math.Max(0, (finalSize.Width - used) / (items.Count - 1)) : 0;
            double x = 0, h = items.Max(i => i.DesiredSize.Height);
            foreach (var i in items)
            {
                i.Arrange(new Rect(x, y, i.DesiredSize.Width, h));
                x += i.DesiredSize.Width + gap;
            }
            y += h;
        }
        return finalSize;
    }
}
