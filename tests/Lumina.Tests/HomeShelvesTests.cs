using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

public class HomeShelvesTests
{
    static long _id = 1000;

    static MediaItem Ep(int season, int episode, bool watched = false, double pos = 0, long tmdb = 7, string title = "Show",
        long added = 0, long played = 0, string? epName = null, double? dur = 1000) =>
        new() { Id = ++_id, Path = $"x/{title}.S{season}E{episode}.mkv", Kind = "show", Title = title, Season = season, Episode = episode,
                Watched = watched, Position = pos, Duration = dur, TmdbId = tmdb, AddedAt = added, PlayedAt = played, EpisodeName = epName };

    static MediaItem Movie(string title, bool watched = false, double pos = 0, long added = 0, long played = 0, int? year = 2020) =>
        new() { Id = ++_id, Path = $"x/{title}.mkv", Kind = "movie", Title = title, Year = year, Watched = watched, Position = pos, Duration = 1000, AddedAt = added, PlayedAt = played };

    [Fact]
    public void ContinueWatchingHoldsPartlyPlayedMoviesAndStartedShowsNewestFirst()
    {
        var entries = LibraryEntry.Build(new[]
        {
            Movie("Half Done", pos: 400, played: 100),
            Movie("Never Played"),
            Movie("Finished", watched: true, played: 500),
            Ep(1, 1, watched: true, played: 300, title: "Series"), Ep(1, 2, title: "Series"), Ep(1, 3, title: "Series"),
            Ep(1, 1, tmdb: 8, title: "Untouched"),
        });
        var c = LibraryEntry.ContinueWatching(entries);
        Assert.Equal(new[] { "Series", "Half Done" }, c.Select(e => e.Title)); // most recently played first
        Assert.Equal(2, c[0].ContinueItem!.Episode);                            // up next, not the one already finished
    }

    [Fact]
    public void AFinishedShowLeavesContinueWatching()
    {
        var entries = LibraryEntry.Build(new[] { Ep(1, 1, watched: true, played: 10), Ep(1, 2, watched: true, played: 20) });
        Assert.Empty(LibraryEntry.ContinueWatching(entries));
    }

    [Fact]
    public void ContinueWatchingIsCappedAndRecentlyAddedIsNewestFirst()
    {
        var many = Enumerable.Range(1, 30).Select(i => Movie("M" + i.ToString("00"), pos: 300, added: i, played: i)).ToList();
        var entries = LibraryEntry.Build(many);
        Assert.Equal(12, LibraryEntry.ContinueWatching(entries).Count);
        var recent = LibraryEntry.RecentlyAdded(entries, 5);
        Assert.Equal(new[] { "M30", "M29", "M28", "M27", "M26" }, recent.Select(e => e.Title));
    }

    [Fact]
    public void ShowAddedTimeIsItsNewestEpisode()
    {
        var entries = LibraryEntry.Build(new[] { Ep(1, 1, added: 10), Ep(1, 2, added: 90), Movie("Film", added: 50) });
        var recent = LibraryEntry.RecentlyAdded(entries);
        Assert.Equal(new[] { "Show", "Film" }, recent.Select(e => e.Title));
    }

    [Fact]
    public void UnwatchedKeepsAnythingWithAnEpisodeLeft()
    {
        var entries = LibraryEntry.Build(new[]
        {
            Movie("Seen", watched: true), Movie("Fresh"),
            Ep(1, 1, watched: true, title: "Partial"), Ep(1, 2, title: "Partial", tmdb: 9),
        });
        Assert.Equal(new[] { "Fresh", "Partial" }, LibraryEntry.Unwatched(entries).Select(e => e.Title).OrderBy(x => x));
    }

    [Fact]
    public void HeroSubShowsEpisodeForShowsAndYearForMovies()
    {
        var show = LibraryEntry.Build(new[] { Ep(2, 5, watched: false, pos: 300, epName: "Grilled") })[0];
        Assert.Equal("S02E05 · Grilled", show.HeroSub);
        var movie = LibraryEntry.Build(new[] { Movie("Film", year: 1999) })[0];
        Assert.Equal("1999", movie.HeroSub);
    }

    [Fact]
    public void HeroPercentFollowsTheEpisodeInProgress()
    {
        var show = LibraryEntry.Build(new[] { Ep(1, 1, watched: true), Ep(1, 2, pos: 250) })[0];
        Assert.Equal(25, show.HeroPercent, 3);
        Assert.Equal(0, LibraryEntry.Build(new[] { Movie("Untouched") })[0].HeroPercent);
    }

    [Fact]
    public void ChipsAreShortFacts()
    {
        var m = LibraryEntry.Build(new[] { new MediaItem { Id = ++_id, Path = "p", Kind = "movie", Title = "T", Year = 2010, Rating = 8.44 } })[0];
        Assert.Equal(new[] { "Movie", "2010", "TMDB 8.4" }, m.Chips);
        var s = LibraryEntry.Build(new[] { Ep(1, 1), Ep(2, 1) })[0];
        Assert.Equal(new[] { "TV show", "2 seasons · 2 episodes" }, s.Chips);
    }

    [Fact]
    public void AllWatchedAndInProgressFlags()
    {
        var done = LibraryEntry.Build(new[] { Ep(1, 1, watched: true), Ep(1, 2, watched: true) })[0];
        Assert.True(done.AllWatched);
        Assert.False(done.AnyUnwatched);
        var going = LibraryEntry.Build(new[] { Ep(1, 1, watched: true), Ep(1, 2, pos: 300) })[0];
        Assert.False(going.AllWatched);
        Assert.True(going.InProgress);
    }

    [Fact]
    public void EpisodeDisplayHelpers()
    {
        var e = Ep(1, 7, epName: "The Pilot");
        Assert.Equal("7", e.EpisodeNumber);
        Assert.Equal("The Pilot", e.EpisodeTitle);
        Assert.Equal("Show.S1E7", Ep(1, 7).EpisodeTitle); // no TMDB name: the file name stands in
    }

    [Fact]
    public void NextEpisodeWalksSeasonsInOrderAndStopsAtTheEnd()
    {
        var all = new[] { Ep(1, 1), Ep(1, 2), Ep(2, 1), Ep(1, 1, tmdb: 99, title: "Other"), Movie("Film") };
        Assert.Equal((1, 2), (MediaItem.NextEpisode(all, all[0])!.Season, MediaItem.NextEpisode(all, all[0])!.Episode));
        Assert.Equal(2, MediaItem.NextEpisode(all, all[1])!.Season);   // rolls into the next season
        Assert.Null(MediaItem.NextEpisode(all, all[2]));               // last episode of the show
        Assert.Null(MediaItem.NextEpisode(all, all[4]));               // movies have no next
    }
}
