using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

/// <summary>Issue #15: reorder and hide Home's rows.</summary>
public class HomeLayoutTests
{
    static readonly string[] Standard = HomeLayout.Known.Select(k => k.Id).ToArray();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense+more")]
    public void NothingSavedMeansEveryRowInTheStandardOrder(string? text)
    {
        var (order, hidden) = HomeLayout.Parse(text);
        Assert.Equal(Standard, order);
        Assert.Empty(hidden);
        Assert.True(HomeLayout.IsDefault(text));
    }

    [Fact]
    public void KeepsTheUsersOrderAndHiddenRows()
    {
        var (order, hidden) = HomeLayout.Parse("recent+continue+-unwatched+recent+bogus");
        Assert.Equal(new[] { "recent", "continue", "unwatched" }, order.Take(3));
        Assert.Equal(new[] { "unwatched" }, hidden);
        Assert.Equal(Standard.Length, order.Count);   // repeats and unknown names dropped, the rest added
    }

    [Fact]
    public void RowsAddedInALaterVersionAppearShownAtTheEnd()
    {
        var older = string.Join("+", Standard.Take(Standard.Length - 1).Reverse());
        var (order, hidden) = HomeLayout.Parse(older);
        Assert.Equal(Standard[^1], order[^1]);
        Assert.DoesNotContain(Standard[^1], hidden);
    }

    [Fact]
    public void FormatRoundTrips()
    {
        var order = Standard.Reverse().ToList();
        var hidden = new HashSet<string> { "favourites", "movies" };
        var text = HomeLayout.Format(order, hidden);
        Assert.Equal((order, hidden), HomeLayout.Parse(text) is var (o, h) ? (o, h) : default, new LayoutEq());
        Assert.False(HomeLayout.IsDefault(text));
        Assert.True(HomeLayout.IsDefault(HomeLayout.Format(Standard, new HashSet<string>())));
    }

    sealed class LayoutEq : IEqualityComparer<(List<string>, HashSet<string>)>
    {
        public bool Equals((List<string>, HashSet<string>) a, (List<string>, HashSet<string>) b) => a.Item1.SequenceEqual(b.Item1) && a.Item2.SetEquals(b.Item2);
        public int GetHashCode((List<string>, HashSet<string>) x) => 0;
    }
}
