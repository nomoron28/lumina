using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>
/// Reported on Reddit: "404 (Not Found)", summaries and episode names arrive but no posters at all. A season TMDB does not
/// list (its numbering differs from the files') answered 404, the error ended the whole pass, and the poster step never ran.
/// </summary>
[Collection("db")]
public class TmdbFillTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-fill-" + Guid.NewGuid().ToString("N"));
    readonly FakeWeb _web = new();
    readonly string? _oldCfg = Environment.GetEnvironmentVariable("LUMINA_CONFIG");

    public TmdbFillTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "lib"));
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), """{"tmdbKey":"good"}""");
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "cfg.json"));
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
        Environment.SetEnvironmentVariable("LUMINA_TMDB_API", _web.Base + "3/");
        Environment.SetEnvironmentVariable("LUMINA_TMDB_IMG", _web.Base + "img/");
        Db.Init();
    }

    public void Dispose()
    {
        _web.Dispose();
        foreach (var v in new[] { "LUMINA_TMDB_API", "LUMINA_TMDB_IMG" }) Environment.SetEnvironmentVariable(v, null);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", _oldCfg);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task ASeasonTmdbDoesNotKnowDoesNotStopThePosters()
    {
        var lib = Path.Combine(_dir, "lib");
        File.WriteAllText(Path.Combine(lib, "Show S01E01.mkv"), "x");
        File.WriteAllText(Path.Combine(lib, "Show S05E01.mkv"), "x");      // a season TMDB numbers differently
        Db.AddRoot(lib);
        Scanner.ScanAll();
        Db.SetMatch("show", "Show", null, new Meta("show", 7, "Show", "About it", "/poster.jpg", "/back.jpg", 8.0, 2010));

        _web.Handler = r => r.Url!.AbsolutePath switch
        {
            "/3/tv/7/season/1" => (200, """{"episodes":[{"episode_number":1,"name":"Pilot"}]}""", 0),
            "/3/tv/7/season/5" => (404, """{"status_message":"The resource you requested could not be found."}""", 0),
            var p when p.StartsWith("/img/") => (200, "image-bytes", 0),
            _ => (404, "{}", 0),
        };

        Assert.Equal("", await Tmdb.FillAsync(_ => { }));
        Assert.True(File.Exists(Db.ArtPath("/poster.jpg")), "the poster was downloaded");
        Assert.True(File.Exists(Db.ArtPath("/back.jpg")), "the backdrop was downloaded");
        Assert.Equal("Pilot", Db.List().Single(i => i.Season == 1).EpisodeName);
    }
}
