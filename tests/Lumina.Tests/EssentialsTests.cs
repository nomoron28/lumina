using System.Globalization;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class TimecodeTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:02:03", 3723)]
    [InlineData("1:02:03.5", 3723.5)]
    [InlineData("0:00", 0)]
    [InlineData("1h2m3s", 3723)]
    [InlineData("90s", 90)]
    [InlineData("2m", 120)]
    [InlineData(" 5:00 ", 300)]
    [InlineData("1.5", 1.5)]
    public void ParsesTheFormsPeopleType(string text, double seconds) => Assert.Equal(seconds, Timecode.Parse(text)!.Value, 3);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("1:75")]        // minutes and seconds stop at 59
    [InlineData("1.5:30")]      // only seconds may be fractional
    [InlineData("-5")]
    [InlineData("1:2:3:4")]
    [InlineData("1h2x")]
    [InlineData("h")]
    [InlineData("5m3")]         // trailing number without a unit
    [InlineData("::")]
    public void RejectsNonsense(string text) => Assert.Null(Timecode.Parse(text));

    [Fact]
    public void FormatRoundTripsWithParse()
    {
        foreach (var s in new[] { 0.0, 59, 60, 3599, 3600, 7322 })
            Assert.Equal(s, Timecode.Parse(Timecode.Format(s))!.Value, 3);
        Assert.Equal("0:00", Timecode.Format(-4));
    }
}

