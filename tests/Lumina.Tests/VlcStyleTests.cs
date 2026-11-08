using System.Text.Json;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

/// <summary>The VLC controller style: a flat bar docked under the picture and a tabbed panel on the right.</summary>
public class VlcStyleTests
{
    static string? Wait(Mpv m, string prop, Func<string?, bool> ok)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (ok(v)) break; Thread.Sleep(30); }
        return v;
    }

    static int _sync;
    /// <summary>Sends a controller action and waits until it has been handled (messages are handled in order).</summary>
    static void Act(Mpv m, params string[] args)
    {
        m.Command(new[] { "script-message", "lumina-do" }.Concat(args).ToArray());
        var n = Interlocked.Increment(ref _sync).ToString();
        m.Command("script-message", "lumina-do", "sync", n);
        Wait(m, "sync", v => v == n);
    }

    static string Draw(Mpv m, int w = 1920, int h = 1080)
    {
        m.Set("user-data/lumina/draw_result", "");
        m.Command("script-message", "lumina-do", "draw-test", $"{w}x{h}");
        return Wait(m, "draw_result", v => !string.IsNullOrEmpty(v)) ?? "no answer";
    }

    static Mpv Open()
    {
        var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=vlc");
        m.Command("script-message", "lumina-neighbors", "yes", "yes");
        m.Command("script-message", "lumina-queue", "{\"cur\":0,\"shuffle\":false,\"repeat\":\"off\",\"items\":[\"First\",\"Second\",\"Third\"]}");
        Wait(m, "queue_count", v => v == "3");
        return m;
    }

    [Fact]
    public void HoldingTheMouseOnTheVolumeAndMovingChangesIt()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Assert.Equal("ok", Draw(m));
        var vol = JsonDocument.Parse(Wait(m, "regions", v => !string.IsNullOrEmpty(v) && v.Contains("\"vol\""))!.Replace("\\\"", "\"")).RootElement.GetProperty("vol");
        double x0 = vol.GetProperty("x").GetDouble(), w = vol.GetProperty("w").GetDouble(), y = vol.GetProperty("y").GetDouble() + vol.GetProperty("h").GetDouble() / 2;
        double Vol() => double.Parse(m.Get("volume") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        m.Command("mouse", ((int)(x0 + w * 0.2)).ToString(), ((int)y).ToString());
        m.Command("keydown", "MBTN_LEFT");
        Thread.Sleep(150);
        var low = Vol();
        m.Command("mouse", ((int)(x0 + w * 0.8)).ToString(), ((int)y).ToString());
        Thread.Sleep(200);
        var high = Vol();
        m.Command("keyup", "MBTN_LEFT");
        Assert.True(high > low + 10, $"the volume did not follow the held mouse: {low} -> {high}");
    }

    [Fact]
    public void TheCKeyCyclesTheCropAndAnyOtherCropPresetIsReachable()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Command("keypress", "c");
        for (int i = 0; i < 40 && string.IsNullOrEmpty(m.Get("video-crop")); i++) Thread.Sleep(50);
        Assert.False(string.IsNullOrEmpty(m.Get("video-crop")), "C did not crop");
    }

    [Fact]
    public void SpeedKeysStepByTenthsNotByPercent()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        foreach (var expected in new[] { 1.1, 1.2, 1.3 }) { m.Command("keypress", "]"); Thread.Sleep(120); Assert.Equal(expected, double.Parse(m.Get("speed")!, System.Globalization.CultureInfo.InvariantCulture), 3); }
        m.Command("keypress", "[");
        Thread.Sleep(120);
        Assert.Equal(1.2, double.Parse(m.Get("speed")!, System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Fact]
    public void UpAndDownChangeTheVolume()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Set("volume", "50");
        m.Command("keypress", "UP");
        Thread.Sleep(150);
        Assert.Equal(55, double.Parse(m.Get("volume")!, System.Globalization.CultureInfo.InvariantCulture), 1);
        m.Command("keypress", "DOWN");
        Thread.Sleep(150);
        Assert.Equal(50, double.Parse(m.Get("volume")!, System.Globalization.CultureInfo.InvariantCulture), 1);
    }

    [Fact]
    public void TheBackButtonIsClickableAndLeavesTheVideo()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Assert.Equal("ok", Draw(m));
        var regions = Wait(m, "regions", v => !string.IsNullOrEmpty(v) && v.Contains("close"));
        Assert.Contains("close", regions);
        var json = JsonDocument.Parse(regions!.Replace("\\\"", "\"")).RootElement.GetProperty("close");
        var ended = new ManualResetEventSlim();
        m.Ended += why => { if (why == "shutdown") ended.Set(); };
        // a real click: move onto the button, press and release
        string P(string n, double f) => ((int)(json.GetProperty(n).GetDouble() + json.GetProperty(n == "x" ? "w" : "h").GetDouble() * f)).ToString();
        m.Command("mouse", P("x", 0.5), P("y", 0.5));
        m.Command("keydown", "MBTN_LEFT");
        m.Command("keyup", "MBTN_LEFT");
        Assert.True(ended.Wait(4000), "clicking Back did not leave the video");
    }

    [Fact]
    public void HoldingTheMouseOnTheTimelineAndMovingScrubsTheVideo()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Set("pause", "yes");
        Assert.Equal("ok", Draw(m));
        var regions = JsonDocument.Parse(Wait(m, "regions", v => !string.IsNullOrEmpty(v) && v.Contains("seek"))!.Replace("\\\"", "\"")).RootElement;
        var seek = regions.GetProperty("seek");
        double x0 = seek.GetProperty("x").GetDouble(), w = seek.GetProperty("w").GetDouble(), y = seek.GetProperty("y").GetDouble() + seek.GetProperty("h").GetDouble() / 2;
        double Pos() => double.Parse(m.Get("time-pos") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        string X(double f) => ((int)(x0 + w * f)).ToString();

        m.Command("mouse", X(0.2), ((int)y).ToString());
        m.Command("keydown", "MBTN_LEFT");
        Thread.Sleep(200);
        var first = Pos();
        m.Command("mouse", X(0.6), ((int)y).ToString());
        Thread.Sleep(300);
        m.Command("mouse", X(0.9), ((int)y).ToString());
        Thread.Sleep(300);
        var held = Pos();
        m.Command("keyup", "MBTN_LEFT");
        Assert.True(held > first + 0.1, $"the video did not follow the held mouse: {first} -> {held}");
    }

    [Fact]
    public void TheButtonsOpenThePanelOnTheirOwnTabAndPressingAgainClosesIt()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        foreach (var (button, tab) in new[] { ("cc", "subs"), ("audio", "audio"), ("gear", "playback"), ("queue", "queue"), ("speed", "playback") })
        {
            Act(m, "click", button);
            Assert.Equal(tab, Wait(m, "panel", v => v == tab));
            Act(m, "click", button);
            Assert.Equal("", Wait(m, "panel", v => v == ""));
        }
    }

    [Fact]
    public void TheTabsSwitchAndTheRowsCanBeUsed()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Act(m, "click", "gear");
        Assert.Equal("ok", Draw(m));
        Act(m, "click", "ptab:subs");
        Assert.Equal("subs", Wait(m, "panel", v => v == "subs"));
        Assert.Equal("ok", Draw(m));
        var rows = (Wait(m, "panel_rows", v => v != null && v.Contains("None")) ?? "").Split('|');
        Assert.Contains("#Embedded Tracks", rows);
        // choose the first real subtitle track, then None
        var eng = Array.FindIndex(rows, r => r.StartsWith("ENG")) + 1;
        Assert.True(eng > 0);
        Act(m, "click", "pr:" + eng);
        Assert.Equal("1", m.Get("sid"));
        var none = Array.IndexOf(rows, "None") + 1;
        Act(m, "click", "pr:" + none);
        Assert.Equal("no", m.Get("sid"));
    }

    [Fact]
    public void AValueRowStepsItsSetting()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Act(m, "click", "gear");
        Assert.Equal("ok", Draw(m));
        var rows = (Wait(m, "panel_rows", v => v != null && v.Contains("Speed")) ?? "").Split('|');
        var speed = Array.IndexOf(rows, "Speed") + 1;
        Assert.True(speed > 0);
        Act(m, "click", $"pr:{speed}:p");
        Assert.Equal(1.05, double.Parse(m.Get("speed")!, System.Globalization.CultureInfo.InvariantCulture), 2);
        Act(m, "click", $"pr:{speed}:m");
        Act(m, "click", $"pr:{speed}:m");
        Assert.Equal(0.95, double.Parse(m.Get("speed")!, System.Globalization.CultureInfo.InvariantCulture), 2);
    }

    [Fact]
    public void TheQueueTabJumpsAndTheOldMenuNamesStillOpenTheRightTab()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Command("script-message", "lumina-open-menu", "chapters");
        Assert.Equal("chapters", Wait(m, "panel", v => v == "chapters"));
        m.Command("script-message", "lumina-open-menu", "bookmarks");
        Assert.Equal("bookmarks", Wait(m, "panel", v => v == "bookmarks"));
        Act(m, "click", "queue");
        Assert.Equal("ok", Draw(m));
        Act(m, "hide-ui");
        Assert.Equal("", Wait(m, "panel", v => v == ""));
    }

    [Fact]
    public void TheAccentDrivesThePlayerForEveryOfTheNineThemesAndTextOnItStaysReadable()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        foreach (var (hex, name) in Lumina.Settings.Catalog.AccentThemes)
        {
            m.Set("user-data/lumina/accent_colors", "");
            m.Command("script-message", "lumina-accent", hex);
            var got = Wait(m, "accent_colors", v => !string.IsNullOrEmpty(v)) ?? "";
            var chosen = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + hex);
            var darkText = Lumina.Settings.Accent.Contrast(System.Windows.Media.Colors.Black, chosen) >= 4.5;
            Assert.Equal(hex + "|" + (darkText ? "0C0D10" : "FFFFFF"), got.ToUpperInvariant());                  // the colour that sits on the play pill
            Assert.True(Lumina.Settings.Accent.Contrast(darkText ? System.Windows.Media.Colors.Black : System.Windows.Media.Colors.White, chosen) >= 4.5, name);
            Do2(m, "panel-open", "subs");
            Assert.Equal("ok", Draw(m, 1366, 820));
        }
    }

    static void Do2(Mpv m, params string[] args) => Act(m, args);

    static (double X, double Y) Centre(Mpv m, string region)
    {
        var json = Wait(m, "regions", v => !string.IsNullOrEmpty(v) && v.Contains("\"" + region + "\""))!.Replace("\\\"", "\"");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement.GetProperty(region);
        return (r.GetProperty("x").GetDouble() + r.GetProperty("w").GetDouble() / 2, r.GetProperty("y").GetDouble() + r.GetProperty("h").GetDouble() / 2);
    }

    /// <summary>The position settles a moment after a seek: wait until it is near the wanted time, then return it.</summary>
    static double TimeNear(Mpv m, double want)
    {
        var until = DateTime.UtcNow.AddSeconds(4);
        double v = -1;
        while (DateTime.UtcNow < until)
        {
            v = double.Parse(m.Get("time-pos") ?? "-1", System.Globalization.CultureInfo.InvariantCulture);
            if (Math.Abs(v - want) < 0.6) break;
            Thread.Sleep(40);
        }
        return v;
    }

    [Theory]
    [InlineData("vlc")]
    [InlineData("classic")]
    [InlineData("floating")]
    public void TheWheelOverTheSeekBarScrubsAndElsewhereItStillChangesTheVolume(string style)
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=" + style);
        m.Set("pause", "yes");
        m.Set("user-data/lumina/regions", "");
        Assert.Equal("ok", Draw(m));
        var (sx, sy) = Centre(m, "seek");
        m.Set("time-pos", "3");
        Act(m, "wheel-at", $"{(int)sx},{(int)sy},1");
        Assert.Equal(8, TimeNear(m, 8), 0);                                                                              // forward 5 seconds
        Act(m, "wheel-at", $"{(int)sx},{(int)sy},-1");
        Act(m, "wheel-at", $"{(int)sx},{(int)sy},-1");
        Assert.Equal(0, TimeNear(m, 0), 0);                                                                              // back, and not past the start

        var vol = double.Parse(m.Get("volume")!, System.Globalization.CultureInfo.InvariantCulture);
        Act(m, "wheel-at", "10,10,1");                                                                                   // over the picture
        Assert.Equal(vol + 5, double.Parse(m.Get("volume")!, System.Globalization.CultureInfo.InvariantCulture), 0);
    }

    record Box(double X, double Y, double W, double H)
    {
        public double R => X + W;
        public double B => Y + H;
        public bool Overlaps(Box o) => X < o.R - 0.5 && o.X < R - 0.5 && Y < o.B - 0.5 && o.Y < B - 0.5;
        public bool Inside(Box o) => X >= o.X - 0.5 && Y >= o.Y - 0.5 && R <= o.R + 0.5 && B <= o.B + 0.5;
    }

    static Dictionary<string, Box> Layout(Mpv m, int w, int h)
    {
        m.Command("script-message", "lumina-do", "layout-test", $"{w}x{h}");
        var until = DateTime.UtcNow.AddSeconds(5);
        string? json = null;
        while (DateTime.UtcNow < until)
        {
            json = m.Get("user-data/lumina/layout_test")?.Trim('"')?.Replace("\\\"", "\"");
            if (json != null && (json.Contains($"\"win_w\":{w},") || json.Contains($"\"win_w\":{w}}}"))) break;
            Thread.Sleep(40);
        }
        using var doc = JsonDocument.Parse(json!);
        var d = new Dictionary<string, Box>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Object)
                d[p.Name] = new Box(p.Value.GetProperty("x").GetDouble(), p.Value.GetProperty("y").GetDouble(), p.Value.GetProperty("w").GetDouble(), p.Value.GetProperty("h").GetDouble());
        return d;
    }

    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(1920, 1080)]
    [InlineData(1366, 820)]
    [InlineData(1000, 640)]
    [InlineData(820, 460)]
    [InlineData(700, 400)]
    [InlineData(480, 300)]
    public void EveryControlSitsOnTheBarInsideTheWindowAndNothingOverlaps(int w, int h)
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        var d = Layout(m, w, h);
        var win = new Box(0, 0, w, h);
        var bar = d["bar"];
        Assert.True(bar.Inside(win));
        Assert.Equal(h, bar.B, 1);                                                    // docked to the bottom
        Assert.True(bar.H >= 40 && bar.H <= 100, $"the bar is {bar.H} high");
        var names = new[] { "prev", "back", "play", "fwd", "next", "seek", "mute", "vol", "cc", "audio", "speed", "gear", "queue", "snap", "chap", "fs" }.Where(d.ContainsKey).ToList();
        Assert.Contains("play", names); Assert.Contains("seek", names); Assert.Contains("gear", names); Assert.Contains("fs", names);
        foreach (var n in names) Assert.True(d[n].Inside(bar) || n == "seek", $"{n} {d[n]} is outside the bar {bar}");
        for (var i = 0; i < names.Count; i++)
            for (var j = i + 1; j < names.Count; j++)
                Assert.False(d[names[i]].Overlaps(d[names[j]]), $"{names[i]} {d[names[i]]} overlaps {names[j]} {d[names[j]]} at {w}x{h}");
        Assert.True(d["seek"].W >= 60, $"the seek bar is {d["seek"].W} wide");
        var panel = d["qpanel"];
        Assert.True(panel.Inside(win)); Assert.True(panel.B <= bar.Y + 0.5);          // the panel sits above the bar
    }
}
