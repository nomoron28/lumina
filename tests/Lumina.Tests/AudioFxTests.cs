using System.Diagnostics;
using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class AudioFxTests
{
    static readonly double[] Zero = new double[10];

    static AudioFxSettings S(double[]? bands = null, bool eq = true, string comp = "off", string spatial = "off", double width = 0, bool norm = false, bool night = false) =>
        new(bands ?? Zero, eq, comp, spatial, width, norm, night);

    [Fact]
    public void EverythingOffGivesNoFilterAtAll()
    {
        Assert.Null(AudioFx.Graph(S()));
        Assert.Equal("", AudioFx.Compose(S()));
    }

    [Fact]
    public void FlatBandsAddNothingEvenWhenTheEqualizerIsOn() => Assert.Null(AudioFx.Graph(S(Zero, eq: true)));

    [Fact]
    public void BandsAreIgnoredWhenTheEqualizerIsOff() => Assert.Null(AudioFx.Graph(S(new double[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 }, eq: false)));

    [Fact]
    public void OnlyChangedBandsBecomeFiltersAtTheirOwnFrequencies()
    {
        var g = AudioFx.Graph(S(new double[] { 0, 0, 0, 0, 0, 3, 0, 0, 0, -4 }))!;
        Assert.Contains("equalizer=f=1000:t=o:w=1:g=3", g);
        Assert.Contains("equalizer=f=16000:t=o:w=1:g=-4", g);
        Assert.Equal(2, g.Split("equalizer").Length - 1);
    }

    [Fact]
    public void BoostsAreOffsetByHeadroomSoTheResultCannotClip()
    {
        var g = AudioFx.Graph(S(new double[] { 6, 0, 0, 0, 0, 0, 0, 0, 0, 2 }))!;
        Assert.StartsWith("volume=-6dB,", g);                       // the biggest boost
        Assert.DoesNotContain("volume", AudioFx.Graph(S(new double[] { -3, 0, 0, 0, 0, 0, 0, 0, 0, -1 }))!);   // cuts need no headroom
    }

    [Fact]
    public void GainsOutsideTheRangeAreClamped()
    {
        var g = AudioFx.Graph(S(new double[] { 99, -99, 0, 0, 0, 0, 0, 0, 0, 0 }))!;
        Assert.Contains("g=12", g);
        Assert.Contains("g=-12", g);
        Assert.DoesNotContain("99", g);
    }

    [Fact]
    public void ShortBandListsAreTreatedAsZeroBeyondTheirLength() =>
        Assert.Contains("f=31", AudioFx.Graph(S(new double[] { 2 }))!);

    [Theory]
    [InlineData("gentle", "ratio=2")]
    [InlineData("medium", "ratio=4")]
    [InlineData("strong", "ratio=8")]
    public void CompressorLevelsAreDistinct(string level, string expected) => Assert.Contains(expected, AudioFx.Graph(S(comp: level))!);

    [Theory]
    [InlineData("headphones", "crossfeed")]
    [InlineData("room", "aecho=0.8:0.88:60")]
    [InlineData("hall", "aecho=0.8:0.9:1000|1800")]
    public void SpatialModes(string mode, string expected) => Assert.Contains(expected, AudioFx.Graph(S(spatial: mode))!);

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(50, "extrastereo=m=2.5")]
    [InlineData(100, "extrastereo=m=4")]
    [InlineData(500, "extrastereo=m=4")]
    public void StereoWidthMapsToAMultiplierAndClamps(double width, string? expected)
    {
        var g = AudioFx.Graph(S(width: width));
        if (expected == null) Assert.Null(g); else Assert.Equal(expected, g);
    }

    [Fact]
    public void UnknownLevelsAreIgnoredNotFatal() => Assert.Null(AudioFx.Graph(S(comp: "bogus", spatial: "nonsense")));

    [Fact]
    public void FiltersAppearInASensibleOrderEqualizerBeforeEffectsBeforeLoudness()
    {
        var g = AudioFx.Graph(S(new double[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, comp: "gentle", spatial: "room", width: 40, norm: true, night: true))!;
        var order = new[] { "volume=", "equalizer=", "acompressor=threshold=0.1", "aecho", "extrastereo", "dynaudnorm", "acompressor=threshold=0.063" };
        var at = order.Select(o => g.IndexOf(o, StringComparison.Ordinal)).ToArray();
        Assert.All(at, i => Assert.True(i >= 0));
        Assert.Equal(at.OrderBy(i => i), at);
    }

    [Fact]
    public void ComposeWrapsTheGraphForMpv()
    {
        var c = AudioFx.Compose(S(norm: true));
        Assert.StartsWith("lavfi=[", c);
        Assert.EndsWith("]", c);
    }

    [Fact]
    public void EveryPresetHasTenGainsInRangeAndNamesMatchTheDropdown()
    {
        foreach (var (name, gains) in AudioFx.Presets)
        {
            Assert.Equal(AudioFx.BandHz.Length, gains.Length);
            Assert.All(gains, g => Assert.InRange(g, -12, 12));
            Assert.Contains(AudioFx.PresetChoices, c => c.Value == name);
        }
        Assert.Contains(AudioFx.PresetChoices, c => c.Value == "off");
        Assert.Contains(AudioFx.PresetChoices, c => c.Value == "custom");
        Assert.All(AudioFx.PresetChoices.Where(c => c.Value is not ("off" or "custom")), c => Assert.True(AudioFx.Presets.ContainsKey(c.Value)));
    }

    [Fact]
    public void NextCyclesAndWraps()
    {
        Assert.Equal("gentle", AudioFx.Next(AudioFx.CompressorChoices, "off"));
        Assert.Equal("off", AudioFx.Next(AudioFx.CompressorChoices, "strong"));
        Assert.Equal("off", AudioFx.Next(AudioFx.CompressorChoices, "unknown"));
    }
}

