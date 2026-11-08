using System.IO;
using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

public class NameParserTests
{
    [Theory]
    [InlineData("Breaking.Bad.S02E07.1080p.BluRay.x265", "show", "Breaking Bad", null, 2, 7)]
    [InlineData("the_office_us 3x05 720p", "show", "the office us", null, 3, 5)]
    [InlineData("Movie.Name.2019.2160p.UHD.BluRay.x265-GRP", "movie", "Movie Name", 2019, null, null)]
    [InlineData("The Matrix (1999) 1080p", "movie", "The Matrix", 1999, null, null)]
    [InlineData("2012.2009.1080p", "movie", "2012", 2009, null, null)]
    [InlineData("1917", "movie", "1917", null, null, null)]
    [InlineData("Some Movie 1080p x264", "movie", "Some Movie", null, null, null)]
    [InlineData("[Group] Anime Title S01E03 [1080p]", "show", "Anime Title", null, 1, 3)]
    [InlineData("S01E02", "show", "", null, 1, 2)]
    [InlineData("Beef (2023) - S01E01 - The Birds Dont Sing", "show", "Beef", 2023, 1, 1)]
    [InlineData("Rizzoli & Isles (2010) - S01E01 - See One. Do One.", "show", "Rizzoli & Isles", 2010, 1, 1)]
    [InlineData("Show.Name.2019.S02E03.720p", "show", "Show Name", 2019, 2, 3)]
    [InlineData("Blade Runner 2049 S01E01", "show", "Blade Runner 2049", null, 1, 1)]
    [InlineData("Numb3rs (2005) - S05E01", "show", "Numb3rs", 2005, 5, 1)]
    public void Parses(string name, string kind, string title, int? year, int? season, int? episode) =>
        Assert.Equal(new Parsed(kind, title, year, season, episode), NameParser.Parse(name));
}

[CollectionDefinition("db", DisableParallelization = true)]
public class DbCollection { }

[Collection("db")]
public class DbTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-test-" + Guid.NewGuid().ToString("N"));

    public DbTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void ScanIsIncrementalAndPrunesMissing()
    {
        var lib = Path.Combine(_dir, "media");
        Directory.CreateDirectory(Path.Combine(lib, "Show Name", "Season 1"));
        File.WriteAllText(Path.Combine(lib, "Movie (2020) 1080p.mkv"), "x");
        File.WriteAllText(Path.Combine(lib, "Show Name", "Season 1", "S01E01.mkv"), "x");
        File.WriteAllText(Path.Combine(lib, "notes.txt"), "ignored");
        Db.Init();
        Db.AddRoot(lib);

        Assert.Equal(2, Scanner.ScanAll());
        var items = Db.List();
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Kind == "show" && i.Title == "Show Name" && i.Season == 1 && i.Episode == 1);

        Assert.Equal(0, Scanner.ScanAll()); // nothing changed -> nothing touched

        File.WriteAllText(Path.Combine(lib, "New.Film.2021.mkv"), "x");
        Assert.Equal(1, Scanner.ScanAll()); // only the new file

        File.Delete(Path.Combine(lib, "Movie (2020) 1080p.mkv"));
        Scanner.ScanAll();
        Assert.DoesNotContain(Db.List(), i => i.Title == "Movie");
    }

    [Fact]
    public void AShowStoredWithItsYearInTheTitleIsFixedAndMatchedAgain()
    {
        var lib = Path.Combine(_dir, "tv");
        Directory.CreateDirectory(Path.Combine(lib, "Beef (2023)", "Season 01"));
        File.WriteAllText(Path.Combine(lib, "Beef (2023)", "Season 01", "Beef (2023) - S01E01 - Pilot.mkv"), "x");
        Db.Init();
        Db.AddRoot(lib);
        Scanner.ScanAll();
        Assert.Contains(Db.List(), i => i.Title == "Beef" && i.Year == 2023);

        // an older version stored it as "Beef 2023" and the search for that found nothing (tmdb_id 0)
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(_dir, "t.db")))
        {
            c.Open();
            using var u = c.CreateCommand();
            u.CommandText = "UPDATE media SET title='Beef 2023', year=NULL, tmdb_id=0";
            u.ExecuteNonQuery();
        }
        Db.KvSet("parse_v", "1");
        Scanner.ScanAll();
        var fixedRow = Assert.Single(Db.List());
        Assert.Equal("Beef", fixedRow.Title);
        Assert.Equal(2023, fixedRow.Year);
        Assert.Contains(Db.PendingTitles(), t => t.Title == "Beef" && t.Year == 2023);    // queued for a new search
        Assert.Equal(0, Scanner.ScanAll());                                                 // and only once
    }

    [Fact]
    public void OfflineRootKeepsItsRows()
    {
        var lib = Path.Combine(_dir, "nas");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "A.2020.mkv"), "x");
        Db.Init();
        Db.AddRoot(lib);
        Scanner.ScanAll();
        Directory.Delete(lib, true); // NAS goes away
        Scanner.ScanAll();
        Assert.Single(Db.List());
    }

    [Fact]
    public void WatchedIsStickyAndResumeSurvives()
    {
        var lib = Path.Combine(_dir, "m");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "A.2020.mkv"), "x");
        Db.Init();
        Db.AddRoot(lib);
        Scanner.ScanAll();
        var id = Db.List()[0].Id;

        Db.SaveWatch(id, 120, 1000, false);
        var a = Db.List()[0];
        Assert.Equal(120, a.Position);
        Assert.False(a.Watched);
        Assert.Equal(12, a.Percent, 3);

        Db.SaveWatch(id, 950, 1000, true);
        Db.SaveWatch(id, 30, 1000, false); // rewatching must not un-watch
        var b = Db.List()[0];
        Assert.True(b.Watched);
        Assert.Equal(30, b.Position);

        Scanner.ScanAll(); // rescan keeps watch state
        Assert.True(Db.List()[0].Watched);
    }
}
