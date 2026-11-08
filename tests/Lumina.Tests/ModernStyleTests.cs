using System.Text.Json;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

/// <summary>The modern controller style: a floating glass dock with the seek bar on top.</summary>
public class ModernStyleTests
{
    static string? Wait(Mpv m, string prop, Func<string?, bool> ok)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (ok(v)) break; Thread.Sleep(30); }
        return v;
    }

    static string Draw(Mpv m, int w, int h)
    {
        m.Set("user-data/lumina/draw_result", "");
        m.Command("script-message", "lumina-do", "draw-test", $"{w}x{h}");
        return Wait(m, "draw_result", v => !string.IsNullOrEmpty(v)) ?? "no answer";
    }

    static Mpv Open()
    {
        var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-style=modern");
        m.Command("script-message", "lumina-neighbors", "yes", "yes");
        return m;
    }

    static JsonElement Region(Mpv m, string name)
    {
        var json = Wait(m, "regions", v => !string.IsNullOrEmpty(v) && v.Contains("\"" + name + "\""))!.Replace("\\\"", "\"");
        return JsonDocument.Parse(json).RootElement.GetProperty(name);
    }

    [Fact]
    public void ItDrawsWithoutErrorAtEverySize()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Assert.Equal("modern", Wait(m, "style", v => !string.IsNullOrEmpty(v)));
        foreach (var (w, h) in new[] { (1920, 1080), (1366, 820), (1000, 640), (700, 400), (3840, 2160) })
            Assert.Equal("ok", Draw(m, w, h));
    }

    [Fact]
    public void TheDockFloatsAboveTheBottomEdgeAndHoldsEveryControl()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Assert.Equal("ok", Draw(m, 1920, 1080));
        var bar = Region(m, "bar");
        Assert.True(bar.GetProperty("y").GetDouble() + bar.GetProperty("h").GetDouble() < 1080 - 10, "the dock should float clear of the edge");
        foreach (var name in new[] { "play", "back", "fwd", "seek", "vol", "mute", "cc", "audio", "gear", "queue", "fs" })
        {
            var r = Region(m, name);
            Assert.True(r.GetProperty("x").GetDouble() >= bar.GetProperty("x").GetDouble() - 1, name + " starts inside the dock");
            Assert.True(r.GetProperty("x").GetDouble() + r.GetProperty("w").GetDouble() <= bar.GetProperty("x").GetDouble() + bar.GetProperty("w").GetDouble() + 1, name + " ends inside the dock");
        }
        Assert.Equal(0.0, double.Parse(m.Get("video-margin-ratio-bottom") ?? "0", System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Fact]
    public void ControlsDoNotOverlapWhenTheWindowIsNarrow()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        Assert.Equal("ok", Draw(m, 1000, 640));
        var play = Region(m, "play");
        var fs = Region(m, "fs");
        var gear = Region(m, "gear");
        Assert.True(play.GetProperty("x").GetDouble() + play.GetProperty("w").GetDouble() < gear.GetProperty("x").GetDouble(), "Play ends before the tools begin");
        Assert.True(gear.GetProperty("x").GetDouble() + gear.GetProperty("w").GetDouble() <= fs.GetProperty("x").GetDouble() + 1);
    }

    [Fact]
    public void HoldingTheMouseOnTheSeekBarScrubs()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Set("pause", "yes");
        Assert.Equal("ok", Draw(m, 1920, 1080));
        var s = Region(m, "seek");
        double x0 = s.GetProperty("x").GetDouble(), w = s.GetProperty("w").GetDouble(), y = s.GetProperty("y").GetDouble() + s.GetProperty("h").GetDouble() / 2;
        double Pos() => double.Parse(m.Get("time-pos") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        m.Command("mouse", ((int)(x0 + w * 0.2)).ToString(), ((int)y).ToString());
        m.Command("keydown", "MBTN_LEFT");
        Thread.Sleep(200);
        var first = Pos();
        m.Command("mouse", ((int)(x0 + w * 0.8)).ToString(), ((int)y).ToString());
        Thread.Sleep(400);
        var held = Pos();
        m.Command("keyup", "MBTN_LEFT");
        Thread.Sleep(500);
        // the drag shows the picture at the nearest keyframe; letting go lands exactly where the pointer is
        Assert.InRange(Pos(), 12.0 * 0.8 - 0.8, 12.0 * 0.8 + 0.8);
    }

    [Fact]
    public void ThePanelButtonsStillOpenTheirTabs()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        foreach (var (button, tab) in new[] { ("cc", "subs"), ("audio", "audio"), ("gear", "playback"), ("queue", "queue") })
        {
            m.Command("script-message", "lumina-do", "click", button);
            Assert.Equal(tab, Wait(m, "panel", v => v == tab));
            m.Command("script-message", "lumina-do", "click", button);
            Assert.Equal("", Wait(m, "panel", v => v == ""));
        }
        Assert.Equal("ok", Draw(m, 1920, 1080));
    }

    [Fact]
    public void TheSeekBarPreviewCanBeSwitchedOffFromThePlayerPanel()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open();
        m.Command("script-message", "lumina-do", "click", "gear");
        Assert.Equal("playback", Wait(m, "panel", v => v == "playback"));
        Assert.Equal("ok", Draw(m, 1920, 1080));
        var rows = Wait(m, "panel_rows", v => v != null && v.Contains("Seek-bar preview")) ?? "";
        Assert.Contains("Seek-bar preview", rows);
        m.Command("script-message", "lumina-thumbs-state", "no");
        Assert.Equal("no", Wait(m, "thumbs", v => v == "no"));
        m.Command("script-message", "lumina-thumbs-state", "yes");
        Assert.Equal("yes", Wait(m, "thumbs", v => v == "yes"));
    }
}