public class EssentialsActionTests
{
    static int _sync;
    static void Do(Mpv m, string action, string? arg = null)
    {
        if (arg == null) m.Command("script-message", "lumina-do", action); else m.Command("script-message", "lumina-do", action, arg);
        // the script handles messages on its own thread, in order: ask it to answer a question and wait for that answer
        var id = Interlocked.Increment(ref _sync).ToString();
        m.Command("script-message", "lumina-do", "sync", id);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && m.Get("user-data/lumina/sync")?.Trim('"') != id) Thread.Sleep(10);
        Thread.Sleep(20);
    }

    static double N(Mpv m, string p) => double.Parse(m.Get(p)!, CultureInfo.InvariantCulture);

    // Crop is applied by the video pipeline a moment after the property is set.
    static string? Settles(Mpv m, string prop, string expected)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until && (m.Get(prop) ?? "") != expected) Thread.Sleep(50);
        return m.Get(prop);
    }

    static bool NoCrop(Mpv m) => string.IsNullOrEmpty(m.Get("video-crop")) || m.Get("video-crop") == "0x0+0+0";

    [Fact]
    public void DelaysSpeedAndZoomStepAndClamp()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Do(m, "sub-delay", "0.1"); Do(m, "sub-delay", "0.1");
        Assert.Equal(0.2, N(m, "sub-delay"), 3);
        Do(m, "sub-delay", "-0.5");
        Assert.Equal(-0.3, N(m, "sub-delay"), 3);
        Do(m, "audio-delay", "0.1");
        Assert.Equal(0.1, N(m, "audio-delay"), 3);
        Do(m, "speed", "0.05"); Do(m, "speed", "0.05");
        Assert.Equal(1.1, N(m, "speed"), 3);
        Do(m, "speed", "-100");                       // far below the floor
        Assert.Equal(0.25, N(m, "speed"), 3);
        Do(m, "zoom", "0.1");
        Assert.Equal(0.1, N(m, "video-zoom"), 3);
        Do(m, "zoom", "100");                         // clamped at the ceiling
        Assert.Equal(3, N(m, "video-zoom"), 3);
    }

    [Fact]
    public void PictureSlidersStayInsideMinusAndPlusOneHundred()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        foreach (var p in new[] { "brightness", "contrast", "saturation", "gamma", "hue" })
        {
            Do(m, p, "5");
            Assert.Equal(5, N(m, p), 3);
            Do(m, p, "500");
            Assert.Equal(100, N(m, p), 3);
            Do(m, p, "-500");
            Assert.Equal(-100, N(m, p), 3);
        }
    }

    [Fact]
    public void RotateAspectAndCropCycleThroughTheirPresets()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Fixtures.Eventually(m, "video-params/w");
        var turns = new List<double>();
        for (var i = 0; i < 5; i++) { Do(m, "rotate"); turns.Add(N(m, "video-rotate")); }
        Assert.Equal(new double[] { 90, 180, 270, 0, 90 }, turns);

        Do(m, "aspect");                               // 16:9
        Assert.Equal(16.0 / 9, N(m, "video-aspect-override"), 2);
        Do(m, "aspect", "1");                          // back to auto by index
        Assert.Equal(-1, N(m, "video-aspect-override"), 2);

        Do(m, "crop", "4");                            // rotated 90 right now: refused, nothing changes
        Thread.Sleep(300);
        Assert.True(NoCrop(m));
        Do(m, "rotate", "0");
        Do(m, "crop", "4");                            // 4:3 of a 640x360 picture
        Assert.Equal("480x360+80+0", Settles(m, "video-crop", "480x360+80+0"));
        Do(m, "crop", "11");                            // 1:1
        Assert.Equal("360x360+140+0", Settles(m, "video-crop", "360x360+140+0"));
        Do(m, "crop", "1");
        Settles(m, "video-crop", "");
        Assert.True(NoCrop(m));
    }

    [Fact]
    public void ABLoopCyclesStartEndAndOff()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("seek", "2", "absolute+exact"); Thread.Sleep(200);
        Do(m, "ab-loop");
        Assert.Equal(2, N(m, "ab-loop-a"), 0);
        Assert.Equal("no", m.Get("ab-loop-b"));
        m.Command("seek", "6", "absolute+exact"); Thread.Sleep(200);
        Do(m, "ab-loop");
        Assert.Equal(6, N(m, "ab-loop-b"), 0);
        Do(m, "ab-loop");
        Assert.Equal("no", m.Get("ab-loop-a"));
        Assert.Equal("no", m.Get("ab-loop-b"));
    }

    [Fact]
    public void ResetEverythingPutsEveryAdjustmentBack()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Fixtures.Eventually(m, "video-params/w");
        Do(m, "sub-delay", "1"); Do(m, "audio-delay", "1"); Do(m, "speed", "0.5"); Do(m, "zoom", "0.5"); Do(m, "rotate");
        Do(m, "aspect"); Do(m, "crop", "3"); Do(m, "brightness", "20"); Do(m, "hue", "-20");
        Do(m, "reset-all");
        Assert.Equal(0, N(m, "sub-delay")); Assert.Equal(0, N(m, "audio-delay")); Assert.Equal(1, N(m, "speed"));
        Assert.Equal(0, N(m, "video-zoom")); Assert.Equal(0, N(m, "video-rotate")); Assert.Equal(0, N(m, "brightness")); Assert.Equal(0, N(m, "hue"));
        Assert.Equal(-1, N(m, "video-aspect-override"), 2);
        Assert.True(NoCrop(m));
    }

    [Fact]
    public void UnknownActionsAreIgnoredWithoutBreakingTheController()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Do(m, "no-such-action", "1");
        Do(m, "sub-delay", "not a number");            // falls back to the default step instead of throwing
        Assert.Equal(0.1, N(m, "sub-delay"), 3);
        Assert.Equal("yes", m.Get("user-data/lumina/ready")?.Trim('"'));
    }
}

public class MenuContentTests
{
    static string[] Items(Mpv m, string menu)
    {
        m.Command("script-message", "lumina-open-menu", menu);
        Thread.Sleep(250);
        return (m.Get("user-data/lumina/menu_items") ?? "").Trim('"').Split('|');
    }

