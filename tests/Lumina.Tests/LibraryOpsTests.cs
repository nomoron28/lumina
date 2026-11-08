using System.IO;
using Lumina.Library;
using Lumina.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class LibraryOpsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-ops-" + Guid.NewGuid().ToString("N"));
    string Lib => Path.Combine(_dir, "media");
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_DB");

    public LibraryOpsTests()
    {
        Directory.CreateDirectory(Path.Combine(Lib, "Show Name", "Season 1"));
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        File.WriteAllText(Path.Combine(Lib, "Movie (2020) 1080p.mkv"), "x");
        foreach (var e in new[] { "S01E01", "S01E02", "S01E03" }) File.WriteAllText(Path.Combine(Lib, "Show Name", "Season 1", e + ".mkv"), "x");
        Db.Init();
        Db.AddRoot(Lib);
        Scanner.ScanAll();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("LUMINA_DB", _old);
        Directory.Delete(_dir, true);
    }

    static List<MediaItem> Eps() => Db.List().Where(i => i.Kind == "show").OrderBy(i => i.Episode).ToList();

    [Fact]
    public void MarkingWatchedSetsTheFlagClearsTheResumePointAndCanBeUndone()
    {
        var e = Eps();
        Db.SaveWatch(e[0].Id, 40, 100, false);
        Assert.True(Eps()[0].CanResume);

        Db.MarkWatched(new[] { e[0].Id, e[1].Id }, true);
        var after = Eps();
        Assert.True(after[0].Watched && after[1].Watched);
        Assert.False(after[2].Watched);
        Assert.Equal(0, after[0].Position);
        Assert.True(after[0].PlayedAt > 0);

        Db.MarkWatched(new[] { e[0].Id }, false);
        Assert.False(Eps()[0].Watched);
        Assert.True(Eps()[1].Watched);                       // only the one asked about
        Assert.Equal(0, Eps()[0].PlayedAt);                   // no longer in Continue watching
    }

    [Fact]
    public void MarkingAnItemThatHasNoWatchRowYetStillWorks()
    {
        var movie = Db.List().Single(i => i.Kind == "movie");
        Db.MarkWatched(new[] { movie.Id }, false);            // never played: the row does not exist yet
        Assert.False(Db.List().Single(i => i.Kind == "movie").Watched);
        Db.MarkWatched(new[] { movie.Id }, true);
        Assert.True(Db.List().Single(i => i.Kind == "movie").Watched);
    }

    [Fact]
    public void MarkingNothingDoesNothing()
    {
        Db.MarkWatched(Array.Empty<long>(), true);
        Assert.All(Db.List(), i => Assert.False(i.Watched));
    }

    [Fact]
    public void HidingRemovesFromTheLibraryButNeverFromDiskAndSurvivesRescans()
    {
        var path = Path.Combine(Lib, "Movie (2020) 1080p.mkv");
        Assert.Equal(1, Db.Hide(new[] { path }));
        Assert.DoesNotContain(Db.List(), i => i.Kind == "movie");
        Assert.True(File.Exists(path));                       // the file is untouched
        Assert.Equal(1, Db.HiddenCount());

        File.SetLastWriteTime(path, DateTime.Now.AddHours(1)); // a changed file must not sneak back in either
        Scanner.ScanAll();
        Assert.DoesNotContain(Db.List(), i => i.Kind == "movie");
        Assert.Equal(3, Db.List().Count);                     // the show is unaffected
    }

    [Fact]
    public void ShowingHiddenAgainBringsThemBackOnTheNextScan()
    {
        var path = Path.Combine(Lib, "Show Name", "Season 1", "S01E02.mkv");
        Db.Hide(new[] { path });
        Assert.Equal(2, Eps().Count);
        Assert.Equal(1, Db.UnhideAll());
        Assert.Equal(0, Db.HiddenCount());
        Scanner.ScanAll();
        Assert.Equal(3, Eps().Count);
    }

    [Fact]
    public void HidingTwiceOrAnUnknownPathIsHarmless()
    {
        var path = Path.Combine(Lib, "Movie (2020) 1080p.mkv");
        Db.Hide(new[] { path });
        Db.Hide(new[] { path, Path.Combine(Lib, "nothing.mkv") });
        Assert.Equal(2, Db.HiddenCount());                    // the unknown path is remembered too, so it stays out if it appears later
        Assert.Equal(3, Db.List().Count);
    }

    [Fact]
    public void HidingIsCaseInsensitiveLikeWindowsPaths()
    {
        Db.Hide(new[] { Path.Combine(Lib, "MOVIE (2020) 1080P.MKV") });
        Assert.DoesNotContain(Db.List(), i => i.Kind == "movie");
    }

    [Fact]
    public void RematchClearsTheMatchSoTitlesAreSearchedAgain()
    {
        Db.SetMatch("movie", "Movie", 2020, new Meta("movie", 42, "Wrong Film", null, null, null, 5, 2020));
        Assert.Equal(42, Db.List().Single(i => i.Kind == "movie").TmdbId);
        Assert.DoesNotContain(Db.PendingTitles(), t => t.Title == "Movie");

        Db.Rematch("movie", "Movie", 2020);
        Assert.Equal(0, Db.List().Single(i => i.Kind == "movie").TmdbId);
        Assert.Contains(Db.PendingTitles(), t => t.Title == "Movie");
        Assert.Equal(("Movie", (int?)2020), Db.SearchTerms("movie", "Movie", 2020));   // no alias: asks about the file's own name
    }

    [Fact]
    public void FixMatchRemembersADifferentSearchAndPlainRematchForgetsIt()
    {
        Db.Rematch("movie", "Movie", 2020, "The Real Title", 1999);
        Assert.Equal(("The Real Title", (int?)1999), Db.SearchTerms("movie", "Movie", 2020));
        Assert.Equal(("movie", (int?)2019), Db.SearchTerms("movie", "movie", 2019));     // other titles unaffected: asked as given
        Assert.Equal(("The Real Title", (int?)1999), Db.SearchTerms("movie", "MOVIE", 2020)); // case-insensitive on the title
        Db.Rematch("movie", "Movie", 2020, "Only A Name", null);
        Assert.Equal(("Only A Name", (int?)null), Db.SearchTerms("movie", "Movie", 2020));
        Db.Rematch("movie", "Movie", 2020);
        Assert.Equal(("Movie", (int?)2020), Db.SearchTerms("movie", "Movie", 2020));
    }

    [Fact]
    public void RematchOnlyTouchesThatTitle()
    {
        Db.SetMatch("show", "Show Name", null, new Meta("show", 7, "Show Name", null, null, null, 8, 2010));
        Db.SetMatch("movie", "Movie", 2020, new Meta("movie", 42, "Movie", null, null, null, 5, 2020));
        Db.Rematch("movie", "Movie", 2020);
        Assert.All(Eps(), i => Assert.Equal(7, i.TmdbId));
    }

    [Fact]
    public void UnwatchedTimeIsStoredAndClearedWhenWatchedAgain()
    {
        Db.SetMatch("show", "Show Name", null, new Meta("show", 7, "Show Name", null, null, null, 8, 2010));
        var id = Eps()[0].Id;
        Db.MarkWatched(new[] { id }, false);
        Assert.NotNull(Db.LocalWatchRows().Single(r => r.Id == id).UnwatchedAt);
        Db.MarkWatched(new[] { id }, true);
        Assert.Null(Db.LocalWatchRows().Single(r => r.Id == id).UnwatchedAt);
    }
}

