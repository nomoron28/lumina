using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class MiniGeometryTests
{
    static readonly MiniGeometry.Box Screen = new(0, 0, 1920, 1040);          // a work area: the screen without the taskbar

    [Fact]
    public void ItSitsInTheBottomRightCornerWithAMarginAndTheVideoAreaIs480Wide()
    {
        var b = MiniGeometry.For(Screen, 16.0 / 9, 32);
        Assert.Equal(480, b.Width);
        Assert.Equal(480 / (16.0 / 9) + 32, b.Height, 6);
        Assert.Equal(1920 - 480 - 24, b.Left);
        Assert.Equal(1040 - b.Height - 24, b.Top, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnknownAspectIsTreatedAsWidescreen(double aspect) =>
        Assert.Equal(MiniGeometry.For(Screen, 16.0 / 9, 30), MiniGeometry.For(Screen, aspect, 30));

    [Theory]
    [InlineData(0.1)]
    [InlineData(9)]
    public void AnExtremeAspectIsHeldToASensibleShape(double aspect)
    {
        var b = MiniGeometry.For(Screen, aspect, 30);
        Assert.InRange(b.Height, 480 / MiniGeometry.MaxAspect + 30 - 0.01, 480 / MiniGeometry.MinAspect + 30 + 0.01);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040)]
    [InlineData(-1920, 0, 1920, 1080)]       // a second monitor to the left
    [InlineData(1920, -200, 2560, 1400)]     // one above and to the right
    [InlineData(0, 0, 640, 480)]             // a small screen
    [InlineData(0, 0, 320, 240)]             // a tiny one
    public void TheWindowAlwaysStaysInsideTheWorkArea(double l, double t, double w, double h)
    {
        var wa = new MiniGeometry.Box(l, t, w, h);
        foreach (var aspect in new[] { 0.6, 1.0, 16.0 / 9, 2.39, 2.6 })
        {
            var b = MiniGeometry.For(wa, aspect, 32);
            Assert.True(b.Left >= wa.Left - 0.01 && b.Top >= wa.Top - 0.01, $"{aspect}: starts outside the work area ({b})");
            Assert.True(b.Left + b.Width <= wa.Left + wa.Width + 0.01 || wa.Width < 300, $"{aspect}: runs off the right ({b})");
            Assert.True(b.Top + b.Height <= wa.Top + wa.Height + 0.01 || wa.Height < 200, $"{aspect}: runs off the bottom ({b})");
        }
    }

    [Fact]
    public void ANegativeFrameHeightIsTreatedAsNone() =>
        Assert.Equal(MiniGeometry.For(Screen, 2, 0).Height, MiniGeometry.For(Screen, 2, -50).Height);
}

/// <summary>The compact controller used in a small window.</summary>
public class CompactControllerTests
{
    record Box(double X, double Y, double W, double H)
    {
        public double R => X + W;
        public double B => Y + H;
        public bool Overlaps(Box o) => X < o.R - 0.5 && o.X < R - 0.5 && Y < o.B - 0.5 && o.Y < B - 0.5;
        public bool Inside(Box o) => X >= o.X - 0.5 && Y >= o.Y - 0.5 && R <= o.R + 0.5 && B <= o.B + 0.5;
    }

    static Dictionary<string, Box> Layout(Mpv m, int w, int h, out bool compact)
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
        using var doc = System.Text.Json.JsonDocument.Parse(json!);
        compact = doc.RootElement.GetProperty("compact").GetInt32() == 1;
        var d = new Dictionary<string, Box>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                d[p.Name] = new Box(p.Value.GetProperty("x").GetDouble(), p.Value.GetProperty("y").GetDouble(), p.Value.GetProperty("w").GetDouble(), p.Value.GetProperty("h").GetDouble());
        return d;
    }

    [Fact]
    public void TheMiniplayerIsCompactEvenWhenAHighDpiScreenMakesItWiderThan760Pixels()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("classic");
        Layout(m, 840, 472, out var before);
        Assert.False(before);                      // an ordinary window of that size is not compact
        m.Command("script-message", "lumina-mini-state", "yes");
        Thread.Sleep(150);
        var d = Layout(m, 842, 472, out var compact);   // a different width, so the answer read is the new one
        Assert.True(compact);
        Assert.False(d.ContainsKey("vol") || d.ContainsKey("cc") || d.ContainsKey("audio") || d.ContainsKey("speed"));
    }

    static Mpv Open(string style)
    {
        var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=" + style);
        m.Command("script-message", "lumina-neighbors", "yes", "yes");
        m.Command("script-message", "lumina-queue", "{\"cur\":0,\"items\":[\"a\",\"b\"]}");
        Thread.Sleep(200);
        return m;
    }

    [Theory]
    [InlineData("classic", 480, 300)]
    [InlineData("classic", 560, 330)]
    [InlineData("classic", 759, 420)]
    [InlineData("floating", 480, 300)]
    [InlineData("floating", 560, 330)]
    [InlineData("floating", 759, 420)]
    [InlineData("classic", 320, 200)]
    public void ASmallWindowKeepsOnlyWhatFitsAndNothingOverlapsOrIsCutOff(string style, int w, int h)
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open(style);
        var r = Layout(m, w, h, out var compact);
        Assert.True(compact, "below 760 px wide the controller is compact");
        var win = new Box(0, 0, w, h);
        foreach (var (name, box) in r.Where(kv => kv.Key != "pill"))
            Assert.True(box.Inside(win), $"{style} {w}x{h}: {name} is cut off ({box})");
        foreach (var gone in new[] { "mute", "vol", "chap", "cc", "audio", "speed", "queue", "snap" })
            Assert.False(r.ContainsKey(gone), $"{style} {w}x{h}: {gone} should be left to the player panel");
        foreach (var kept in new[] { "play", "seek", "gear", "fs" }.Concat(w >= 480 ? new[] { "back", "fwd" } : Array.Empty<string>()))
            Assert.True(r.ContainsKey(kept), $"{style} {w}x{h}: {kept} is missing");
        var row = new[] { "prev", "back", "play", "fwd", "next", "gear", "fs" }.Where(r.ContainsKey).ToList();
        for (var i = 0; i < row.Count; i++)
            for (var j = i + 1; j < row.Count; j++)
                Assert.False(r[row[i]].Overlaps(r[row[j]]), $"{style} {w}x{h}: {row[i]} overlaps {row[j]}");
    }

    [Fact]
    public void PreviousAndNextAreKeptOnlyWhenThereIsRoomForThem()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("classic");
        Assert.False(Layout(m, 560, 330, out _).ContainsKey("next"));
        Assert.True(Layout(m, 700, 400, out _).ContainsKey("next"));
    }

    [Fact]
    public void AtAndAboveTheThresholdTheFullControllerReturns()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("classic");
        var r = Layout(m, 760, 430, out var compact);
        Assert.False(compact);
        Assert.True(r.ContainsKey("mute") && r.ContainsKey("vol") && r.ContainsKey("cc"));      // (speed and the rest may be left out when they would not fit)
    }

    [Fact]
    public void TheMiniplayerKeyAndPanelRowAskTheAppAndTheStateIsPublished()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        m.Command("script-message", "lumina-do", "miniplayer");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until) { lock (got) if (got.Any(a => a.Length > 0 && a[0] == "lumina-miniplayer")) break; Thread.Sleep(30); }
        lock (got) Assert.Contains(got, a => a.Length > 0 && a[0] == "lumina-miniplayer");
        m.Command("script-message", "lumina-mini-state", "yes");
        until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && m.Get("user-data/lumina/mini")?.Trim('"') != "yes") Thread.Sleep(30);
        Assert.Equal("yes", m.Get("user-data/lumina/mini")?.Trim('"'));
        var shipped = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "input.conf"));
        Assert.Contains("w script-message lumina-miniplayer", shipped);
        Assert.Empty(Shortcuts.Conflicts(new Dictionary<string, string>()));
    }
}