    [Fact]
    public void ChapterMenuListsEveryChapterWithItsTitle()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var items = Items(m, "chapters");
        Assert.Equal(new[] { "1  Opening", "2  Middle", "3  Ending" }, items);
    }

    [Fact]
    public void AudioMenuListsTracksThenTheOutputDeviceEntry()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var items = Items(m, "audio");
        Assert.Equal(3, items.Length);
        Assert.StartsWith("ENG", items[0]);
        Assert.StartsWith("JPN", items[1]);
        Assert.Equal("Output device", items[2]);
    }

    [Fact]
    public void SubtitleMenuHasOffTwoTracksTheSearchEntriesAndTheLookControls()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var items = Items(m, "cc");
        Assert.Equal(new[] { "Off", "ENG", "SPA", "Search OpenSubtitles", "Choose from all matches...", "Larger text", "Smaller text", "Move up", "Move down", "Show later (+0.1 s)", "Show earlier (-0.1 s)" }, items);
    }

    [Fact]
    public void DeviceMenuAlwaysOffersAtLeastTheSystemDefault()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var items = Items(m, "devices");
        Assert.NotEmpty(items);
        Assert.False(string.IsNullOrWhiteSpace(items[0]));
    }

    [Fact]
    public void AnUnknownMenuNameOpensAnEmptyMenuWithoutBreakingAnything()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-open-menu", "nonsense");
        Thread.Sleep(200);
        Assert.Equal("", m.Get("user-data/lumina/menu")?.Trim('"'));   // nothing opened, so nothing for the draw code to choke on
        Assert.Equal("yes", m.Get("user-data/lumina/ready")?.Trim('"'));
    }
}

public class MediaKeyTests
{
    [Fact]
    public void NextAndPreviousMediaKeysReachTheHostInsteadOfMpvsOwnPlaylist()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new System.Collections.Concurrent.BlockingCollection<string>();
        m.ClientMessage += a => { if (a.Length > 0) got.Add(a[0]); };
        m.Command("keypress", "NEXT");
        m.Command("keypress", "PREV");
        Assert.True(got.TryTake(out var first, 3000)); Assert.Equal("lumina-next", first);
        Assert.True(got.TryTake(out var second, 3000)); Assert.Equal("lumina-prev", second);
    }

    [Fact]
    public void MpvAcceptsTheMediaControlsOptionAndSeesTheTitleWeSet()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Set("force-media-title", "Show Name  S01E02");
        Assert.Equal("Show Name  S01E02", m.Get("media-title"));
        Assert.Empty(m.BadOptions);          // media-controls=yes in mpv.conf was accepted
    }

    [Fact]
    public void PlayPauseKeyStillTogglesPause()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");        // starts paused
        m.Command("keypress", "PLAYPAUSE"); Thread.Sleep(250);
        Assert.Equal("no", m.Get("pause"));
        m.Command("keypress", "PLAYPAUSE"); Thread.Sleep(250);
        Assert.Equal("yes", m.Get("pause"));
    }
}

public class FxPanelMessageTests
{
    static List<string[]> Capture(Mpv m, Action act)
    {
        var got = new System.Collections.Concurrent.BlockingCollection<string[]>();
        m.ClientMessage += a => got.Add(a);
        act();
        Thread.Sleep(400);
        return got.ToList();
    }

    [Fact]
    public void PanelActionsAreSentToTheHostSoItOwnsTheSettings()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var msgs = Capture(m, () =>
        {
            foreach (var (a, arg) in new[] { ("fx-open", (string?)null), ("fx-eq", null), ("fx-comp", null), ("fx-space", null), ("fx-width", "-10"), ("fx-norm", null), ("fx-night", null) })
                if (arg == null) m.Command("script-message", "lumina-do", a); else m.Command("script-message", "lumina-do", a, arg);
        });
        var fx = msgs.Where(x => x.Length > 0 && x[0] == "lumina-fx").Select(x => string.Join(" ", x.Skip(1))).ToList();
        Assert.Equal(new[] { "refresh", "eq", "comp", "space", "width -10", "norm", "night" }, fx);
    }

    [Fact]
    public void TheControllerShowsWhatTheHostReports()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-fx-state", "{\"eq\":\"Rock\",\"comp\":\"Gentle\",\"space\":\"Hall\",\"width\":40,\"norm\":true,\"night\":false}");
        Assert.Equal("Rock|Gentle|Hall|40|true|false", Fixtures.Eventually(m, "user-data/lumina/fx")?.Trim('"'));
        m.Command("script-message", "lumina-fx-state", "garbage");                  // must not break the controller
        Thread.Sleep(200);
        Assert.Equal("yes", m.Get("user-data/lumina/ready")?.Trim('"'));
    }
}
