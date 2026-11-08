using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lumina.Player;

/// <summary>
/// mpv creates its embedded window disabled, so Windows never gives it the mouse. The cursor is really over our
/// surface, so WPF sees the events and this hands them to mpv: position (for its on-screen controls), buttons,
/// double click and wheel. It also hides an idle cursor in fullscreen, which mpv cannot do for a window it does not own.
/// </summary>
public sealed class MouseForwarder
{
    readonly FrameworkElement _surface;
    readonly Func<Mpv?> _mpv;
    readonly Func<bool> _fullscreen;
    readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(2) };
    bool _leftDown;

    public MouseForwarder(FrameworkElement surface, Func<Mpv?> mpv, Func<bool> fullscreen)
    {
        _surface = surface;
        _mpv = mpv;
        _fullscreen = fullscreen;
        _idle.Tick += (_, _) => { _idle.Stop(); _surface.Cursor = Cursors.None; };   // an idle pointer hides over the video, windowed or fullscreen

        surface.MouseMove += (_, e) =>
        {
            WakeCursor();
            var (x, y) = Pixels(e);
            Send("mouse", x.ToString(), y.ToString());
            // While the button is held the controller is told the position outright, so a drag on the seek bar or volume follows the pointer
            // whatever mpv's own pointer tracking does with a held button.
            if (_leftDown) Send("script-message", "lumina-drag", x.ToString(), y.ToString());
        };
        surface.MouseLeftButtonDown += OnLeftDown;
        surface.MouseLeftButtonUp += OnLeftUp;
        surface.MouseRightButtonDown += (_, e) => { WakeCursor(); Send("keydown", "MBTN_RIGHT"); };
        surface.MouseRightButtonUp += (_, e) => Send("keyup", "MBTN_RIGHT");
        surface.MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) Send("keydown", "MBTN_MID"); };
        surface.MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) Send("keyup", "MBTN_MID"); };
        surface.MouseWheel += (_, e) => { WakeCursor(); if (Wheel(e.Delta) is var (name, n)) for (int i = 0; i < n; i++) Send("keypress", name); };
    }

    /// <summary>Wheel delta to the mpv key and how many notches. Smooth-scroll mice send fractions of 120.</summary>
    public static (string Name, int Count)? Wheel(int delta) =>
        delta == 0 ? null : (delta > 0 ? "WHEEL_UP" : "WHEEL_DOWN", Math.Max(1, Math.Abs(delta) / 120));

    /// <summary>DIPs to the physical pixels mpv works in.</summary>
    public static int Px(double dips, double scale) => (int)Math.Round(dips * scale);

    (int X, int Y) Pixels(MouseEventArgs e)
    {
        var p = e.GetPosition(_surface);
        var dpi = VisualTreeHelper.GetDpi(_surface);
        return (Px(p.X, dpi.DpiScaleX), Px(p.Y, dpi.DpiScaleY));
    }

    /// <summary>
    /// A finger on the picture. WPF ignores the mouse messages Windows makes from touch and never gets the touch itself over a child window,
    /// so the main window hands them over here: position in physical pixels from the video's top-left corner.
    /// </summary>
    public void Touch(string phase, int x, int y)
    {
        WakeCursor();
        Send("mouse", x.ToString(), y.ToString());
        switch (phase)
        {
            case "down": _leftDown = true; Send("keydown", "MBTN_LEFT"); break;
            case "move": if (_leftDown) Send("script-message", "lumina-drag", x.ToString(), y.ToString()); break;
            case "up": if (_leftDown) { _leftDown = false; Send("keyup", "MBTN_LEFT"); } break;
        }
    }

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        WakeCursor();
        var (x, y) = Pixels(e);
        Send("mouse", x.ToString(), y.ToString());
        if (e.ClickCount == 2) { Send("mouse", x.ToString(), y.ToString(), "0", "double"); return; }
        _leftDown = true;
        _surface.CaptureMouse(); // so the release arrives even if the drag (a seek bar) leaves the surface
        Send("keydown", "MBTN_LEFT");
    }

    void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (!_leftDown) return; // the second half of a double click has no matching down
        _leftDown = false;
        _surface.ReleaseMouseCapture();
        Send("keyup", "MBTN_LEFT");
    }

    void WakeCursor()
    {
        _surface.Cursor = null;
        _idle.Stop();
        _idle.Start();
    }

    void Send(params string[] args)
    {
        try { _mpv()?.CommandAsync(args); }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { } // player closing
    }
}
