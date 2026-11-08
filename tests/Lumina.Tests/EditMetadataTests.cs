using System.IO;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>"Edit metadata": the search results the window lists, and what choosing one (or "none") does to the library.</summary>
[Collection("db")]
public class EditMetadataTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-editmeta-" + Guid.NewGuid().ToString("N"));

    public EditMetadataTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    const string MovieResults = """
        {"results":[
          {"id":11,"title":"Ittefaq","release_date":"2017-11-03","overview":"Police officer Dev investigates a double murder case.","poster_path":"/a.jpg","backdrop_path":"/b.jpg","vote_average":6.9},
          {"id":12,"title":"Ittefaq","release_date":"2001-06-30","overview":"Shiva is a professional hit man.","poster_path":null,"vote_average":5.1},
          {"id":13,"title":"Itt van Amerika","release_date":"","overview":"","vote_average":0},
          {"title":"no id, skipped"}
        ]}
        """;

    [Fact]
    public void SearchResultsKeepTheirOrderDatesAndMissingPosters()
    {
        using var doc = JsonDocument.Parse(MovieResults);
        var l = Tmdb.ParseMany(doc.RootElement, tv: false);
        Assert.Equal(new long[] { 11, 12, 13 }, l.Select(c => c.Meta.TmdbId));            // the entry without an id is dropped
        Assert.Equal("2017-11-03", l[0].Date);
        Assert.Equal(2017, l[0].Meta.Year);
        Assert.Equal("/a.jpg", l[0].Meta.PosterPath);
        Assert.Null(l[1].Meta.PosterPath);
        Assert.Null(l[2].Meta.Year); Assert.Null(l[2].Date);
        Assert.All(l, c => Assert.Equal("movie", c.Meta.Kind));
    }

    [Fact]
    public void TvResultsUseTheNameAndFirstAirDate()
    {
        using var doc = JsonDocument.Parse("""{"results":[{"id":5,"name":"Beef","first_air_date":"2023-04-06","overview":"x","vote_average":8}]}""");
        var c = Assert.Single(Tmdb.ParseMany(doc.RootElement, tv: true));
        Assert.Equal(("show", "Beef", 2023), (c.Meta.Kind, c.Meta.Title, c.Meta.Year));
    }

    [Fact]
    public void TheListIsCapped()
    {
        var many = string.Join(",", Enumerable.Range(1, 40).Select(i => $"{{\"id\":{i},\"title\":\"T{i}\"}}"));
        using var doc = JsonDocument.Parse("{\"results\":[" + many + "]}");
        Assert.Equal(15, Tmdb.ParseMany(doc.RootElement, false).Count);
    }

    static (string lib, long id) AddMovie(string dir)
    {
        var lib = Path.Combine(dir, "media");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "Ittefaq.2017.1080p.mkv"), "x");
        Db.Init();
        Db.AddRoot(lib);
        Scanner.ScanAll();
        return (lib, Db.List().Single().Id);
    }

    [Fact]
    public void ChoosingAResultPointsTheFilesAtItAndQueuesItsDetails()
    {
        var (_, id) = AddMovie(_dir);
        using var doc = JsonDocument.Parse(MovieResults);
        var pick = Tmdb.ParseMany(doc.RootElement, false)[1].Meta;                        // the 2001 film
        Db.AssignMatch(new[] { id }, pick);
        var item = Db.List().Single();
        Assert.Equal(12, item.TmdbId);
        Assert.Contains(Db.NeedingDetail(), n => n.Kind == "movie" && n.Id == 12);
        Assert.Empty(Db.PendingTitles());                                                  // it is not searched for again
        Assert.Equal(0, Scanner.ScanAll());                                                // and a scan does not undo the choice
        Assert.Equal(12, Db.List().Single().TmdbId);
    }

    [Fact]
    public void ChoosingNoneStopsTmdbBeingAskedAndKeepsTheFileName()
    {
        var (_, id) = AddMovie(_dir);
        Db.AssignMatch(new[] { id }, new Meta("movie", 99, "Wrong film", null, null, null, 0, 2001));
        Db.AssignMatch(new[] { id }, null);
        var item = Db.List().Single();
        Assert.Equal(0, item.TmdbId);
        Assert.Equal("Ittefaq", item.Title);
        Assert.Empty(Db.PendingTitles());
    }

    [Fact]
    public void PickingATitleAgainKeepsWhatIsAlreadyKnownAboutIt()
    {
        var (_, id) = AddMovie(_dir);
        var first = new Meta("movie", 11, "Ittefaq", "overview", "/a.jpg", "/b.jpg", 6.9, 2017);
        Db.AssignMatch(new[] { id }, first);
        Db.SetDetail("movie", 11, new Detail(120, "U/A", new[] { "Thriller" }, "tag", null, "tt1", Array.Empty<Credit>()));
        Db.AssignMatch(new[] { id }, first with { Overview = null, PosterPath = null });    // a search result without a picture
        var d = Db.GetDetail("movie", 11);
        Assert.NotNull(d); Assert.Equal(120, d!.Runtime);                                  // the fetched details survive
        Assert.Contains("/a.jpg", Db.PosterPaths());                                       // and so does the picture
    }
}
