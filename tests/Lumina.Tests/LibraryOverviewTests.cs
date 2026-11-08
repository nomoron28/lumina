using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Lumina.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

public class LibraryStatsTests
{
    static MediaItem M(string kind, long size) => new() { Kind = kind, Size = size };

    [Fact]
    public void CountsFilmsAndEpisodesAndAddsUpTheSize()
    {
        var s = LibraryStats.From(new[] { M("movie", 1_000_000_000), M("show", 500_000_000), M("show", 500_000_000), M("movie", -5) });
        Assert.Equal((2, 2), (s.Films, s.Episodes));
        Assert.Equal(2_000_000_000, s.Bytes);                        // a nonsense negative size is ignored
        Assert.Equal("1.9 GB", s.SizeText);
    }

    [Fact]
    public void AnEmptyLibraryIsAllZeros()
    {
        var s = LibraryStats.From(Array.Empty<MediaItem>());
        Assert.Equal((0, 0, 0L, "0 B"), (s.Films, s.Episodes, s.Bytes, s.SizeText));
    }

    static long Unix(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

    [Fact]
    public void LastUpdateReadsTodayYesterdayOrTheDate()
    {
        var now = new DateTime(2026, 10, 16, 18, 30, 0, DateTimeKind.Local);
        Assert.Equal("Today, 3:06 PM", LibraryStats.LastUpdate(Unix(new DateTime(2026, 10, 16, 15, 6, 0, DateTimeKind.Local)), now));
        Assert.Equal("Today, 12:05 AM", LibraryStats.LastUpdate(Unix(new DateTime(2026, 10, 16, 0, 5, 0, DateTimeKind.Local)), now));
        Assert.Equal("Yesterday, 9:30 AM", LibraryStats.LastUpdate(Unix(new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Local)), now));
        Assert.Equal("12 Oct 2026", LibraryStats.LastUpdate(Unix(new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Local)), now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NoScanYetSaysSo(long? unix) => Assert.Equal("Not scanned yet", LibraryStats.LastUpdate(unix, DateTime.Now));

    [Fact]
    public void BothKindsAreShownByDefault()
    {
        Assert.Equal("true", Catalog.Find("library.showMovies")!.Default);
        Assert.Equal("true", Catalog.Find("library.showShows")!.Default);
    }
}

[Collection("db")]
public class RequeueMetadataTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-requeue-" + Guid.NewGuid().ToString("N"));

    public RequeueMetadataTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
        Db.Init();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    [Fact]
    public void RequeueAsksAgainButKeepsMatchesFavouritesProgressAndAlreadyStoredDetails()
    {
        File.WriteAllText(Path.Combine(_dir, "Show.S01E01.mkv"), "x");
        File.WriteAllText(Path.Combine(_dir, "Show.S01E02.mkv"), "x");
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        Db.SetMatch("show", "Show", null, new Meta("show", 200, "Show", "o", null, null, 7, 2026));
        Db.SetSeason(200, 1, new List<Ep> { new(1, "One", "first", null) });             // episode 2 gets a blank row
        Db.SetDetail("show", 200, Detail.Empty with { ImdbId = "tt2", Genres = new[] { "Drama" } });
        Db.SetRatings("show", 200, new Ratings("8.0", null));
        Db.SetFavourite("show", 200, true);
        Db.MarkWatched(Db.List().Select(i => i.Id), true);
        Assert.Empty(Db.NeedingDetail());

        Db.RequeueMetadata();

        Assert.Equal(new[] { ("show", 200L) }, Db.NeedingDetail());                       // asked again
        Assert.Single(Db.NeedingRatings());
        Assert.Equal(new[] { "Drama" }, Db.GetDetail("show", 200)!.Genres);               // what was stored stays visible until the new answer replaces it
        Assert.Equal("tt2", Db.GetDetail("show", 200)!.ImdbId);
        Assert.True(Db.IsFavourite("show", 200));
        Assert.All(Db.List(), i => Assert.True(i.Watched));
        Assert.Equal(200, Db.List().First().TmdbId);                                      // still matched
        Assert.Equal("One", Db.List().First(i => i.Episode == 1).EpisodeName);            // a known episode is kept
        Assert.Single(Db.SeasonsNeedingEpisodes());                                       // the blank row for episode 2 is gone, so the season is fetched again
    }

    [Fact]
    public void RequeueOnAnEmptyDatabaseIsHarmless()
    {
        Db.RequeueMetadata();
        Assert.Empty(Db.NeedingDetail());
    }

    [Fact]
    public void ItemsCarryTheirFileSize()
    {
        File.WriteAllText(Path.Combine(_dir, "Movie (2020).mkv"), new string('x', 1234));
        Db.AddRoot(_dir);
        Scanner.ScanAll();
        Assert.Equal(1234, Db.List().Single().Size);
    }
}
