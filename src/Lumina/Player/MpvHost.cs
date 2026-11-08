using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lumina.Player;

/// <summary>Plain child HWND that mpv renders into (its wid). No WinForms involved.</summary>
public sealed class MpvHost : HwndHost
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);

    const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000;

    public IntPtr Hwnd { get; private set; }

    // ---- let the pointer through mpv's own window ----
    // mpv puts its picture in a window of its own inside ours. Mouse input already passes through it (it is disabled), but touch and pen input is aimed at
    // whatever window is under the finger, so a tap or drag on the video never reached Lumina. Answering "transparent" to the hit test hands it on to us.
    delegate IntPtr HitTestProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    const int GWLP_WNDPROC = -4;
    const uint WM_NCHITTEST = 0x0084;
    static readonly IntPtr HTTRANSPARENT = new(-1);

    readonly Dictionary<IntPtr, (IntPtr Previous, HitTestProc Keep)> _subclassed = new();

    /// <summary>Call after mpv has been given this window; mpv creates its own window a moment later, so this looks for a couple of seconds.</summary>
    public void PassInputThrough()
    {
        int tries = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            if (Hwnd == IntPtr.Zero) { timer.Stop(); return; }
            EnumChildWindows(Hwnd, (child, _) =>
            {
                if (!_subclassed.ContainsKey(child))
                {
                    IntPtr previous = IntPtr.Zero;
                    HitTestProc proc = (h, msg, w, l) => msg == WM_NCHITTEST ? HTTRANSPARENT : CallWindowProc(previous, h, msg, w, l);
                    previous = SetWindowLongPtr(child, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(proc));
                    _subclassed[child] = (previous, proc);
                }
                return true;
            }, IntPtr.Zero);
            if (_subclassed.Count > 0 || ++tries > 40) timer.Stop();
        };
        timer.Start();
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        // "static" is a stock window class, so no RegisterClass boilerplate.
        Hwnd = CreateWindowEx(0, "static", "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN,
            0, 0, 16, 16, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, Hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd) => DestroyWindow(hwnd.Handle);
}
