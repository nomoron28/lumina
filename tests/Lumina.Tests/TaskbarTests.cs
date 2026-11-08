using Lumina.Windows;
using Xunit;

namespace Lumina.Tests;

public class TaskbarStateTests
{
    [Fact]
    public void NothingPlayingShowsNoProgress() =>
        Assert.Equal(new TaskbarState(ProgressKind.None, 0), TaskbarState.From(false, 50, 100, false, false));

    [Fact]
    public void PlayingShowsTheFraction() =>
        Assert.Equal(new TaskbarState(ProgressKind.Normal, 0.25), TaskbarState.From(true, 25, 100, false, false));

    [Fact]
    public void PausedKeepsTheFractionInThePausedColour() =>
        Assert.Equal(new TaskbarState(ProgressKind.Paused, 0.5), TaskbarState.From(true, 50, 100, true, false));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void UnknownLengthIsIndeterminate(double duration) =>
        Assert.Equal(ProgressKind.Indeterminate, TaskbarState.From(true, 10, duration, false, false).Kind);

    [Fact]
    public void BufferingIsIndeterminateEvenWithAKnownLength() =>
        Assert.Equal(ProgressKind.Indeterminate, TaskbarState.From(true, 10, 100, false, true).Kind);

    [Theory]
    [InlineData(-5, 100, 0)]
    [InlineData(150, 100, 1)]
    [InlineData(100, 100, 1)]
    public void OutOfRangePositionsAreClamped(double pos, double dur, double expected) =>
        Assert.Equal(expected, TaskbarState.From(true, pos, dur, false, false).Value);

    [Fact]
    public void NaNPositionNeverReachesTheTaskbar() =>
        Assert.Equal(0, TaskbarState.From(true, double.NaN, 100, false, false).Value);
}

public class RecentFilesTests
{
    [Fact]
    public void NewestFirstWithoutDuplicatesIgnoringCase()
    {
        var l = RecentFiles.Add(new[] { @"D:\a.mkv", @"D:\b.mkv" }, @"d:\A.MKV");
        Assert.Equal(new[] { @"d:\A.MKV", @"D:\b.mkv" }, l);
    }

    [Fact]
    public void ListIsCapped()
    {
        var l = new List<string>();
        for (var i = 0; i < 25; i++) l = RecentFiles.Add(l, $"f{i}.mkv");
        Assert.Equal(RecentFiles.Max, l.Count);
        Assert.Equal("f24.mkv", l[0]);
        Assert.Equal("f15.mkv", l[^1]);
    }

    [Fact]
    public void PruneDropsMissingFilesButKeepsStreams()
    {
        var l = RecentFiles.Prune(new[] { "gone.mkv", "here.mkv", "https://x/y.m3u8" }, p => p == "here.mkv");
        Assert.Equal(new[] { "here.mkv", "https://x/y.m3u8" }, l);
    }

    [Fact]
    public void SerializationSurvivesOddNamesAndBadData()
    {
        var l = new[] { "D:/Mu sic/café 日本語 \"q\".mkv" };
        Assert.Equal(l, RecentFiles.Deserialize(RecentFiles.Serialize(l)));
        Assert.Empty(RecentFiles.Deserialize("{not json"));
        Assert.Empty(RecentFiles.Deserialize(null));
        Assert.Empty(RecentFiles.Deserialize("[\"\", \"  \"]"));
    }
}
