namespace Lumina.Player;

/// <summary>Where the miniplayer window goes and how big it is. Pure: units are whatever the caller uses (device-independent pixels in the app).</summary>
public static class MiniGeometry
{
    public const double Width = 480, Margin = 24, MinAspect = 0.6, MaxAspect = 2.6;

    public readonly record struct Box(double Left, double Top, double Width, double Height);

    /// <summary>
    /// A window whose video area is <see cref="Width"/> wide and as tall as the picture needs, plus the window's own frame (<paramref name="chromeHeight"/>),
    /// in the bottom-right corner of the work area. A strange or unknown aspect ratio is held to a sensible range, and the window never leaves the work area.
    /// </summary>
    public static Box For(Box workArea, double aspect, double chromeHeight, double width = Width)
    {
        if (double.IsNaN(aspect) || double.IsInfinity(aspect) || aspect <= 0) aspect = 16.0 / 9;
        aspect = Math.Clamp(aspect, MinAspect, MaxAspect);
        chromeHeight = Math.Max(0, chromeHeight);
        var w = Math.Min(width, Math.Max(200, workArea.Width - 2 * Margin));
        var h = Math.Min(w / aspect + chromeHeight, Math.Max(150, workArea.Height - 2 * Margin));
        var left = workArea.Left + workArea.Width - w - Margin;
        var top = workArea.Top + workArea.Height - h - Margin;
        return new Box(Math.Max(workArea.Left, left), Math.Max(workArea.Top, top), w, h);
    }
}
