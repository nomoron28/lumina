using System.Globalization;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

/// <summary>Touch swipes over the picture (0.3). Without a video output the controller treats the window as 1280 x 720.</summary>
public class TouchGestureTests
{
    static string? Wait(Mpv m, string prop, string want)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (v == want) break; Thread.Sleep(40); }
        return v;
    }

    static double Num(Mpv m, string prop) => double.Parse(m.Get(prop) ?? "0", CultureInfo.InvariantCulture);

    static void Swipe(Mpv m, (int X, int Y) from, (int X, int Y) to)
    {
        m.Command("script-message", "lumina-touch", "down", from.X.ToString(), from.Y.ToString());
        m.Command("script-message", "lumina-touch", "move", ((from.X + to.X) / 2).ToString(), ((from.Y + to.Y) / 2).ToString());
        m.Command("script-message", "lumina-touch", "move", to.X.ToString(), to.Y.ToString());
        m.Command("script-message", "lumina-touch", "up", to.X.ToString(), to.Y.ToString());
    }

    [Fact]
    public void ASidewaysSwipeSeeksWhenTheFingerLifts()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Set("pause", "yes");
        m.Command("seek", "2", "absolute+exact");
        Swipe(m, (300, 400), (300 + 85, 400));              // 85 of 1280 pixels: about 6 seconds
        Assert.Equal("seek", Wait(m, "gesture", "seek"));
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && Num(m, "time-pos") < 7.5) Thread.Sleep(40);
        Assert.InRange(Num(m, "time-pos"), 7.5, 8.5);
    }

    [Fact]
    public void UpOnTheRightEdgeIsVolumeAndOnTheLeftIsBrightness()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Set("volume", "50");
        Swipe(m, (1100, 500), (1100, 260));                 // a third of the height up: about 50 points
        Assert.Equal("volume", Wait(m, "gesture", "volume"));
        Assert.InRange(Num(m, "volume"), 95, 105);

        Swipe(m, (150, 300), (150, 540));                   // down on the left
        Assert.Equal("brightness", Wait(m, "gesture", "brightness"));
        Assert.InRange(Num(m, "brightness"), -55, -45);
    }

    [Fact]
    public void TapsAndSwipesInTheMiddleAreNotGestures()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Set("volume", "50");
        Swipe(m, (1100, 500), (1100, 300));
        Assert.Equal("volume", Wait(m, "gesture", "volume"));

        Swipe(m, (640, 500), (644, 497));                   // a tap: too small to be a swipe
        Assert.Equal("", Wait(m, "gesture", ""));
        Swipe(m, (1100, 500), (1100, 300));
        Assert.Equal("volume", Wait(m, "gesture", "volume"));
        Swipe(m, (640, 500), (640, 200));                   // up and down in the middle third: nothing
        Assert.Equal("", Wait(m, "gesture", ""));
    }
}
