using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lumina.Windows;

/// <summary>
/// An auto-hiding taskbar normally slides up when the pointer touches the screen edge. It does not when a window covers the whole screen
/// (fullscreen video, or a maximized window with its own title bar), so this watches the pointer: resting at the taskbar's edge for a moment hands the
/// taskbar the focus, which brings it up; moving well away hands the focus back to the window.
/// </summary>
public sealed class TaskbarReveal(Window window)
{
    [StructLayout(LayoutKind.Sequential)] struct Rect32 { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Point32 { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct AppBarData { public uint Size; public IntPtr Hwnd; public uint Callback; public uint Edge; public Rect32 Rc; public IntPtr Param; }

    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point32 p);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect32 r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    /// <summary>Windows refuses to hand the focus to a program that is not in front (the taskbar is, while it is showing). Joining the front window's input briefly gets around that.</summary>
    static void TakeFocusBack(IntPtr hwnd)
    {
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _), me = GetCurrentThreadId();
        bool attached = fgThread != me && AttachThreadInput(me, fgThread, true);
        try { BringWindowToTop(hwnd); SetForegroundWindow(hwnd); }
        finally { if (attached) AttachThreadInput(me, fgThread, false); }
    }

    /// <summary>The edge an auto-hiding taskbar sits on (0 left, 1 top, 2 right, 3 bottom), or -1 when it does not hide itself.</summary>
    static int AutoHideEdge()
    {
        try
        {
            var d = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
            if (((uint)SHAppBarMessage(4, ref d) & 1) == 0 || SHAppBarMessage(5, ref d) == UIntPtr.Zero) return -1;   // ABM_GETSTATE auto-hide bit, ABM_GETTASKBARPOS
            return (int)d.Edge;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return -1; }
    }

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    int _edge = -1, _atEdge, _away;
    bool _revealed, _hooked;

    public void Start()
    {
        _edge = AutoHideEdge(); _revealed = false; _atEdge = _away = 0;
        if (!_hooked) { _timer.Tick += Watch; _hooked = true; }
        _timer.Start();
    }

    public void Stop() { _timer.Stop(); _revealed = false; }

    void Watch(object? s, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (_edge < 0 || hwnd == IntPtr.Zero || !GetCursorPos(out var p)) return;
        var m = Lumina.Player.Fullscreen.MonitorBounds(hwnd);
        bool inside = p.X >= m.X && p.X < m.X + m.W && p.Y >= m.Y && p.Y < m.Y + m.H;
        bool at = inside && _edge switch { 0 => p.X <= m.X, 1 => p.Y <= m.Y, 2 => p.X >= m.X + m.W - 1, _ => p.Y >= m.Y + m.H - 1 };
        var tray = FindWindow("Shell_TrayWnd", null);
        if (!_revealed)
        {
            // only while this window is the one in front: another program's window should keep its own behaviour
            _atEdge = at && window.IsActive ? _atEdge + 1 : 0;
            if (_atEdge >= 2 && tray != IntPtr.Zero) { _revealed = true; _atEdge = _away = 0; SetForegroundWindow(tray); }
            return;
        }
        bool near = tray != IntPtr.Zero && GetWindowRect(tray, out var tr)
            && p.X >= tr.Left - 30 && p.X <= tr.Right + 30 && p.Y >= tr.Top - 30 && p.Y <= tr.Bottom + 30;
        _away = near ? 0 : _away + 1;
        if (_away >= 8) { _revealed = false; _away = 0; TakeFocusBack(hwnd); window.Activate(); }
    }
}
