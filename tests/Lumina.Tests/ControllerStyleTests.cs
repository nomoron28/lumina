using System.IO;
using System.Text.Json;
using Lumina.Player;
using Lumina.Settings;
using Xunit;

namespace Lumina.Tests;

/// <summary>The controller's geometry, checked without drawing: the Lua script publishes every region for a window size it is asked about.</summary>
public class ControllerStyleTests
{
    record Box(double X, double Y, double W, double H)
    {
        public double R => X + W;
        public double B => Y + H;
        public bool Overlaps(Box o) => X < o.R - 0.5 && o.X < R - 0.5 && Y < o.B - 0.5 && o.Y < B - 0.5;
        public bool Inside(Box o) => X >= o.X - 0.5 && Y >= o.Y - 0.5 && R <= o.R + 0.5 && B <= o.B + 0.5;
    }

    static string? Wait(Mpv m, string prop, Func<string?, bool> ok)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (ok(v)) break; Thread.Sleep(40); }
        return v;
    }

    /// <summary>Asks the controller for its regions at a size, once the answer is for that size.</summary>
    static Dictionary<string, Box> Layout(Mpv m, int w, int h)
    {
        m.Command("script-message", "lumina-do", "layout-test", $"{w}x{h}");
        // the answer names the window size it is for, so a leftover answer for another size is never mistaken for this one
        var json = Wait(m, "layout_test", v => v != null && v.Replace("\\\"", "\"").Contains($"\"win_w\":{w},") | v.Replace("\\\"", "\"").Contains($"\"win_w\":{w}}}"))!;
        using var doc = JsonDocument.Parse(json.Replace("\\\"", "\""));
        var d = new Dictionary<string, Box>();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Object)
                d[p.Name] = new Box(p.Value.GetProperty("x").GetDouble(), p.Value.GetProperty("y").GetDouble(), p.Value.GetProperty("w").GetDouble(), p.Value.GetProperty("h").GetDouble());
        return d;
    }

    static readonly string[] Row = { "mute", "vol", "prev", "back", "play", "fwd", "next", "chap", "cc", "audio", "speed", "queue", "gear", "snap", "fs" };

    static Mpv Open(string style = "")
    {
        var m = Fixtures.Open("multi.mkv", style.Length > 0 ? "script-opts=lumina-osc-style=" + style : "");
        m.Command("script-message", "lumina-neighbors", "yes", "yes");
        m.Command("script-message", "lumina-queue", "{\"cur\":0,\"items\":[\"a\",\"b\"]}");
        Wait(m, "queue_count", v => v == "2");
        return m;
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1366, 820)]
    [InlineData(1920, 1080)]
    [InlineData(3840, 2160)]
    public void BothStylesKeepEveryControlInsideTheWindowAndApartFromTheOthers(int w, int h)
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        foreach (var style in new[] { "classic", "floating" })
        {
            m.Command("script-message", "lumina-do", "style", style);
            Assert.Equal(style, Wait(m, "style", v => v == style));
            var r = Layout(m, w, h);
            var win = new Box(0, 0, w, h);
            foreach (var (name, box) in r.Where(kv => kv.Key != "pill"))
                Assert.True(box.Inside(win), $"{style} {w}x{h}: {name} is cut off at {box}");
            var row = Row.Where(r.ContainsKey).ToList();
            for (int i = 0; i < row.Count; i++)
                for (int j = i + 1; j < row.Count; j++)
                    Assert.False(r[row[i]].Overlaps(r[row[j]]), $"{style} {w}x{h}: {row[i]} overlaps {row[j]}");
            Assert.True(r["seek"].B - r["play"].Y <= 0.01 * h + 2, $"{style} {w}x{h}: the seek bar runs into the play button");   // the classic bar's hit area touches it by a pixel on purpose
            Assert.True(r["qpanel"].B <= r["play"].Y, $"{style} {w}x{h}: the side panel covers the transport row");
        }
    }

    [Theory]
    [InlineData(1000, 640)]
    [InlineData(1366, 820)]
    [InlineData(3840, 2160)]
    public void TheFloatingBarHoldsEverythingAndSitsAtTheBottomCentre(int w, int h)
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open("floating");
        Assert.Equal("floating", Wait(m, "style", v => v == "floating"));
        var r = Layout(m, w, h);
        var pill = r["pill"];
        Assert.InRange(pill.X + pill.W / 2, w / 2.0 - 1, w / 2.0 + 1);                  // centred
        Assert.True(pill.B < h, "a gap under the bar");
        Assert.True(pill.B > h * 0.8, "near the bottom");
        foreach (var name in Row.Append("seek").Where(r.ContainsKey))
            Assert.True(r[name].Inside(pill), $"{w}x{h}: {name} sticks out of the floating bar: {r[name]} vs {pill}");
        Assert.True(r["seek"].Y < r["play"].Y, "the seek bar is the upper row");
        var close = r["close"];
        Assert.True(close.X < w * 0.1 && close.Y < h * 0.1, "Close is at the top left");
        Assert.False(close.Overlaps(pill));
    }

    [Fact]
    public void TheClassicBarHasNeitherCloseNorPillAndKeepsTheOldRows()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Assert.Equal("classic", Wait(m, "style", v => v == "classic"));
        var r = Layout(m, 1366, 820);
        Assert.False(r.ContainsKey("close"));
        Assert.False(r.ContainsKey("pill"));
        Assert.True(r["mute"].X < 50 && r["fs"].R > 1366 - 50, "classic runs edge to edge");
    }

    [Fact]
    public void ASavedStyleReachesTheControllerAtStartAndNonsenseFallsBackToClassic()
    {
        if (!Fixtures.HasMpv) return;
        using (var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=floating"))
            Assert.Equal("floating", Wait(m, "style", v => v == "floating"));
        using (var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=wobbly"))
            Assert.Equal("classic", Wait(m, "style", v => v == "classic"));
        using var live = Fixtures.Open("multi.mkv");
        live.Command("script-message", "lumina-do", "style", "banana");
        Thread.Sleep(150);
        Assert.Equal("classic", live.Get("user-data/lumina/style")?.Trim('"'));
    }

    [Fact]
    public void SwitchingStyleClosesAnOpenMenuSoNothingHangsFromTheOldPosition()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Command("script-message", "lumina-open-menu", "speed");
        Assert.Equal("speed", Wait(m, "menu", v => v == "speed"));
        m.Command("script-message", "lumina-do", "style", "floating");
        Assert.Equal("", Wait(m, "menu", v => v == ""));
    }

    [Fact]
    public void TheCatalogRowAndTheMpvOptionAgree()
    {
        var d = Catalog.Find("interface.controllerStyle")!;
        Assert.Equal("modern", d.Default);
        Assert.Equal(new[] { "modern", "vlc", "classic", "floating" }, d.Choices!.Select(c => c.Value));
    }

    [Fact]
    public void FloatingStyleBecomesAScriptOptionOnlyWhenChosen()
    {
        var file = Path.Combine(Path.GetTempPath(), "lumina-style-" + Guid.NewGuid().ToString("N") + ".json");
        var old = Environment.GetEnvironmentVariable("LUMINA_CONFIG");
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", file);
        try
        {
            Assert.Contains(AppSettings.MpvLines(), l => l.Contains("lumina-osc-style=modern"));          // the default
            AppSettings.Set("interface.controllerStyle", "classic");
            Assert.DoesNotContain(AppSettings.MpvLines(), l => l.Contains("lumina-osc-style"));        // the script's own default
            AppSettings.Set("interface.controllerStyle", "floating");
            Assert.Contains(AppSettings.MpvLines(), l => l.StartsWith("script-opts=") && l.Contains("lumina-osc-style=floating"));
        }
        finally { Environment.SetEnvironmentVariable("LUMINA_CONFIG", old); File.Delete(file); }
    }
}
