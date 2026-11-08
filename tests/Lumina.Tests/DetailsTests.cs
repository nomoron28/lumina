using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

public class LibraryEntryTests
{
    static long _id;

    static MediaItem Ep(int season, int episode, bool watched = false, double pos = 0, double? dur = 1000, long tmdb = 7, string title = "Show") =>
        new() { Id = ++_id, Path = $"x/{title}.S{season}E{episode}.mkv", Kind = "show", Title = title, Season = season, Episode = episode,
                Watched = watched, Position = pos, Duration = dur, TmdbId = tmdb };

    static MediaItem Movie(string title, int? year = 2020, double pos = 0, bool watched = false) =>
        new() { Id = ++_id, Path = $"x/{title}.mkv", Kind = "movie", Title = title, Year = year, Position = pos, Duration = 1000, Watched = watched };

    [Fact]
    public void GroupsEpisodesIntoOneShowAndKeepsMoviesSeparate()
    {
        var entries = LibraryEntry.Build(new[]
        {
            Ep(1, 2), Movie("Zebra"), Ep(1, 1), Movie("Alpha"), Ep(2, 1), Ep(1, 1, tmdb: 99, title: "Other"),
        });

        Assert.Equal(new[] { "Alpha", "Zebra", "Other", "Show" }, entries.Select(e => e.Title)); // movies first, each alphabetical
        var show = entries.Single(e => e.Title == "Show");
        Assert.Equal("show:7", show.Key);
        Assert.Equal(new[] { (1, 1), (1, 2), (2, 1) }, show.Items.Select(i => (i.Season!.Value, i.Episode!.Value)));
        Assert.Equal("2 seasons · 3 episodes", show.Sub);
        Assert.Equal("1 season · 1 episode", entries.Single(e => e.Title == "Other").Sub);
    }

    [Fact]
    public void ShowsWithoutTmdbIdGroupByTitleIgnoringCase()
    {
        var entries = LibraryEntry.Build(new[] { Ep(1, 1, tmdb: 0, title: "My Show"), Ep(1, 2, tmdb: 0, title: "my show") });
        Assert.Single(entries);
        Assert.Equal(2, entries[0].Items.Count);
    }

    [Fact]
    public void PercentIsProgressForMoviesAndWatchedShareForShows()
    {
        Assert.Equal(25, LibraryEntry.Build(new[] { Movie("M", pos: 250) })[0].Percent, 3);
        var show = LibraryEntry.Build(new[] { Ep(1, 1, watched: true), Ep(1, 2), Ep(1, 3), Ep(1, 4, watched: true) })[0];
        Assert.Equal(50, show.Percent, 3);
    }

    [Fact]
    public void NextPicksTheRightEpisode()
    {
        MediaItem Next(params MediaItem[] eps) => LibraryEntry.Build(eps)[0].Next;

        // nothing watched: start at the beginning
        Assert.Equal(1, Next(Ep(1, 2), Ep(1, 1), Ep(1, 3)).Episode);
        // partway through episode 2: continue it
        Assert.Equal(2, Next(Ep(1, 1, watched: true), Ep(1, 2, pos: 300), Ep(1, 3)).Episode);
        // finished 1 and 2: on to 3
        Assert.Equal(3, Next(Ep(1, 1, watched: true), Ep(1, 2, watched: true), Ep(1, 3)).Episode);
        // finished 2 only: continue after it, not back at the skipped 1
        Assert.Equal(3, Next(Ep(1, 1), Ep(1, 2, watched: true), Ep(1, 3)).Episode);
        // finished 2 and 3 but 1 is still unwatched: go back for it
        Assert.Equal(1, Next(Ep(1, 1), Ep(1, 2, watched: true), Ep(1, 3, watched: true)).Episode);
        // everything watched: start over
        Assert.Equal(1, Next(Ep(1, 1, watched: true), Ep(1, 2, watched: true)).Episode);
        // crosses a season boundary
        var next = Next(Ep(1, 1, watched: true), Ep(1, 2, watched: true), Ep(2, 1));
        Assert.Equal((2, 1), (next.Season!.Value, next.Episode!.Value));
        // barely started (5 s or less) does not count as resumable
        Assert.Equal(1, Next(Ep(1, 1, pos: 3), Ep(1, 2)).Episode);
    }

