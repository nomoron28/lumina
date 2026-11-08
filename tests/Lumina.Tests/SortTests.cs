using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

public class SortTests
{
    static long _id = 5000;

    static LibraryEntry E(string title, int? year = null, double? rating = null, long added = 0, long played = 0) =>
        LibraryEntry.Build(new[]
        {
            new MediaItem { Id = ++_id, Path = $"x/{title}.mkv", Kind = "movie", Title = title, Year = year, MetaYear = year, Rating = rating, AddedAt = added, PlayedAt = played, Duration = 100 },
        })[0];

    static string[] Order(SortKey k, params LibraryEntry[] e) => LibrarySort.Apply(e, k).Select(x => x.Title).ToArray();

    [Fact]
    public void TitleIgnoresCase() => Assert.Equal(new[] { "alpha", "Beta", "Gamma" }, Order(SortKey.Title, E("Gamma"), E("alpha"), E("Beta")));

    [Fact]
    public void AddedIsNewestFirstWithTitleBreakingTies() =>
        Assert.Equal(new[] { "C", "A", "B" }, Order(SortKey.Added, E("B", added: 10), E("A", added: 10), E("C", added: 99)));

    [Fact]
    public void WatchedPutsNeverWatchedLastNotFirst() =>
        Assert.Equal(new[] { "Late", "Early", "Never1", "Never2" }, Order(SortKey.Watched, E("Never2"), E("Early", played: 5), E("Never1"), E("Late", played: 50)));

    [Fact]
    public void YearIsNewestFirstAndMissingYearsGoLast() =>
        Assert.Equal(new[] { "New", "Old", "NoYearA", "NoYearB" }, Order(SortKey.Year, E("NoYearB"), E("Old", 1999), E("New", 2024), E("NoYearA")));

    [Fact]
    public void RatingIsHighestFirstAndUnratedGoLast() =>
        Assert.Equal(new[] { "Great", "Fine", "Unrated" }, Order(SortKey.Rating, E("Unrated"), E("Fine", rating: 6.5), E("Great", rating: 9.1)));

    [Fact]
    public void ARatingOfZeroStillCountsAsRatedAheadOfUnrated() =>
        Assert.Equal(new[] { "Zero", "Unrated" }, Order(SortKey.Rating, E("Unrated"), E("Zero", rating: 0)));

    [Fact]
    public void EmptyAndSingleListsAreFine()
    {
        Assert.Empty(LibrarySort.Apply(Array.Empty<LibraryEntry>(), SortKey.Rating));
        Assert.Single(LibrarySort.Apply(new[] { E("Only") }, SortKey.Watched));
    }

    [Fact]
    public void RepeatedSortsGiveIdenticalOrderWhenValuesTie()
    {
        var items = Enumerable.Range(0, 20).Select(i => E("T" + (i % 5), 2000, 7, 1, 1)).ToArray();
        var a = LibrarySort.Apply(items, SortKey.Year).Select(x => x.Title);
        var b = LibrarySort.Apply(items.Reverse(), SortKey.Year).Select(x => x.Title);
        Assert.Equal(a, b);   // ties fall back to the title, so input order never leaks into the result
    }

    [Theory]
    [InlineData("title", SortKey.Title)]
    [InlineData("added", SortKey.Added)]
    [InlineData("watched", SortKey.Watched)]
    [InlineData("year", SortKey.Year)]
    [InlineData("rating", SortKey.Rating)]
    [InlineData("nonsense", SortKey.Title)]
    [InlineData(null, SortKey.Title)]
    public void SettingValuesParseAndUnknownOnesFallBackToTitle(string? value, SortKey expected) => Assert.Equal(expected, LibrarySort.Parse(value));

    [Fact]
    public void EveryKeyHasAChoiceAndTheNamesRoundTrip()
    {
        foreach (var k in Enum.GetValues<SortKey>())
        {
            Assert.Contains(LibrarySort.Choices, c => c.Value == LibrarySort.Name(k));
            Assert.Equal(k, LibrarySort.Parse(LibrarySort.Name(k)));
        }
    }
}
