using System.IO;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

public class TmdbParseTests
{
    static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void MovieHit()
    {
        var m = Tmdb.ParseFirstHit(J("""{"results":[{"id":603,"title":"The Matrix","overview":"o","poster_path":"/p.jpg","backdrop_path":null,"vote_average":8.2,"release_date":"1999-03-30"},{"id":1,"title":"x"}]}"""), false)!;
        Assert.Equal(("movie", 603L, "The Matrix", "/p.jpg", 1999), (m.Kind, m.TmdbId, m.Title, m.PosterPath, m.Year));
        Assert.Null(m.BackdropPath);
        Assert.Equal(8.2, m.Rating);
    }

    [Fact]
    public void ShowHitAndEmpty()
    {
        var m = Tmdb.ParseFirstHit(J("""{"results":[{"id":1396,"name":"Breaking Bad","first_air_date":"2008-01-20"}]}"""), true)!;
        Assert.Equal(("show", "Breaking Bad", 2008), (m.Kind, m.Title, m.Year));
        Assert.Null(Tmdb.ParseFirstHit(J("""{"results":[]}"""), true));
    }

    [Fact]
    public void Season()
    {
        var eps = Tmdb.ParseSeason(J("""{"episodes":[{"episode_number":1,"name":"Pilot","overview":"","still_path":"/s.jpg"},{"episode_number":2,"name":"Cats in the Bag"}]}"""));
        Assert.Equal(2, eps.Count);
        Assert.Equal("Pilot", eps[0].Name);
        Assert.Null(eps[0].Overview); // empty string -> null
        Assert.Equal(2, eps[1].Number);
    }
}

[Collection("db")]
public class MetaDbTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-meta-" + Guid.NewGuid().ToString("N"));

    public MetaDbTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    string Lib(params string[] files)
    {
        var lib = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(lib);
        foreach (var f in files) File.WriteAllText(Path.Combine(lib, f), "x");
        Db.Init();
        Db.AddRoot(lib);
        Scanner.ScanAll();
        return lib;
    }

    [Fact]
    public void MatchFillsListAndPendingShrinks()
    {
        Lib("The Matrix (1999).mkv", "Show.S01E01.mkv", "Show.S01E02.mkv", "Show.S01E03.mkv");
        Assert.Equal(2, Db.PendingTitles().Count); // one movie, one show, however many episodes

        Db.SetMatch("movie", "The Matrix", 1999, new Meta("movie", 603, "The Matrix", "ov", "/p.jpg", null, 8.2, 1999));
        Db.SetMatch("show", "Show", null, new Meta("show", 7, "Show Canonical", null, null, null, 0, 2008));
        Assert.Empty(Db.PendingTitles());

        Db.SetSeason(7, 1, new() { new Ep(1, "Pilot", null, null), new Ep(2, "Second", null, null) });
        Assert.Empty(Db.SeasonsNeedingEpisodes()); // episode 3 got a blank row, so no refetch loop

        var items = Db.List();
        var movie = items.Single(i => i.Kind == "movie");
        Assert.Equal("The Matrix", movie.Title);
        Assert.Equal("ov", movie.Overview);
        Assert.Null(movie.Poster); // not downloaded yet

        Directory.CreateDirectory(Db.ArtDir);
        File.WriteAllText(Db.ArtPath("/p.jpg"), "img");
        Assert.NotNull(Db.List().Single(i => i.Kind == "movie").Poster);

        var eps = items.Where(i => i.Kind == "show").OrderBy(i => i.Episode).ToList();
        Assert.All(eps, e => Assert.Equal("Show Canonical", e.Title));
        Assert.Equal("S01E01 · Pilot", eps[0].Sub);
        Assert.Equal("S01E03", eps[2].Sub); // no TMDB name -> plain
    }

    [Fact]
    public void NoMatchIsRememberedAndRenameRequeues()
    {
        var lib = Lib("Obscure.Thing.2001.mkv");
        Db.SetMatch("movie", "Obscure Thing", 2001, null);
        Assert.Empty(Db.PendingTitles()); // tmdb_id=0: not searched again every launch

        File.Move(Path.Combine(lib, "Obscure.Thing.2001.mkv"), Path.Combine(lib, "Obscure Thing Extended Cut 2001.mkv"));
        Scanner.ScanAll();
        Assert.Single(Db.PendingTitles()); // new path -> new row -> searched
    }
}

[Collection("db")]
public class TmdbLiveTest
{
    // Real TMDB call. Runs only when TMDB_KEY is in the environment, so no key lives in the repo.
    [Fact]
    public async Task EndToEnd()
    {
        var key = Environment.GetEnvironmentVariable("TMDB_KEY");
        if (string.IsNullOrEmpty(key)) return;

        var dir = Path.Combine(Path.GetTempPath(), "lumina-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        var omdb = Environment.GetEnvironmentVariable("OMDB_KEY");
        File.WriteAllText(Path.Combine(dir, "cfg.json"), "{\"tmdbKey\":\"" + key + "\"" + (string.IsNullOrEmpty(omdb) ? "" : ",\"omdbKey\":\"" + omdb + "\"") + "}");
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(dir, "cfg.json"));
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(dir, "art"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "lib", "The.Matrix.1999.1080p.BluRay.mkv"), "x");
            File.WriteAllText(Path.Combine(dir, "lib", "Breaking.Bad.S01E01.720p.mkv"), "x");
            Db.Init();
            Db.AddRoot(Path.Combine(dir, "lib"));
            Scanner.ScanAll();

            var err = await Tmdb.FillAsync(_ => { });
            Assert.Equal("", err);

            var items = Db.List();
            var movie = items.Single(i => i.Kind == "movie");
            Assert.Equal("The Matrix", movie.Title);
            Assert.NotNull(movie.Poster);
            Assert.True(new FileInfo(movie.Poster!).Length > 1000);
            // details: runtime, certificate, genres, logo, cast photos (and scores when an OMDb key is given)
            var d = Db.GetDetail("movie", movie.TmdbId)!;
            Assert.True(d.Runtime > 60);
            Assert.NotEmpty(d.Genres);
            Assert.True(d.Cast.Count > 3);
            Assert.Equal("Director", d.Cast[0].Role);
            Assert.True(d.Cast.Where(c => c.Photo != null).All(c => File.Exists(Db.ArtPath(c.Photo!))));
            if (d.Logo != null) Assert.True(File.Exists(Db.ArtPath(d.Logo)));
            if (!string.IsNullOrEmpty(omdb)) { Assert.NotNull(d.ImdbId); Assert.True(Db.GetRatings("movie", movie.TmdbId).Any); }
            var showDetail = Db.GetDetail("show", items.Single(i => i.Kind == "show").TmdbId)!;
            Assert.NotEmpty(showDetail.Genres);

            var ep = items.Single(i => i.Kind == "show");
            Assert.Equal("Breaking Bad", ep.Title);
            Assert.Equal("S01E01 · Pilot", ep.Sub);
            Assert.NotNull(ep.Poster);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUMINA_CONFIG", null);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
