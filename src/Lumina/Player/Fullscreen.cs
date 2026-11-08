using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lumina.Player;

/// <summary>
/// Borderless fullscreen over the whole monitor, taskbar included, and back to exactly the previous placement.
/// mpv's own fullscreen cannot be used: its window is a child of ours, so it would only fill our window.
/// </summary>
public sealed class Fullscreen(Window window, UIElement chrome)
{
    [StructLayout(LayoutKind.Sequential)]
    struct Rect32 { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo { public int Size; public Rect32 Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Windows 11 rounds the corners and draws a 1 px border on any top-level window; fullscreen must have neither (round = 1 off, 0 default; border = none or default).</summary>
    static void Frame(IntPtr hwnd, bool fullscreen)
    {
        int round = fullscreen ? 1 : 0, border = fullscreen ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AppBarData { public uint Size; public IntPtr Hwnd; public uint Callback; public uint Edge; public Rect32 Rc; public IntPtr Param; }

    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    /// <summary>
    /// When the taskbar hides itself, a window that covers every pixel of the screen stops it appearing when the pointer touches that edge.
    /// Leaving a single pixel uncovered on the taskbar's side restores the hover (the same trick other players use). Nothing is left out otherwise.
    /// </summary>
    public static (int X, int Y, int W, int H) LeaveTaskbarEdge((int X, int Y, int W, int H) b)
    {
        try
        {
            var d = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
            const uint ABM_GETSTATE = 4, ABM_GETTASKBARPOS = 5, ABS_AUTOHIDE = 1;
            if (((uint)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) == 0) return b;
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref d) == UIntPtr.Zero) return b;
            return d.Edge switch
            {
                0 => (b.X + 1, b.Y, b.W - 1, b.H),   // taskbar at the left
                1 => (b.X, b.Y + 1, b.W, b.H - 1),   // top
                2 => (b.X, b.Y, b.W - 1, b.H),       // right
                _ => (b.X, b.Y, b.W, b.H - 1),       // bottom
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return b; }
    }

    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    static readonly IntPtr HWND_TOPMOST = new(-1);

    WindowState _state;
    WindowStyle _style;
    ResizeMode _resize;
    bool _topmost;
    Rect _bounds;

    readonly Lumina.Windows.TaskbarReveal _reveal = new(window);

    public bool On { get; private set; }

    // Fullscreen sits above the taskbar by being topmost; while another window is chosen (Alt+Tab, a click elsewhere) it must stop being topmost,
    // or the window that was switched to opens underneath it and the switch seems to do nothing.
    void OnDeactivated(object? s, EventArgs e) { if (On) window.Topmost = _topmost; }
    void OnActivated(object? s, EventArgs e) { if (On) window.Topmost = true; }

    /// <summary>Full pixel bounds (not the work area) of the monitor the window is mostly on.</summary>
    public static (int X, int Y, int W, int H) MonitorBounds(IntPtr hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info);
        var m = info.Monitor;
        return (m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);
    }

    public void Toggle()
    {
        if (On) Exit(); else Enter();
    }

    public void Enter()
    {
        if (On) return;
        _state = window.WindowState;
        _style = window.WindowStyle;
        _resize = window.ResizeMode;
        _topmost = window.Topmost;
        _bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;

        // Normal first: a maximized window would keep clamping itself to the work area.
        window.WindowState = WindowState.Normal;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.Topmost = true;
        window.Deactivated += OnDeactivated;
        window.Activated += OnActivated;
        chrome.Visibility = Visibility.Collapsed;

        var hwnd = new WindowInteropHelper(window).Handle;
        var (x, y, w, h) = LeaveTaskbarEdge(MonitorBounds(hwnd));
        _reveal.Start();
        Frame(hwnd, true);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        On = true;
    }

    public void Exit()
    {
        if (!On) return;
        On = false;
        _reveal.Stop();
        window.Deactivated -= OnDeactivated;
        window.Activated -= OnActivated;
        chrome.Visibility = Visibility.Visible;
        Frame(new WindowInteropHelper(window).Handle, false);
        window.Topmost = _topmost;
        window.ResizeMode = _resize;
        window.WindowStyle = _style;
        window.WindowState = WindowState.Normal;
        // Put the normal bounds back before maximizing, or "restore" would later mean fullscreen-sized.
        window.Left = _bounds.Left;
        window.Top = _bounds.Top;
        window.Width = _bounds.Width;
        window.Height = _bounds.Height;
        if (_state == WindowState.Maximized) window.WindowState = WindowState.Maximized;
    }
}
