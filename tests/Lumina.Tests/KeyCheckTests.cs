using System.IO;
using System.Text.Json;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>The Welcome tour's Test buttons, against a local fake of TMDB, OMDb and OpenSubtitles.</summary>
[Collection("db")]
public class KeyCheckTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-keys-" + Guid.NewGuid().ToString("N"));
    readonly string? _oldCfg = Environment.GetEnvironmentVariable("LUMINA_CONFIG");
    readonly FakeWeb _web = new();

    public KeyCheckTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "cfg.json"));
        Environment.SetEnvironmentVariable("LUMINA_TMDB_API", _web.Base + "3/");
        Environment.SetEnvironmentVariable("LUMINA_OMDB_API", _web.Base + "omdb/");
        Environment.SetEnvironmentVariable("LUMINA_OPENSUBTITLES_API", _web.Base + "os/");
        // a key is good when it is "good"; each fake answers the way the real service does for a bad one
        _web.Handler = r =>
        {
            var good = (r.Url!.Query.Contains("good") || r.Headers["Api-Key"] == "good");
            return good ? (200, "{}", 0) : (401, """{"status_message":"Invalid API key","Response":"False"}""", 0);
        };
    }

    public void Dispose()
    {
        _web.Dispose();
        foreach (var v in new[] { "LUMINA_TMDB_API", "LUMINA_OMDB_API", "LUMINA_OPENSUBTITLES_API" }) Environment.SetEnvironmentVariable(v, null);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", _oldCfg);
        Directory.Delete(_dir, true);
    }

    void Key(string name, string value) => File.WriteAllText(Path.Combine(_dir, "cfg.json"), JsonSerializer.Serialize(new Dictionary<string, string> { [name] = value }));

    static KeyCheck.Service Svc(string key) => KeyCheck.Services.Single(s => s.Key == key);

    [Theory]
    [InlineData("tmdbKey", "TMDB")]
    [InlineData("omdbKey", "OMDb")]
    [InlineData("openSubtitlesKey", "OpenSubtitles")]
    public async Task AGoodKeyWorksAndABadOneIsNamed(string key, string name)
    {
        Key(key, "good");
        Assert.Null(await KeyCheck.TestAsync(Svc(key)));
        Key(key, "bad");
        Assert.Equal($"{name} did not accept this key", await KeyCheck.TestAsync(Svc(key)));
    }

    [Fact]
    public async Task NoKeyAsksForOneAndNothingIsSent()
    {
        Key("other", "x");
        Assert.Equal("Paste a key first", await KeyCheck.TestAsync(Svc("tmdbKey")));
        Assert.Empty(_web.Requests);
    }

    [Fact]
    public async Task AServerErrorIsNotCalledABadKey()
    {
        _web.Handler = _ => (503, "{}", 0);
        Key("omdbKey", "good");
        Assert.Equal("OMDb answered with error 503. Try again later", await KeyCheck.TestAsync(Svc("omdbKey")));
    }

    [Fact]
    public async Task AnUnreachableServiceSaysSo()
    {
        Environment.SetEnvironmentVariable("LUMINA_TMDB_API", "http://127.0.0.1:9/3/");   // the discard port: nothing listens there
        Key("tmdbKey", "good");
        Assert.Equal("Could not reach TMDB. Check your connection", await KeyCheck.TestAsync(Svc("tmdbKey")));
    }
}
