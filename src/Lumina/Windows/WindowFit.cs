using Lumina.Player;

namespace Lumina.Windows;

/// <summary>
/// Keeps the main window inside the screen's usable area (issue #34): on a 1366x768 screen the default 1366x820 window
/// was taller than the screen, so its title bar opened out of reach. Pure: units are whatever the caller uses.
/// </summary>
public static class WindowFit
{
    /// <summary>The window shrunk to fit <paramref name="work"/>, then centred in it or moved back inside it.</summary>
    public static MiniGeometry.Box Fit(MiniGeometry.Box want, MiniGeometry.Box work, bool center)
    {
        var w = Math.Min(want.Width, work.Width);
        var h = Math.Min(want.Height, work.Height);
        var left = center ? work.Left + (work.Width - w) / 2 : Math.Clamp(want.Left, work.Left, work.Left + work.Width - w);
        var top = center ? work.Top + (work.Height - h) / 2 : Math.Clamp(want.Top, work.Top, work.Top + work.Height - h);
        return new MiniGeometry.Box(left, top, w, h);
    }
}
