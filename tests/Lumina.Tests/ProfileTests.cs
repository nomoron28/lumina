using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Lumina.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class ProfileTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-prof-" + Guid.NewGuid().ToString("N"));
    readonly string? _oldDb = Environment.GetEnvironmentVariable("LUMINA_DB");
    readonly string? _oldCfg = Environment.GetEnvironmentVariable("LUMINA_CONFIG");

    public ProfileTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "config.json"));
        Db.Init();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("LUMINA_DB", _oldDb);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", _oldCfg);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void EveryBuiltInProfileUsesRealSettingsWithValidValues()
    {
        foreach (var name in Profiles.BuiltIn)
            foreach (var (k, v) in Profiles.Values(name)!)
            {
                var d = Profiles.Scope.SingleOrDefault(s => s.Key == k);
                Assert.True(d != null, $"{name}: {k} is not a profile setting");
                Assert.Equal(AppSettings.Normalize(d!, v), v.Trim());   // survives the clean-up unchanged, so it is a legal value
            }
    }

    [Fact]
    public void ScopeLeavesOutSecretsDevicesAndButtons() =>
        Assert.DoesNotContain(Profiles.Scope, d => d.Kind is Kind.Button or Kind.Secret || d.Key is "audio.device" || d.Category is "accounts" or "network");

    [Fact]
    public void ApplyThenUndoRestoresTheExactPriorFile()
    {
        AppSettings.Set(new Dictionary<string, string> { ["video.brightness"] = "20", ["subtitles.size"] = "60", ["tmdbKey"] = "keep-me" });
        var before = File.ReadAllText(Config_File);
        var changed = Profiles.Apply("Night");
        Assert.Contains("audio.night", changed);
        Assert.Equal("true", AppSettings.Get("audio.night"));
        Assert.Equal("-10", AppSettings.Get("video.brightness"));
        Assert.Equal("46", AppSettings.Get("subtitles.size"));     // not in Night: back to default
        Assert.Equal("keep-me", Config.Raw("tmdbKey"));            // outside the scope: untouched
        Assert.Equal("Night", Profiles.UndoName());

        Assert.True(Profiles.Undo());
        Assert.Equal("20", AppSettings.Get("video.brightness"));
        Assert.Equal("60", AppSettings.Get("subtitles.size"));
        Assert.Equal("false", AppSettings.Get("audio.night"));
        Assert.Equal("", Config.Raw("audio.night"));               // unset stays unset
        Assert.Equal(Canon(before), Canon(File.ReadAllText(Config_File)));
        Assert.False(Profiles.Undo());                             // only once
    }

    [Fact]
    public void ApplyingAProfileTwiceChangesNothingTheSecondTime()
    {
        Profiles.Apply("Anime");
        Assert.Empty(Profiles.Apply("Anime"));
    }

    [Fact]
    public void SavedProfilesCaptureOnlyNonDefaultsAndReapplyExactly()
    {
        AppSettings.Set(new Dictionary<string, string> { ["video.contrast"] = "15", ["audio.eq3"] = "4" });
        Profiles.Save("Mine");
        Assert.Equal(new[] { "audio.eq3", "video.contrast" }, Profiles.Values("Mine")!.Keys.OrderBy(k => k));
        Profiles.Apply("Movies");
        Assert.Equal("0", AppSettings.Get("video.contrast"));
        Profiles.Apply("Mine");
        Assert.Equal("15", AppSettings.Get("video.contrast"));
        Assert.Equal("4", AppSettings.Get("audio.eq3"));
        Assert.Contains("Mine", Profiles.All);
        Profiles.Delete("Mine");
        Assert.DoesNotContain("Mine", Profiles.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Movies")]
    public void BadNamesAreRefused(string name) => Assert.Throws<ArgumentException>(() => Profiles.Save(name));

    [Fact]
    public void ExportImportRoundTripsAndCleansHostileInput()
    {
        AppSettings.Set("subtitles.size", "70");
        Profiles.Save("Big");
        var json = Profiles.Export("Big");
        Profiles.Delete("Big");
        Assert.Equal("Big", Profiles.Import(json));
        Assert.Equal("70", Profiles.Values("Big")!["subtitles.size"]);

        var evil = """{"lumina_profile":1,"name":"Movies","values":{"tmdbKey":"x","audio.device":"y","subtitles.size":"9999","mpvOptions":"z","nope":"1"}}""";
        var name = Profiles.Import(evil);
        Assert.Equal("Movies (imported)", name);                    // cannot shadow a built-in
        var v = Profiles.Values(name)!;
        Assert.Equal(new[] { "subtitles.size" }, v.Keys);
        Assert.Equal("90", v["subtitles.size"]);                    // clamped to the slider
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"lumina_profile":1,"values":{}}""")]
    [InlineData("""{"lumina_profile":1,"name":"x","values":[]}""")]
    public void NonProfilesAreRejected(string json) => Assert.Throws<FormatException>(() => Profiles.Import(json));

    static string Config_File => Environment.GetEnvironmentVariable("LUMINA_CONFIG")!;
    static string Canon(string json) => string.Join("|", System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject().OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));   // order of keys does not matter
}
