using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Lumina.Windows;

/// <summary>
/// An icon in the notification area: click it to open Lumina, right-click for Open and Quit, and it can show a balloon message.
/// Plain Windows calls, so no extra library is needed. It lives on a message-only window of its own.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const int WmApp = 0x8000, WmTray = WmApp + 1, WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmContext = 0x007B;
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifInfo = 0x10;
    const int IdOpen = 1, IdQuit = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public int cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    readonly HwndSource _window;
    readonly IntPtr _icon;
    readonly string _tip;
    bool _added;

    public event Action? OpenRequested;
    public event Action? QuitRequested;

    public TrayIcon(string tooltip)
    {
        _tip = tooltip.Length > 120 ? tooltip[..120] : tooltip;
        _icon = Lumina.Ui.BrandIcons.Tray();
        _window = new HwndSource(new HwndSourceParameters("LuminaTray") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });   // HWND_MESSAGE: no visible window
        _window.AddHook(WndProc);
    }

    NotifyIconData Data(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(), hWnd = _window.Handle, uID = 1, uFlags = flags, uCallbackMessage = WmTray, hIcon = _icon,
        szTip = _tip, szInfo = "", szInfoTitle = "",
    };

    public void Show()
    {
        if (_added) return;
        var d = Data(NifMessage | NifIcon | NifTip);
        _added = Shell_NotifyIcon(NimAdd, ref d);
        if (_added) { d.uVersion = 4; Shell_NotifyIcon(NimSetVersion, ref d); }
    }

    public void Hide()
    {
        if (!_added) return;
        var d = Data(0);
        Shell_NotifyIcon(NimDelete, ref d);
        _added = false;
    }

    /// <summary>A small message from the icon (the system shows it as a notification).</summary>
    public void Balloon(string title, string text)
    {
        if (!_added) return;
        var d = Data(NifInfo);
        d.szInfoTitle = title.Length > 60 ? title[..60] : title;
        d.szInfo = text.Length > 250 ? text[..250] : text;
        d.dwInfoFlags = 1;      // NIIF_INFO
        Shell_NotifyIcon(NimModify, ref d);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmTray) return IntPtr.Zero;
        var ev = (int)(lParam.ToInt64() & 0xFFFF);
        if (ev == WmLButtonUp) { handled = true; OpenRequested?.Invoke(); }
        else if (ev == WmRButtonUp || ev == WmContext) { handled = true; ShowMenu(); }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, (UIntPtr)IdOpen, "Open Lumina");
        AppendMenu(menu, 0, (UIntPtr)IdQuit, "Quit");
        GetCursorPos(out var p);
        SetForegroundWindow(_window.Handle);              // the menu closes when you click elsewhere only if its owner is in front
        var choice = TrackPopupMenuEx(menu, 0x0100 | 0x0002, p.X, p.Y, _window.Handle, IntPtr.Zero);   // TPM_RETURNCMD | TPM_RIGHTBUTTON
        PostMessage(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        if (choice == IdOpen) OpenRequested?.Invoke();
        else if (choice == IdQuit) QuitRequested?.Invoke();
    }

    public void Dispose()
    {
        Hide();
        _window.Dispose();
    }
}
