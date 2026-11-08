using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class HdrPanelTests
{
    static string? Wait(Mpv m, string prop, Func<string?, bool> ok)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (ok(v)) break; Thread.Sleep(40); }
        return v;
    }

    static string State(Mpv m, string action, Func<string, bool> ok)
    {
        m.Command("script-message", "lumina-do", action);
        return Wait(m, "hdr_state", v => v != null && ok(v)) ?? "";
    }

    [Fact]
    public void ToneMappingStepsThroughEveryChoiceAndWrapsAndMpvAcceptsAll()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("hdr10.mkv");
        var expected = new[] { "auto", "bt.2390", "bt.2446a", "spline", "hable", "mobius", "reinhard", "clip" };
        m.Set("tone-mapping", "auto");
        foreach (var next in expected.Skip(1).Append("auto"))
        {
            var s = State(m, "hdr-tonemap", v => v.StartsWith(next + "|"));
            Assert.StartsWith(next + "|", s);
            Assert.Equal(next, m.Get("tone-mapping"));
        }
    }

    [Fact]
    public void PeakBrightnessStepsThroughTheCommonLevels()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("hdr10.mkv");
        m.Set("target-peak", "auto");
        foreach (var next in new[] { "100", "203", "400", "600", "1000", "auto" })
        {
            State(m, "hdr-peak", v => v.Split('|')[1] == next);
            Assert.Equal(next, m.Get("target-peak"));
        }
    }

    [Fact]
    public void PeakDetectionAndTheDisplayHintCycleAutoYesNo()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("hdr10.mkv");
        m.Set("hdr-compute-peak", "auto"); m.Set("target-colorspace-hint", "auto");
        foreach (var next in new[] { "yes", "no", "auto" })
        {
            State(m, "hdr-computepeak", v => v.Split('|')[2] == next);
            Assert.Equal(next, m.Get("hdr-compute-peak"));
            State(m, "hdr-hint", v => v.Split('|')[3] == next);
            Assert.Equal(next, m.Get("target-colorspace-hint"));
        }
    }

    [Fact]
    public void AValueSetElsewhereThatIsNotInTheListGoesToTheFirstEntryNotTheSecond()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("hdr10.mkv");
        m.Set("tone-mapping", "gamma");                                   // valid in mpv, not offered in the panel
        State(m, "hdr-tonemap", v => v.StartsWith("auto|"));
        Assert.Equal("auto", m.Get("tone-mapping"));
    }

    [Fact]
    public void BackToAutomaticResetsAllFourAndTheSignalIsNamedFromTheFile()
    {
        if (!Fixtures.HasMpv) return;
        using var hdr = Fixtures.Open("hdr10.mkv");
        Assert.Equal("pq", Fixtures.Eventually(hdr, "video-params/gamma"));       // decoder facts appear a moment after the file loads
        hdr.Set("tone-mapping", "clip"); hdr.Set("target-peak", "400"); hdr.Set("hdr-compute-peak", "no"); hdr.Set("target-colorspace-hint", "yes");
        State(hdr, "hdr-reset", v => v == "auto|auto|auto|auto");
        Assert.Equal("auto|auto|auto|auto", hdr.Get("user-data/lumina/hdr_state")?.Trim('"'));
        Assert.Equal("HDR10", Wait(hdr, "hdr_signal", v => v == "HDR10"));

        using var sdr = Fixtures.Open("multi.mkv");
        Assert.NotNull(Fixtures.Eventually(sdr, "video-params/gamma"));
        State(sdr, "hdr-open", v => v.Length > 0);
        Assert.Equal("SDR", Wait(sdr, "hdr_signal", v => v == "SDR"));
    }
}

public class GearPanelScrollTests
{
    [Fact]
    public void ScrollingByNonsenseOrFarPastTheEndIsHarmless()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        // the panel clamps its scroll position when it draws, so a wild value cannot break it
        m.Command("script-message", "lumina-do", "gear-scroll", "banana");
        m.Command("script-message", "lumina-do", "gear-scroll", "-50");
        m.Command("script-message", "lumina-do", "gear-scroll", "999");
        Thread.Sleep(300);
        Assert.Equal("yes", m.Get("user-data/lumina/ready")?.Trim('"'));
    }
}
