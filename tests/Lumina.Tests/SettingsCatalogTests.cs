using System.IO;
using System.Globalization;
using Lumina.Settings;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class SettingsCatalogTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), "lumina-settings-" + Guid.NewGuid().ToString("N") + ".json");
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_CONFIG");

    public SettingsCatalogTests() => Environment.SetEnvironmentVariable("LUMINA_CONFIG", _file);
    public void Dispose() { Environment.SetEnvironmentVariable("LUMINA_CONFIG", _old); File.Delete(_file); }

    [Fact]
    public void CatalogIsConsistent()
    {
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(d => d.Key).Distinct().Count());
        var cats = Catalog.Categories.Select(c => c.Id).ToHashSet();
        foreach (var d in Catalog.All)
        {
            Assert.Contains(d.Category, cats);
            if (d.Choices is { } cs) Assert.Contains(d.Default, cs.Select(c => c.Value));
            if (d.Kind == Kind.Slider)
            {
                var n = double.Parse(d.Default, CultureInfo.InvariantCulture);
                Assert.InRange(n, d.Min, d.Max);
            }
            if (d.Kind is Kind.Choice or Kind.Segmented) Assert.NotNull(d.Choices);
        }
        Assert.All(Catalog.Categories, c => Assert.NotEmpty(Catalog.In(c.Id)));
    }

    [Fact]
    public void DefaultsEmitNothingAndDefaultValuesAreNotStored()
    {
        Assert.Equal(new[] { "screenshot-directory", "script-opts" }, AppSettings.MpvLines().Select(l => l.Split('=')[0])); // snapshots default to Pictures\Lumina; the controller style (vlc) is the one default the player script does not share
        AppSettings.Set("video.brightness", "0");
        Assert.Equal("", Lumina.Metadata.Config.Raw("video.brightness"));
    }

    [Fact]
    public void ChangedValuesMapToMpvAndAreClamped()
    {
        AppSettings.Set("video.brightness", "500");
        AppSettings.Set("subtitles.color", "#ffcc00");
        AppSettings.Set("playback.loop", "bogus");
        var lines = AppSettings.MpvLines();
        Assert.Contains("brightness=100", lines);
        Assert.Contains("sub-color=#FFCC00", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("loop-file"));
    }

    [Fact]
    public void AudioFiltersAreCombinedIntoOneLine()
    {
        AppSettings.Set("audio.normalize", "true");
        AppSettings.Set("audio.night", "true");
        var af = Assert.Single(AppSettings.MpvLines(), l => l.StartsWith("af="));
        Assert.Contains("dynaudnorm", af);
        Assert.Contains("acompressor", af);
    }

    [Fact]
    public void OscOptionsShareOneScriptOptsLineAndAdvancedWinsLast()
    {
        AppSettings.Set("interface.hideAfter", "4");
        AppSettings.Set("controls.skip", "30");
        AppSettings.Set("mpvOptions", "brightness=5");
        var lines = AppSettings.MpvLines();
        Assert.Contains("script-opts=lumina-osc-hide_ms=4000,lumina-osc-skip=30,lumina-osc-style=modern", lines);
        Assert.EndsWith("brightness=5", AppSettings.MpvExtra());
    }

    [Fact]
    public void EveryMappedSettingIsAcceptedByMpvWhenChanged()
    {
        if (!Fixtures.HasMpv) return;    // needs libmpv
        var text = new Dictionary<string, string>
        {
            ["playback.alang"] = "ja", ["subtitles.encoding"] = "utf-8", ["subtitles.font"] = "Arial", ["network.proxy"] = "http://127.0.0.1:9",
            ["snapshots.folder"] = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
        };
        var change = new Dictionary<string, string>();
        foreach (var d in Catalog.All.Where(d => d.Mpv != null))
            change[d.Key] = d.Kind switch
            {
                Kind.Toggle => d.Default == "true" ? "false" : "true",
                Kind.Choice or Kind.Segmented => d.Choices!.First(c => c.Value != d.Default).Value,
                Kind.Slider => Catalog.Num(d.Max.ToString(CultureInfo.InvariantCulture) == d.Default ? d.Min : d.Max),
                Kind.Color => "123456",
                _ => text.GetValueOrDefault(d.Key, "x"),
            };
        change["audio.normalize"] = "true";
        change["audio.night"] = "true";
        AppSettings.Set(change);
        var lines = AppSettings.MpvLines();
        Assert.True(lines.Count > 40, lines.Count.ToString());
        using var m = new Lumina.Player.Mpv(IntPtr.Zero, AppContext.BaseDirectory, string.Join(Environment.NewLine, lines) + Environment.NewLine + "vo=null" + Environment.NewLine + "ao=null");
        Assert.Empty(m.BadOptions);
    }

    [Fact]
    public void ChoicesReadAsTheirLabelForScreenReaders()
    {
        // Regression: a dropdown item was announced as "Choice { Value = yes, Label = Always }".
        Assert.All(Catalog.All.Where(d => d.Choices != null).SelectMany(d => d.Choices!), c => Assert.Equal(c.Label, c.ToString()));
    }

    // config.json exactly as the build before the Settings overhaul wrote it. Keys may be added, never renamed or repurposed.
    const string FrozenV1 = "{\"tmdbKey\":\"k\",\"openSubtitlesKey\":\"o\",\"subtitleLanguages\":\"en,es\",\"traktClientId\":\"i\",\"traktClientSecret\":\"s\",\"mpvOptions\":\"brightness=3\",\"somethingFromTheFuture\":\"keep\"}";

    [Fact]
    public void AnOldConfigFileStillLoadsAndUnknownKeysSurviveASave()
    {
        File.WriteAllText(_file, FrozenV1);
        Assert.Equal("k", Lumina.Metadata.Config.TmdbKey);
        Assert.Equal("en,es", Lumina.Metadata.Config.SubtitleLanguages);
        Assert.Equal("5CE1C6", AppSettings.Get("general.accent"));  // new keys fall back to their defaults
        Assert.EndsWith("brightness=3", AppSettings.MpvExtra());     // hand-written options still apply, and still last
        AppSettings.Set("general.accent", "F28C1E");
        var after = File.ReadAllText(_file);
        Assert.Contains("somethingFromTheFuture", after);
        Assert.Contains("\"tmdbKey\": \"k\"", after);
    }

    [Fact]
    public void CustomKeysBecomeAnInputConfBesideTheConfigAndAreHandedToMpv()
    {
        AppSettings.Set("shortcuts", "{\"playpause\":\"k\"}");
        var line = Assert.Single(AppSettings.MpvLines(), l => l.StartsWith("input-conf="));
        var path = line["input-conf=".Length..];
        Assert.Equal(Path.GetDirectoryName(_file), Path.GetDirectoryName(path));
        var text = File.ReadAllText(path);
        Assert.Contains("k cycle pause", text);
        Assert.Contains("SPACE ignore", text);
        Assert.Contains("lumina-subs", text);                      // the shipped bindings are carried along, not replaced
    }

    [Fact]
    public void NoCustomKeysMeansNoGeneratedFileAtAll() => Assert.DoesNotContain(AppSettings.MpvLines(), l => l.StartsWith("input-conf="));

    [Fact]
    public void ClashingHandEditedKeysAreIgnoredRatherThanHalfApplied()
    {
        AppSettings.Set("shortcuts", "{\"quit\":\"m\"}");         // mute still owns m
        Assert.DoesNotContain(AppSettings.MpvLines(), l => l.StartsWith("input-conf="));
    }
}
