using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

/// <summary>
/// The controller is drawn with vector shapes, and a mistake there (a name that is not defined, a missing value) only shows when something is drawn.
/// These tests make the script draw in many states and sizes and report any error it raises.
/// </summary>
public class DrawTests
{
    static string? Wait(Mpv m, string prop, Func<string?, bool> ok)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (ok(v)) break; Thread.Sleep(30); }
        return v;
    }

    /// <summary>Draws once and returns "ok" or the error text. The result is cleared first so an old answer is never read.</summary>
    static string Draw(Mpv m, int w, int h)
    {
        m.Set("user-data/lumina/draw_result", "");
        m.Command("script-message", "lumina-do", "draw-test", $"{w}x{h}");
        return Wait(m, "draw_result", v => !string.IsNullOrEmpty(v)) ?? "no answer";
    }

    static void Do(Mpv m, params string[] args) => m.Command(new[] { "script-message", "lumina-do" }.Concat(args).ToArray());

    static Mpv Open(string style)
    {
        var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=" + style);
        m.Command("script-message", "lumina-neighbors", "yes", "yes");
        m.Command("script-message", "lumina-queue", "{\"cur\":0,\"shuffle\":false,\"repeat\":\"off\",\"items\":[\"First\",\"Second\",\"Third\"]}");
        m.Command("script-message", "lumina-bookmarks", "[{\"t\":3,\"label\":\"A\"},{\"t\":8,\"label\":\"B\"}]");
        m.Command("script-message", "lumina-record-state", "yes");
        m.Command("script-message", "lumina-sleep-state", "25 min left");
        Wait(m, "bookmark_count", v => v == "2");
        Wait(m, "queue_count", v => v == "3");
        return m;
    }

    public static IEnumerable<object[]> Sizes() => new[]
    {
        new object[] { 1000, 640 }, new object[] { 1366, 820 }, new object[] { 1920, 1080 }, new object[] { 3840, 2160 },
        new object[] { 700, 400 }, new object[] { 480, 300 }, new object[] { 320, 200 },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void BothStylesDrawWithoutErrorAtEverySizeWithBookmarksRecordingAndAQueue(int w, int h)
    {
        if (!Fixtures.HasMpv) return;
        foreach (var style in new[] { "classic", "floating", "vlc" })
        {
            using var m = Open(style);
            m.Set("user-data/lumina/regions", "");
            Assert.Equal("ok", Draw(m, w, h));
            // regions are published at the very end of a draw, so seeing them proves the whole drawing code ran
            var regions = Wait(m, "regions", v => !string.IsNullOrEmpty(v)) ?? "";
            Assert.Contains("seek", regions);
            Assert.Contains("play", regions);
            Assert.Contains("gear", regions);
        }
    }

    [Theory]
    [InlineData(1366, 820)]
    [InlineData(1920, 1080)]
    [InlineData(700, 400)]
    [InlineData(3840, 2160)]
    public void EveryTabOfTheVlcPanelDrawsAndScrolls(int w, int h)
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("vlc");
        foreach (var tab in new[] { "queue", "bookmarks", "chapters", "playback", "video", "audio", "subs" })
        {
            Do(m, "panel-open", tab);
            Assert.Equal(tab, Wait(m, "panel", v => v == tab));
            Assert.Equal("ok", Draw(m, w, h));
            Assert.Equal(tab, Wait(m, "panel", v => v == tab));
            Do(m, "gear-scroll", "3");
            Assert.Equal("ok", Draw(m, w, h));
        }
    }

    [Fact]
    public void TheVlcBarLeavesRoomForItBelowThePictureExceptInFullscreen()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("vlc");
        Assert.Equal("ok", Draw(m, 1920, 1080));
        Assert.True(double.Parse(m.Get("video-margin-ratio-bottom") ?? "0", System.Globalization.CultureInfo.InvariantCulture) > 0.03);
        m.Command("script-message", "lumina-fullscreen-state", "yes");
        Assert.Equal("ok", Draw(m, 1920, 1080));
        Assert.Equal(0, double.Parse(m.Get("video-margin-ratio-bottom") ?? "0", System.Globalization.CultureInfo.InvariantCulture), 3);
        m.Command("script-message", "lumina-fullscreen-state", "no");
        Do(m, "hide-ui");
        Assert.Equal("ok", Draw(m, 1920, 1080));
        Assert.Equal(0, double.Parse(m.Get("video-margin-ratio-bottom") ?? "0", System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Fact]
    public void TheLightThemeDrawsToo()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("vlc");
        m.Command("script-message", "lumina-theme", "light");
        foreach (var tab in new[] { "subs", "queue" })
        {
            Do(m, "panel-open", tab);
            Assert.Equal("ok", Draw(m, 1366, 820));
        }
    }

    [Fact]
    public void EveryMenuAndPanelDrawsWithoutError()
    {
        if (!Fixtures.HasMpv) return;
        foreach (var style in new[] { "classic", "floating", "vlc" })
        {
            using var m = Open(style);
            foreach (var (w, h) in new[] { (1366, 820), (480, 300) })
            {
                foreach (var menu in new[] { "cc", "audio", "speed", "chapters", "devices", "bookmarks" })
                {
                    m.Command("script-message", "lumina-open-menu", menu);
                    Thread.Sleep(60);
                    Assert.Equal("ok", Draw(m, w, h));
                    m.Command("script-message", "lumina-open-menu", menu);          // close it again
                }
                foreach (var page in new[] { "main", "fx", "hdr" })
                {
                    Do(m, "gear-open", page);
                    Assert.Equal("ok", Draw(m, w, h));
                    Do(m, "gear-scroll", "5");
                    Assert.Equal("ok", Draw(m, w, h));                              // scrolled, with the scroll bar
                }
                Do(m, "queue-open");
                Assert.Equal("ok", Draw(m, w, h));
            }
        }
    }

    [Fact]
    public void ADrawWithNothingGoingOnAndWithEveryExtraStateOnAreBothFine()
    {
        if (!Fixtures.HasMpv) return;
        using var bare = Fixtures.Open("multi.mkv");
        Assert.Equal("ok", Draw(bare, 1366, 820));                                 // no queue, no bookmarks, no neighbours, not recording
        using var busy = Open("classic");
        busy.Command("script-message", "lumina-mini-state", "yes");
        busy.Command("script-message", "lumina-do", "hide-ui");                     // hidden: draws nothing but must not fail
        Assert.Equal("ok", Draw(busy, 1366, 820));
        busy.Command("script-message", "lumina-do", "hide-ui");
        busy.Command("script-message", "lumina-controls", "gear");
        Assert.Equal("ok", Draw(busy, 1366, 820));                                 // a trimmed button set
    }

    [Fact]
    public void ALongTitleAndALongSubtitleLineDoNotBreakTheDrawing()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("floating");
        m.Command("script-message", "lumina-queue", "{\"cur\":0,\"items\":[\"" + new string('x', 400) + "\",\"" + new string('y', 400) + "\"]}");
        Thread.Sleep(150);
        Assert.Equal("ok", Draw(m, 480, 300));
        Assert.Equal("ok", Draw(m, 1366, 820));
    }

    [Fact]
    public void ThePlayingFlagsAreDrawnForABrokenOrPausedStateToo()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("classic");
        m.Set("pause", "no");
        Assert.Equal("ok", Draw(m, 1366, 820));
        m.Set("pause", "yes");
        Assert.Equal("ok", Draw(m, 1366, 820));
        m.Command("seek", "11.9", "absolute+exact");
        Assert.Equal("ok", Draw(m, 1366, 820));
    }
}
