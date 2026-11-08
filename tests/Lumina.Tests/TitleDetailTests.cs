using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

public class TitleDetailParsingTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    const string MovieJson = """
    {"runtime":100,"tagline":"Stay together","imdb_id":"tt123",
     "genres":[{"id":1,"name":"Science Fiction"},{"id":2,"name":"Mystery"}],
     "release_dates":{"results":[
        {"iso_3166_1":"GB","release_dates":[{"certification":"12A"}]},
        {"iso_3166_1":"US","release_dates":[{"certification":""},{"certification":"PG-13"}]}]},
     "images":{"logos":[
        {"file_path":"/fr.png","iso_639_1":"fr","vote_average":9},
        {"file_path":"/en-low.png","iso_639_1":"en","vote_average":1},
        {"file_path":"/en-best.png","iso_639_1":"en","vote_average":5}]},
     "credits":{
        "crew":[{"name":"Writer","job":"Writer"},{"name":"David R.","job":"Director","profile_path":"/d.jpg"}],
        "cast":[{"name":"Anne","character":"Denise","profile_path":"/a.jpg"},{"name":"Ewan","character":"Greg"}]}}
    """;

    [Fact]
    public void AMovieGivesRuntimeCertificateGenresLogoAndPeople()
    {
        var d = Tmdb.ParseDetail(J(MovieJson), tv: false);
        Assert.Equal(100, d.Runtime);
        Assert.Equal("PG-13", d.Certification);                           // the US one, skipping the empty entry
        Assert.Equal(new[] { "Science Fiction", "Mystery" }, d.Genres);
        Assert.Equal("Stay together", d.Tagline);
        Assert.Equal("tt123", d.ImdbId);
        Assert.Equal("/en-best.png", d.Logo);                             // English first, then the best-voted
        Assert.Equal(new[] { "David R.", "Anne", "Ewan" }, d.Cast.Select(c => c.Person));   // director leads the row
        Assert.Equal("Director", d.Cast[0].Role);
        Assert.Equal("Denise", d.Cast[1].Role);
        Assert.Null(d.Cast[2].Photo);
    }

    [Fact]
    public void ATvShowUsesEpisodeRuntimeContentRatingExternalIdAndTheCreator()
    {
        var d = Tmdb.ParseDetail(J("""
            {"episode_run_time":[54,60],"genres":[{"name":"Drama"}],
             "content_ratings":{"results":[{"iso_3166_1":"DE","rating":"16"},{"iso_3166_1":"US","rating":"TV-14"}]},
             "external_ids":{"imdb_id":"tt999"},"created_by":[{"name":"Terry","profile_path":"/t.jpg"}],
             "credits":{"cast":[{"name":"Paul","character":"Vision"}]}}
            """), tv: true);
        Assert.Equal(54, d.Runtime);
        Assert.Equal("TV-14", d.Certification);
        Assert.Equal("tt999", d.ImdbId);
        Assert.Equal(new[] { "Terry", "Paul" }, d.Cast.Select(c => c.Person));
        Assert.Equal("Creator", d.Cast[0].Role);
    }

    [Fact]
    public void NonUsCertificateIsBetterThanNone() =>
        Assert.Equal("16", Tmdb.ParseDetail(J("""{"content_ratings":{"results":[{"iso_3166_1":"DE","rating":"16"}]}}"""), true).Certification);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"genres":null,"credits":null,"images":null,"release_dates":null}""")]
    [InlineData("""{"genres":"x","credits":{"cast":"no","crew":3},"images":{"logos":{}},"release_dates":{"results":7},"runtime":"abc"}""")]
    [InlineData("""{"credits":{"cast":[{"character":"no name"},{"name":""}]}}""")]
    public void SparseOrOddJsonGivesEmptyPiecesNotCrashes(string json)
    {
        var d = Tmdb.ParseDetail(J(json), false);
        Assert.Empty(d.Cast);
        Assert.Null(d.Logo);
        Assert.Null(d.Runtime);
    }

    [Fact]
    public void AtMostTwelveActorsAreKept()
    {
        var cast = string.Join(",", Enumerable.Range(1, 30).Select(i => $"{{\"name\":\"P{i}\",\"character\":\"C{i}\"}}"));
        Assert.Equal(12, Tmdb.ParseDetail(J("{\"credits\":{\"cast\":[" + cast + "]}}"), false).Cast.Count);
    }

    [Fact]
    public void EpisodesCarryAirDateRuntimeAndScore()
    {
        var e = Tmdb.ParseSeason(J("""{"episodes":[{"episode_number":1,"name":"A","air_date":"2026-10-13","runtime":54,"vote_average":8.2},{"episode_number":2,"name":"B","vote_average":0}]}"""));
        Assert.Equal(("2026-10-13", 54, 8.2), (e[0].AirDate, e[0].Runtime, e[0].Rating));
        Assert.Equal((null, null, null), (e[1].AirDate, e[1].Runtime, (double?)e[1].Rating));
    }

    [Fact]
    public void OmdbScoresAreFormattedForDisplay()
    {
        var r = Omdb.ParseRatings(J("""{"imdbRating":"6.2","Ratings":[{"Source":"Internet Movie Database","Value":"6.2/10"},{"Source":"Rotten Tomatoes","Value":"86%"},{"Source":"Metacritic","Value":"60/100"}]}"""));
        Assert.Equal(("6.2", "86%"), (r.Imdb, r.RottenTomatoes));
        Assert.Equal("7.1", Omdb.ParseRatings(J("""{"imdbRating":"7.1"}""")).Imdb);                       // no Ratings array
        Assert.False(Omdb.ParseRatings(J("""{"Response":"False","Error":"Incorrect IMDb ID."}""")).Any);
        Assert.False(Omdb.ParseRatings(J("""{"imdbRating":"N/A","Ratings":[]}""")).Any);
    }
}

[Collection("db")]
public class TitleDetailDbTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-detail-" + Guid.NewGuid().ToString("N"));
    readonly string? _oldCfg = Environment.GetEnvironmentVariable("LUMINA_CONFIG");

    public TitleDetailDbTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "cfg.json"));
        Db.Init();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_TMDB_API", null);
        Environment.SetEnvironmentVariable("LUMINA_TMDB_IMG", null);
        Environment.SetEnvironmentVariable("LUMINA_OMDB_API", null);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", _oldCfg);
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    static Meta M(long id = 100, string kind = "movie") => new(kind, id, "Title", "o", "/p.jpg", "/b.jpg", 7.1, 2026);

    void Seed(Meta m)
    {
        File.WriteAllText(Path.Combine(_dir, "Title (2026).mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        Db.SetMatch(m.Kind, "Title", 2026, m);
    }

    [Fact]
    public void DetailRoundTripsAndStopsBeingNeeded()
    {
        Seed(M());
        Assert.Equal(new[] { ("movie", 100L) }, Db.NeedingDetail());
        Assert.Null(Db.GetDetail("movie", 100));
        var d = new Detail(100, "PG-13", new[] { "Mystery", "Thriller" }, "tag", "/logo.png", "tt1", new[] { new Credit("A", "Lead", "/a.jpg"), new Credit("B", "", null) });
        Db.SetDetail("movie", 100, d);
        Assert.Empty(Db.NeedingDetail());
        var got = Db.GetDetail("movie", 100)!;
        Assert.Equal((100, "PG-13", "tag", "/logo.png", "tt1"), (got.Runtime, got.Certification, got.Tagline, got.Logo, got.ImdbId));
        Assert.Equal(d.Genres, got.Genres);
        Assert.Equal(d.Cast, got.Cast);
        Assert.Equal(new[] { "/logo.png" }, Db.LogoPaths());
        Assert.Equal(new[] { "/a.jpg" }, Db.CastPhotoPaths());
    }

    [Fact]
    public void SettingDetailAgainReplacesTheCastRatherThanAppendingToIt()
    {
        Seed(M());
        Db.SetDetail("movie", 100, Detail.Empty with { Cast = new[] { new Credit("A", "", null), new Credit("B", "", null) } });
        Db.SetDetail("movie", 100, Detail.Empty with { Cast = new[] { new Credit("C", "", null) } });
        Assert.Equal(new[] { "C" }, Db.GetDetail("movie", 100)!.Cast.Select(c => c.Person));
    }

    [Fact]
    public void AnEmptyDetailMeansTheTitleIsDoneNotMissing()
    {
        Seed(M());
        Db.SetDetail("movie", 100, Detail.Empty);
        Assert.Empty(Db.NeedingDetail());
        Assert.NotNull(Db.GetDetail("movie", 100));
    }

    [Fact]
    public void ScoresStoreAndAnEmptySetStopsTheAsking()
    {
        Seed(M());
        Db.SetDetail("movie", 100, Detail.Empty with { ImdbId = "tt1" });
        Assert.Single(Db.NeedingRatings());
        Db.SetRatings("movie", 100, new Ratings("6.2", null));
        Assert.Empty(Db.NeedingRatings());
        Assert.Equal(("6.2", null), (Db.GetRatings("movie", 100).Imdb, Db.GetRatings("movie", 100).RottenTomatoes));
        Db.SetRatings("movie", 100, new Ratings(null, null));
        Assert.False(Db.GetRatings("movie", 100).Any);
        Assert.Empty(Db.NeedingRatings());                       // "{}" is stored, so it is not asked again
    }

    [Fact]
    public void FavouritesToggleAndListNewestFirst()
    {
        Assert.False(Db.IsFavourite("movie", 1));
        Db.SetFavourite("movie", 1, true);
        Db.SetFavourite("show", 2, true);
        Db.SetFavourite("movie", 1, true);                       // twice is still once
        Assert.Equal(new[] { ("show", 2L), ("movie", 1L) }, Db.Favourites());
        Db.SetFavourite("movie", 1, false);
        Assert.False(Db.IsFavourite("movie", 1));
        Assert.Equal(new[] { ("show", 2L) }, Db.Favourites());
    }

    [Fact]
    public void ARematchedTitleKeepsItsFavouriteAndIsAskedForDetailsAgain()
    {
        Seed(M());
        Db.SetFavourite("movie", 100, true);
        Db.SetDetail("movie", 100, Detail.Empty);
        Db.SetMatch("movie", "Title", 2026, M());                // a rescan or rematch rewrites the meta row
        Assert.True(Db.IsFavourite("movie", 100));
        Assert.Single(Db.NeedingDetail());
    }

    [Fact]
    public void ArtworkIsTrimmedOldestFirstAndOnlyDownToTheLimit()
    {
        var art = Path.Combine(_dir, "art");
        Directory.CreateDirectory(art);
        for (int i = 0; i < 5; i++)
        {
            var f = Path.Combine(art, $"f{i}.jpg");
            File.WriteAllBytes(f, new byte[1000]);
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-(10 - i)));   // f0 is oldest
        }
        Assert.Equal(0, Db.TrimArt(5000));
        Assert.Equal(2, Db.TrimArt(3000));
        Assert.Equal(new[] { "f2.jpg", "f3.jpg", "f4.jpg" }, new DirectoryInfo(art).GetFiles().Select(f => f.Name).OrderBy(n => n));
    }

    [Fact]
    public void EpisodesCarryTheirPictureAirDateRuntimeAndScoreIntoTheList()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "art"));
        File.WriteAllText(Path.Combine(_dir, "art", "s1.jpg"), "x");                       // a downloaded episode picture
        File.WriteAllText(Path.Combine(_dir, "Show.S01E01.mkv"), "x");
        File.WriteAllText(Path.Combine(_dir, "Show.S01E02.mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        Db.SetMatch("show", "Show", null, new Meta("show", 200, "Show", "o", null, null, 7, 2026));
        Db.SetSeason(200, 1, new List<Ep> { new(1, "One", "first", "/s1.jpg", "2026-10-13", 54, 8.2) });   // episode 2 is unknown to TMDB
        var eps = Db.List().Where(i => i.Kind == "show").OrderBy(i => i.Episode).ToList();
        Assert.Equal(("2026-10-13", 54, 8.2), (eps[0].AirDate, eps[0].EpRuntime, eps[0].EpRating));
        Assert.Equal(Db.ArtPath("/s1.jpg"), eps[0].Still);
        Assert.Equal((null, null, null, null), (eps[1].AirDate, eps[1].EpRuntime, (double?)eps[1].EpRating, eps[1].Still));
    }

    [Fact]
    public void AnEpisodeIsFreshUntilItIsStartedOrWatched()
    {
        Assert.True(new MediaItem().IsFresh);
        Assert.True(new MediaItem { Position = 3 }.IsFresh);                              // a few seconds in does not count as started
        Assert.False(new MediaItem { Position = 60 }.IsFresh);
        Assert.False(new MediaItem { Watched = true }.IsFresh);
    }

    // ---------- the whole fill against a local fake TMDB and OMDb ----------

    const string Search = """{"results":[{"id":100,"title":"Title","overview":"o","poster_path":"/p.jpg","backdrop_path":"/b.jpg","vote_average":7.1,"release_date":"2026-01-01"}]}""";
    const string Movie = """{"runtime":100,"imdb_id":"tt1","genres":[{"name":"Mystery"}],"images":{"logos":[{"file_path":"/logo.png","iso_639_1":"en"}]},"credits":{"cast":[{"name":"Anne","character":"Denise","profile_path":"/anne.jpg"}]}}""";

    void Keys(string tmdb, string? omdb, FakeWeb web)
    {
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["tmdbKey"] = tmdb, ["omdbKey"] = omdb ?? "" }.Where(p => p.Value != "").ToDictionary(p => p.Key, p => p.Value)));
        Environment.SetEnvironmentVariable("LUMINA_TMDB_API", web.Base + "3/");
        Environment.SetEnvironmentVariable("LUMINA_TMDB_IMG", web.Base + "img/");
        Environment.SetEnvironmentVariable("LUMINA_OMDB_API", web.Base + "omdb/");
    }

    static (int, string, int) Route(HttpListenerRequest r)
    {
        var p = r.Url!.AbsolutePath;
        if (p.StartsWith("/3/search/movie")) return (200, Search, 0);
        if (p == "/3/movie/100") return (200, Movie, 0);
        if (p.StartsWith("/omdb/")) return (200, """{"imdbRating":"6.2","Ratings":[{"Source":"Rotten Tomatoes","Value":"86%"}]}""", 0);
        if (p.StartsWith("/img/")) return (200, "imagebytes", 0);
        return (404, "{}", 0);
    }

    [Fact]
    public async Task AFillStoresDetailScoresAndDownloadsLogoCastAndPoster()
    {
        File.WriteAllText(Path.Combine(_dir, "Title (2026).mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        using var web = new FakeWeb { Handler = Route };
        Keys("tmdbkey123", "omdbkey456", web);

        Assert.Equal("", await Tmdb.FillAsync(_ => { }));

        var d = Db.GetDetail("movie", 100)!;
        Assert.Equal((100, "tt1"), (d.Runtime, d.ImdbId));
        Assert.Equal("Anne", d.Cast.Single().Person);
        Assert.Equal(("6.2", "86%"), (Db.GetRatings("movie", 100).Imdb, Db.GetRatings("movie", 100).RottenTomatoes));
        Assert.True(File.Exists(Db.ArtPath("/logo.png")));
        Assert.True(File.Exists(Db.ArtPath("/anne.jpg")));
        Assert.True(File.Exists(Db.ArtPath("/p.jpg")));
        var urls = web.Requests.Select(r => r.Url).ToList();
        Assert.Contains(urls, u => u.Contains("append_to_response=credits,images,release_dates"));
        Assert.Contains(urls, u => u.Contains("omdb/") && u.Contains("apikey=omdbkey456"));

        web.Requests.Clear();
        Assert.Equal("", await Tmdb.FillAsync(_ => { }));            // nothing left to ask for: no new calls at all
        Assert.Empty(web.Requests);
    }

    [Fact]
    public async Task WithoutAnOmdbKeyOmdbIsNeverCalled()
    {
        File.WriteAllText(Path.Combine(_dir, "Title (2026).mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        using var web = new FakeWeb { Handler = Route };
        Keys("tmdbkey123", null, web);
        await Tmdb.FillAsync(_ => { });
        Assert.DoesNotContain(web.Requests, r => r.Url.Contains("omdb"));
        Assert.Single(Db.NeedingRatings());                          // still waiting for a key
    }

    [Fact]
    public async Task AServerErrorOnDetailsLeavesEverythingElseAndRetriesNextTime()
    {
        File.WriteAllText(Path.Combine(_dir, "Title (2026).mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        using var web = new FakeWeb { Handler = r => r.Url!.AbsolutePath == "/3/movie/100" ? (500, "boom", 0) : Route(r) };
        Keys("tmdbkey123", null, web);

        Assert.Equal("", await Tmdb.FillAsync(_ => { }));
        Assert.True(File.Exists(Db.ArtPath("/p.jpg")));              // the poster still arrived
        Assert.Null(Db.GetDetail("movie", 100));
        Assert.Single(Db.NeedingDetail());                           // asked again next scan

        web.Handler = Route;
        Assert.Equal("", await Tmdb.FillAsync(_ => { }));
        Assert.NotNull(Db.GetDetail("movie", 100));
    }

    [Fact]
    public async Task ANotFoundTitleIsMarkedDoneSoItIsNotAskedForeverAndOmdbErrorsNeverNameTheKey()
    {
        File.WriteAllText(Path.Combine(_dir, "Title (2026).mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        using var web = new FakeWeb { Handler = r => r.Url!.AbsolutePath == "/3/movie/100" ? (404, "{}", 0) : Route(r) };
        Keys("tmdbkey123", "secretomdb", web);
        await Tmdb.FillAsync(_ => { });
        Assert.Empty(Db.NeedingDetail());
        Assert.NotNull(Db.GetDetail("movie", 100));

        web.Handler = _ => (401, "{\"Error\":\"Invalid API key!\"}", 0);
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Omdb.FetchAsync("tt1"));
        Assert.DoesNotContain("secretomdb", e.Message);
    }
}