/// <summary>
/// Proves the chains are real filter graphs by running them through ffmpeg's own libavfilter. This test is the safety net:
/// mpv accepts any text for <c>af</c> without complaint and then quietly skips a chain it cannot build, so a typo would
/// otherwise show up only as an effect that does nothing.
/// </summary>
public class AudioFxValidityTests
{
    static string? Ffmpeg => new[] { Environment.GetEnvironmentVariable("LUMINA_FFMPEG"), @"C:\ffmpeg\bin\ffmpeg.exe" }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));

    static IEnumerable<AudioFxSettings> Matrix()
    {
        var presets = AudioFx.Presets.Values.Append(new double[] { 12, 12, 12, 12, 12, 12, 12, 12, 12, 12 }).Append(new double[] { -12, -12, -12, -12, -12, -12, -12, -12, -12, -12 });
        foreach (var bands in presets) yield return new(bands, true, "off", "off", 0, false, false);
        foreach (var c in new[] { "gentle", "medium", "strong" }) yield return new(new double[10], false, c, "off", 0, false, false);
        foreach (var sp in new[] { "headphones", "room", "hall" }) yield return new(new double[10], false, "off", sp, 0, false, false);
        foreach (var w in new double[] { 5, 50, 100 }) yield return new(new double[10], false, "off", "off", w, false, false);
        yield return new(new double[10], false, "off", "off", 0, true, false);
        yield return new(new double[10], false, "off", "off", 0, false, true);
        yield return new(AudioFx.Presets["rock"], true, "strong", "hall", 100, true, true);   // everything at once
    }

    [Fact]
    public void EveryCombinationIsAValidFfmpegGraph()
    {
        if (Ffmpeg is not { } ff) return;
        foreach (var s in Matrix())
        {
            var graph = AudioFx.Graph(s)!;
            var psi = new ProcessStartInfo(ff) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=0.3", "-af", graph, "-f", "null", "-" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            Assert.True(p.ExitCode == 0 && err.Trim().Length == 0, $"ffmpeg rejected:\n{graph}\n{err}");
        }
    }

    [Fact]
    public void EveryCombinationIsAcceptedByMpvAndAudioKeepsPlaying()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Fixtures.Eventually(m, "audio-params/samplerate");
        foreach (var s in Matrix())
        {
            m.Set("af", AudioFx.Compose(s));
            Thread.Sleep(120);
            Assert.False(string.IsNullOrEmpty(m.Get("audio-out-params/samplerate")) && string.IsNullOrEmpty(m.Get("audio-params/samplerate")), "audio chain died for " + AudioFx.Graph(s));
            Assert.Contains("lavfi", m.Get("af") ?? "");
        }
        m.Set("af", "");                                   // clearing works too
        Assert.Equal("", m.Get("af"));
    }
}
