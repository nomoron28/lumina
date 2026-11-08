using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class FullscreenTests
{
    static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("STA body failed: " + error.Message, error);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    struct RECT { public int Left, Top, Right, Bottom; }

    static (int X, int Y, int W, int H) PixelRect(Window w)
    {
        GetWindowRect(new WindowInteropHelper(w).Handle, out var r);
        return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    // Windows snaps to whole physical pixels, so DIP values can shift by under one pixel at fractional scaling.
    static void Near(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= 1.0, $"{what}: expected {expected}, got {actual}");

    static void SameBounds(Window w, double l, double t, double wd, double h)
    {
        Near(l, w.Left, "Left"); Near(t, w.Top, "Top"); Near(wd, w.Width, "Width"); Near(h, w.Height, "Height");
    }

    [Fact]
    public void CoversTheWholeMonitorAndRestoresExactly()
    {
        OnSta(() =>
        {
            var chrome = new Border();
            var w = new Window { Left = 120, Top = 90, Width = 700, Height = 450, Content = new Grid { Children = { chrome } } };
            w.Show();
            var fs = new Fullscreen(w, chrome);
            var hwnd = new WindowInteropHelper(w).Handle;

            fs.Toggle();
            Assert.True(fs.On);
            Assert.Equal(Fullscreen.LeaveTaskbarEdge(Fullscreen.MonitorBounds(hwnd)), PixelRect(w)); // the monitor itself, not its work area
            Assert.True(w.Topmost);
            Assert.Equal(WindowStyle.None, w.WindowStyle);
            Assert.Equal(Visibility.Collapsed, chrome.Visibility);

            fs.Toggle();
            Assert.False(fs.On);
            SameBounds(w, 120, 90, 700, 450);
            Assert.False(w.Topmost);
            Assert.Equal(WindowStyle.SingleBorderWindow, w.WindowStyle);
            Assert.Equal(ResizeMode.CanResize, w.ResizeMode);
            Assert.Equal(Visibility.Visible, chrome.Visibility);
            Assert.Equal(WindowState.Normal, w.WindowState);
            w.Close();
        });
    }

    [Fact]
    public void FromMaximizedComesBackMaximizedWithTheOldNormalSize()
    {
        OnSta(() =>
        {
            var chrome = new Border();
            var w = new Window { Left = 150, Top = 100, Width = 640, Height = 400, Content = new Grid { Children = { chrome } } };
            w.Show();
            w.WindowState = WindowState.Maximized;
            var normal = w.RestoreBounds;
            var fs = new Fullscreen(w, chrome);

            fs.Enter();
            fs.Exit();

            Assert.Equal(WindowState.Maximized, w.WindowState);
            Near(normal.Width, w.RestoreBounds.Width, "RestoreBounds.Width"); // un-maximizing later must not mean fullscreen-sized
            Near(normal.Height, w.RestoreBounds.Height, "RestoreBounds.Height");
            w.WindowState = WindowState.Normal;
            SameBounds(w, 150, 100, 640, 400);
            w.Close();
        });
    }

    [Fact]
    public void EnterAndExitAreIdempotent()
    {
        OnSta(() =>
        {
            var chrome = new Border();
            var w = new Window { Left = 80, Top = 60, Width = 500, Height = 300, Content = new Grid { Children = { chrome } } };
            w.Show();
            var fs = new Fullscreen(w, chrome);
            fs.Exit();                      // exit while not fullscreen: nothing happens
            SameBounds(w, 80, 60, 500, 300);
            fs.Enter(); fs.Enter();         // a second Enter must not overwrite the saved placement
            fs.Exit();
            SameBounds(w, 80, 60, 500, 300);
            w.Close();
        });
    }
}

[Collection("db")]
public class FullscreenKeyTests
{
    // Real libmpv: f, F11, double click and Esc must reach Lumina as messages instead of mpv's own fullscreen.
    [Fact]
    public void FullscreenKeysBecomeMessages()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"))) return;
        var ff = @"C:\ffmpeg\bin\ffmpeg.exe";
        if (!File.Exists(ff)) return;

        var dir = Path.Combine(Path.GetTempPath(), "lumina-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var clip = Path.Combine(dir, "clip.mkv");
        using (var proc = System.Diagnostics.Process.Start(ff,
            $"-loglevel error -y -f lavfi -i testsrc=size=320x180:rate=25 -t 10 -c:v libx264 -preset ultrafast \"{clip}\""))
            proc!.WaitForExit();
        try
        {
            using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null");
            var got = new System.Collections.Concurrent.BlockingCollection<string>();
            var loaded = new ManualResetEventSlim();
            m.ClientMessage += a => got.Add(a[0]);
            m.FileLoaded += () => loaded.Set();
            m.Load(clip);
            Assert.True(loaded.Wait(5000));

            foreach (var (key, expected) in new[]
            {
                ("f", "lumina-fullscreen"), ("F11", "lumina-fullscreen"),
                ("MBTN_LEFT_DBL", "lumina-fullscreen"), ("ESC", "lumina-exit-fullscreen"),
            })
            {
                m.Command("keypress", key);
                Assert.True(got.TryTake(out var msg, 3000), key + " produced no message");
                Assert.Equal(expected, msg);
            }
            Assert.Equal("no", m.Get("fullscreen")); // mpv itself never went fullscreen

            // A forwarded mouse double-click is the same message (this is how the real window reaches it).
            m.Command("mouse", "50", "50");
            m.Command("mouse", "50", "50", "0", "double");
            Assert.True(got.TryTake(out var dbl, 3000), "forwarded double click produced no message");
            Assert.Equal("lumina-fullscreen", dbl);
            // A plain forwarded click does nothing special (MBTN_LEFT is not bound to anything of ours).
            m.Command("keydown", "MBTN_LEFT");
            m.Command("keyup", "MBTN_LEFT");
            Assert.False(got.TryTake(out _, 500), "plain click must not trigger fullscreen");
        }
        finally { Directory.Delete(dir, true); }
    }
}


public class MouseForwarderMathTests
{
    [Theory]
    [InlineData(120, "WHEEL_UP", 1)]
    [InlineData(-120, "WHEEL_DOWN", 1)]
    [InlineData(360, "WHEEL_UP", 3)]
    [InlineData(-30, "WHEEL_DOWN", 1)]  // smooth-scroll fractions still scroll one notch
    [InlineData(1, "WHEEL_UP", 1)]
    public void WheelNotches(int delta, string name, int count) =>
        Assert.Equal((name, count), MouseForwarder.Wheel(delta));

    [Fact]
    public void NoWheelMovementIsNothing() => Assert.Null(MouseForwarder.Wheel(0));

    [Theory]
    [InlineData(100.0, 1.0, 100)]
    [InlineData(100.0, 1.75, 175)]
    [InlineData(10.3, 1.5, 15)]     // rounds, not truncates
    [InlineData(0.0, 2.0, 0)]
    public void DipsToPixels(double dips, double scale, int px) => Assert.Equal(px, MouseForwarder.Px(dips, scale));
}