public class UnwatchMergeTests
{
    [Fact]
    public void AnOldTraktPlayDoesNotBringBackSomethingMarkedUnwatchedLater()
    {
        var local = new LocalState(Watched: false, Position: 0, PlayedAt: 0, Duration: 1000, UnwatchedAt: 2000);
        var m = TraktMerge.Merge(local, new RemoteState(Watched: true, WatchedAt: 1500, null, null));
        Assert.False(m.Watched);
        Assert.False(m.Changed);
    }

    [Fact]
    public void ANewerTraktPlayDoesCountBecauseYouWatchedItAgainElsewhere()
    {
        var local = new LocalState(false, 0, 0, 1000, UnwatchedAt: 2000);
        var m = TraktMerge.Merge(local, new RemoteState(true, WatchedAt: 3000, null, null));
        Assert.True(m.Watched);
        Assert.Equal(3000, m.PlayedAt);
    }

    [Fact]
    public void WithoutAnUnwatchRecordTheStickyRuleIsUnchanged()
    {
        var m = TraktMerge.Merge(new LocalState(false, 0, 0, 1000), new RemoteState(true, 1500, null, null));
        Assert.True(m.Watched);
    }

    [Fact]
    public void ARemoteHistoryWithNoDateIsTreatedAsOldWhenYouUnwatched()
    {
        var m = TraktMerge.Merge(new LocalState(false, 0, 0, 1000, UnwatchedAt: 10), new RemoteState(true, null, null, null));
        Assert.False(m.Watched);
    }

    [Fact]
    public void LocalWatchedStillBeatsEverythingEvenWithAnUnwatchRecord()
    {
        var m = TraktMerge.Merge(new LocalState(true, 0, 5000, 1000, UnwatchedAt: 2000), new RemoteState(false, null, null, null));
        Assert.True(m.Watched);
        Assert.True(m.Push);
    }
}

public class RevealTests
{
    [Fact]
    public void ExplorerArgumentsSelectTheFileAndQuoteSpecialCharacters()
    {
        Assert.Equal("/select,\"D:\\Mu sic\\a, b.mkv\"", Lumina.Windows.Explorer.SelectArgs(@"D:\Mu sic\a, b.mkv"));
    }

    [Fact]
    public void StreamsAndEmptyPathsCannotBeRevealed()
    {
        Assert.False(Lumina.Windows.Explorer.CanReveal("https://example.com/a.m3u8"));
        Assert.False(Lumina.Windows.Explorer.CanReveal(""));
        Assert.False(Lumina.Windows.Explorer.CanReveal(@"D:\definitely\not\here.mkv"));
        Assert.True(Lumina.Windows.Explorer.CanReveal(typeof(RevealTests).Assembly.Location));
    }
}

public class TitleQueryTests
{
    [Theory]
    [InlineData("Dune 2021", "Dune", 2021)]
    [InlineData("Dune (2021)", "Dune", 2021)]
    [InlineData("  Dune   (2021)  ", "Dune", 2021)]
    [InlineData("The Matrix", "The Matrix", null)]
    [InlineData("1917", "1917", null)]                  // a title that is only a year is a title
    [InlineData("2012 2009", "2012", 2009)]
    [InlineData("Blade Runner 2049", "Blade Runner", 2049)]   // known limit: a trailing year-like number is taken as the year
    [InlineData("Show Name 1899 (2022)", "Show Name 1899", 2022)]
    [InlineData("", "", null)]
    public void SplitsTheTitleFromAYear(string text, string title, int? year) =>
        Assert.Equal((title, year), Lumina.Library.TitleQuery.Split(text));
}