    [Fact]
    public void PlayLabels()
    {
        Assert.Equal("Play S01E01", LibraryEntry.Build(new[] { Ep(1, 1), Ep(1, 2) })[0].PlayLabel);
        Assert.Equal("Resume S01E02 (5:00)", LibraryEntry.Build(new[] { Ep(1, 1, watched: true), Ep(1, 2, pos: 300) })[0].PlayLabel);
        Assert.Equal("Play", LibraryEntry.Build(new[] { Movie("M") })[0].PlayLabel);
        Assert.Equal("Resume 1:02:03", LibraryEntry.Build(new[] { Movie("M", pos: 3723) })[0].PlayLabel);
        Assert.Equal("Play", LibraryEntry.Build(new[] { Movie("M", pos: 900, watched: true) })[0].PlayLabel); // watched: not resumable
    }

    [Fact]
    public void ItemDisplayHelpers()
    {
        Assert.Equal("Specials", Ep(0, 1).SeasonLabel);
        Assert.Equal("Season 3", Ep(3, 1).SeasonLabel);
        Assert.Equal("4. Show.S3E4", Ep(3, 4).RowTitle.Replace("x/", "")); // no TMDB name: falls back to the file name
        Assert.Equal("Watched", Ep(1, 1, watched: true).WatchedMark);
        Assert.Equal("0:09", MediaItem.Clock(9));
        Assert.Equal("12:05", MediaItem.Clock(725));
    }

    [Fact]
    public void MetaLineAndYear()
    {
        var m = LibraryEntry.Build(new[] { Movie("Film", 1999) })[0];
        Assert.Equal("Movie  ·  1999", m.Meta);
        var s = LibraryEntry.Build(new[] { Ep(1, 1) })[0];
        Assert.Equal("TV show  ·  1 season · 1 episode", s.Meta);
    }
}

[Collection("db")]
public class DetailsDbTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-det-" + Guid.NewGuid().ToString("N"));

    public DetailsDbTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
        Db.Init();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void ListCarriesBackdropEpisodeOverviewAndMetaYear()
    {
        var lib = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "Show.S01E01.mkv"), "x");
        File.WriteAllText(Path.Combine(lib, "Show.S01E02.mkv"), "x");
        Db.AddRoot(lib);
        Scanner.ScanAll();
        Db.SetMatch("show", "Show", null, new Meta("show", 7, "Show Canonical", "About it", "/p.jpg", "/b.jpg", 8.1, 2008));
        Db.SetSeason(7, 1, new() { new Ep(1, "Pilot", "The first one.", null) });

        var before = Db.List().OrderBy(i => i.Episode).ToList();
        Assert.All(before, i => Assert.Null(i.Backdrop)); // not downloaded yet
        Assert.Equal(2008, before[0].MetaYear);
        Assert.Equal("The first one.", before[0].EpisodeOverview);
        Assert.Null(before[1].EpisodeOverview); // blank row for the episode TMDB did not know

        Directory.CreateDirectory(Db.ArtDir);
        File.WriteAllText(Db.ArtPath("/b.jpg"), "img");
        Assert.Contains(Db.BackdropPaths(), p => p == "/b.jpg");

        var entry = LibraryEntry.Build(Db.List())[0];
        Assert.Equal("Show Canonical", entry.Title);
        Assert.NotNull(entry.Backdrop);
        Assert.Equal("About it", entry.Overview);
        Assert.Equal(8.1, entry.Rating);
        Assert.Equal(2008, entry.Year);
        Assert.Equal("TV show  ·  2008  ·  TMDB 8.1  ·  1 season · 2 episodes", entry.Meta);
    }
}
