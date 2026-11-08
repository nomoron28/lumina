using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

public class HomeGenreRowTests
{
    static LibraryEntry E(string title, string kind = "movie", long tmdb = 0, double? rating = null, string? backdrop = null, params string[] genres) =>
        new() { Key = kind + ":" + title, Kind = kind, Title = title, TmdbId = tmdb, Rating = rating, Backdrop = backdrop, Genres = genres, Items = new List<MediaItem> { new() { Title = title, Kind = kind } } };

    static readonly LibraryEntry[] Lib =
    {
        E("Alpha", "movie", 1, 7.0, "a.jpg", "Mystery", "Thriller"),
        E("Beta", "movie", 2, 8.5, "b.jpg", "Mystery"),
        E("Gamma", "movie", 3, null, null, "Comedy"),
        E("Delta", "show", 10, 9.0, "d.jpg", "Drama", "Mystery"),
        E("Epsilon", "movie", 4, 8.5, null),
    };

    [Fact]
    public void GenresListMostTitlesFirstThenByNameAndOnlyForTheKindAsked()
    {
        var film = HomeShelves.Genres(Lib, "movie");
        Assert.Equal(new[] { "Mystery", "Comedy", "Thriller" }, film.Select(g => g.Name));
        Assert.Equal(new[] { 2, 1, 1 }, film.Select(g => g.Count));
        Assert.Equal(new[] { "Drama", "Mystery" }, HomeShelves.Genres(Lib, "show").Select(g => g.Name));   // the show's Mystery is its own tile
    }

    [Fact]
    public void AGenreTileTakesItsBackdropFromTheBestRatedTitleThatHasOne()
    {
        Assert.Equal("b.jpg", HomeShelves.Genres(Lib, "movie").First(g => g.Name == "Mystery").Backdrop);   // Beta 8.5 beats Alpha 7.0
        Assert.Null(HomeShelves.Genres(Lib, "movie").First(g => g.Name == "Comedy").Backdrop);              // none has artwork
    }

    [Fact]
    public void GenresMatchIgnoringCaseAndATitleWithoutGenresAddsNone()
    {
        var l = new[] { E("A", "movie", 1, 5, null, "Sci-Fi"), E("B", "movie", 2, 5, null, "sci-fi"), E("C") };
        var g = Assert.Single(HomeShelves.Genres(l, "movie"));
        Assert.Equal(2, g.Count);
        Assert.Equal(2, HomeShelves.InGenre(l, "movie", "SCI-FI").Count);
        Assert.Empty(HomeShelves.Genres(Array.Empty<LibraryEntry>(), "movie"));
    }

    [Fact]
    public void InGenreKeepsTheKindApart() =>
        Assert.Equal(new[] { "Alpha", "Beta" }, HomeShelves.InGenre(Lib, "movie", "Mystery").Select(e => e.Title));

    [Fact]
    public void TopRatedIsBestFirstTiesByTitleHonoursTheLimitAndLeavesOutUnscored()
    {
        var all = HomeShelves.TopRated(Lib, "movie");
        Assert.DoesNotContain(all, e => e.Title == "Gamma");                  // no score
        Assert.Equal(3, all.Count);
        Assert.Equal(("Beta", "Epsilon"), (all[0].Title, all[1].Title));        // 8.5 twice: alphabetical
        Assert.Single(HomeShelves.TopRated(Lib, "movie", 1));
        Assert.Equal(new[] { "Delta" }, HomeShelves.TopRated(Lib, "show").Select(e => e.Title));
    }

    [Fact]
    public void FavouritesFollowTheStarOrderSkipMissingTitlesAndNeverRepeat()
    {
        var fav = new List<(string, long)> { ("movie", 3), ("show", 10), ("movie", 999), ("movie", 3), ("movie", 1) };
        Assert.Equal(new[] { "Gamma", "Delta", "Alpha" }, HomeShelves.Favourites(Lib, fav).Select(e => e.Title));
        Assert.Empty(HomeShelves.Favourites(Lib, Array.Empty<(string, long)>()));
        Assert.Empty(HomeShelves.Favourites(Lib, new List<(string, long)> { ("movie", 0) }));              // an unmatched title has no id to star
    }

    [Fact]
    public void ColoursAreStableAndReadableUnderWhiteText()
    {
        Assert.Equal(HomeShelves.Colours("Mystery"), HomeShelves.Colours("mystery"));
        foreach (var name in new[] { "Mystery", "Science Fiction", "Thriller", "Drama", "Comedy", "Horror", "Animation", "Documentary" })
        {
            var (from, to) = HomeShelves.Colours(name);
            // WCAG: white text on the darker of the two ends (the overlay is 80% opaque over a backdrop, so judge the colour itself)
            Assert.True(Contrast("FFFFFF", from) >= 3.0, $"{name} start {from}");
            Assert.True(Contrast("FFFFFF", to) >= 3.0, $"{name} end {to}");
        }
        Assert.True(new[] { "Mystery", "Science Fiction", "Thriller", "Drama", "Comedy", "Horror", "Animation", "Documentary" }
            .Select(HomeShelves.Colours).Distinct().Count() >= 4);                                            // a spread, not one colour for everything
    }

    static double Contrast(string a, string b)
    {
        static double L(string hex)
        {
            double Ch(int i) { var v = Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(0) + 0.7152 * Ch(2) + 0.0722 * Ch(4);
        }
        var (hi, lo) = (Math.Max(L(a), L(b)), Math.Min(L(a), L(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    [Fact]
    public void AnEntryIsFreshOnlyWhenNothingOfItWasStarted()
    {
        LibraryEntry Show(params MediaItem[] eps) => new() { Kind = "show", Items = eps.ToList() };
        Assert.True(Show(new MediaItem(), new MediaItem()).IsFresh);
        Assert.False(Show(new MediaItem(), new MediaItem { Watched = true }).IsFresh);
        Assert.False(Show(new MediaItem { Position = 120 }, new MediaItem()).IsFresh);
        Assert.False(new LibraryEntry().IsFresh);                                                           // nothing in it at all
    }

    [Fact]
    public void GenreTileTextIsPluralAwareAndItsOverlayIsFrozen()
    {
        var one = new GenreTile("movie", "Mystery", 1, null, "3F37C9", "E85D2C");
        Assert.Equal("1 title", one.CountText);
        Assert.Equal("5 titles", (one with { Count = 5 }).CountText);
        Assert.Equal("Mystery", one.Title);
        Assert.True(one.Overlay.IsFrozen);
    }
}
