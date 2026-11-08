using System.IO;
using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

public class QueueTests
{
    static QueueItem I(string n) => new(n + ".mkv", n);
    static PlaybackQueue Q(int n, bool shuffle = false, int seed = 1)
    {
        var q = new PlaybackQueue(new Random(seed));
        q.Load(Enumerable.Range(1, n).Select(i => I("t" + i)));
        if (shuffle) q.SetShuffle(true);
        return q;
    }
    static List<string> Lap(PlaybackQueue q) { var l = new List<string> { q.CurrentItem!.Title }; for (var n = q.Count - 1; n > 0; n--) l.Add(q.Next()!.Title); return l; }

    [Fact]
    public void PlaysInOrderAndStopsAtTheEndWhenRepeatIsOff()
    {
        var q = Q(3);
        Assert.Equal("t1", q.CurrentItem!.Title);
        Assert.Equal("t2", q.Advance()!.Title);
        Assert.Equal("t3", q.Advance()!.Title);
        Assert.False(q.HasNext);
        Assert.Null(q.Advance());
        Assert.Equal("t3", q.CurrentItem!.Title);   // stays on the last item
        Assert.Equal("t2", q.Previous()!.Title);
        q.JumpTo(0);
        Assert.Null(q.Previous());
        Assert.False(q.HasPrevious);
    }

    [Fact]
    public void RepeatAllWrapsBothWays()
    {
        var q = Q(3); q.Repeat = RepeatMode.All;
        q.JumpTo(2);
        Assert.True(q.HasNext);
        Assert.Equal("t1", q.Advance()!.Title);
        Assert.Equal("t3", q.Previous()!.Title);
    }

    [Fact]
    public void RepeatOneReplaysOnItsOwnButNextStillMovesOn()
    {
        var q = Q(3); q.Repeat = RepeatMode.One;
        Assert.Equal("t1", q.Advance()!.Title);
        Assert.Equal("t1", q.Advance()!.Title);
        Assert.Equal("t2", q.Next()!.Title);
    }

    [Fact]
    public void RemovingKeepsTheRightItemPlaying()
    {
        var q = Q(5); q.JumpTo(2);                    // t3
        q.Remove(0);                                  // before current
        Assert.Equal("t3", q.CurrentItem!.Title);
        q.Remove(3);                                  // after current (t5)
        Assert.Equal("t3", q.CurrentItem!.Title);
        q.Remove(1);                                  // the current one: t4 slides into its place
        Assert.Equal("t4", q.CurrentItem!.Title);
        Assert.Equal(new[] { "t2", "t4" }, q.Items.Select(x => x.Title));
        q.Remove(1); q.Remove(0);
        Assert.Equal(0, q.Count);
        Assert.Null(q.CurrentItem);
        Assert.Null(q.Advance());
    }

    [Fact]
    public void MovingNeverChangesWhatIsPlaying()
    {
        var q = Q(5); q.JumpTo(2);
        q.Move(0, 4);
        Assert.Equal(new[] { "t2", "t3", "t4", "t5", "t1" }, q.Items.Select(x => x.Title));
        Assert.Equal("t3", q.CurrentItem!.Title);
        q.Move(4, 0);
        q.Move(2, 0);                                  // move the playing item itself
        Assert.Equal("t3", q.CurrentItem!.Title);
        Assert.Equal(0, q.Current);
    }

    [Fact]
    public void ShuffleVisitsEveryItemOnceStartingWithTheCurrentOne()
    {
        var q = Q(8); q.JumpTo(3);
        q.SetShuffle(true);
        Assert.Equal("t4", q.CurrentItem!.Title);
        var lap = Lap(q);
        Assert.Equal(8, lap.Distinct().Count());
        Assert.NotEqual(Enumerable.Range(1, 8).Select(i => "t" + i), lap); // seed 1 does reorder
        Assert.Null(q.Advance());                       // repeat off: the lap ends
    }

    [Fact]
    public void TurningShuffleOffRestoresListedOrderAndKeepsTheCurrentItem()
    {
        var q = Q(6, shuffle: true);
        q.Next(); q.Next();
        var playing = q.CurrentItem!.Title;
        q.SetShuffle(false);
        Assert.Equal(playing, q.CurrentItem!.Title);
        Assert.Equal(new[] { "t1", "t2", "t3", "t4", "t5", "t6" }, q.Items.Select(x => x.Title));
        var idx = q.Current;
        if (idx < 5) Assert.Equal("t" + (idx + 2), q.Next()!.Title);
    }

    [Fact]
    public void ShuffledRepeatAllReshufflesWithoutRepeatingTheLastItemFirst()
    {
        for (var seed = 1; seed <= 25; seed++)
        {
            var q = Q(5, shuffle: true, seed); q.Repeat = RepeatMode.All;
            var seen = new List<string> { q.CurrentItem!.Title };
            for (var n = 0; n < 14; n++) seen.Add(q.Advance()!.Title);
            for (var lap = 0; lap < 3; lap++) Assert.Equal(5, seen.Skip(lap * 5).Take(5).Distinct().Count());
            for (var n = 1; n < seen.Count; n++) Assert.NotEqual(seen[n - 1], seen[n]);
        }
    }

    [Fact]
    public void AddingNeverMovesThePlayingItemAndPlayNextGoesNext()
    {
        foreach (var shuffle in new[] { false, true })
        {
            var q = Q(4, shuffle); q.Next();
            var playing = q.CurrentItem!.Title;
            q.Add(I("new"));
            Assert.Equal(playing, q.CurrentItem!.Title);
            Assert.Equal(5, q.Count);
            q.PlayNext(I("soon"));
            Assert.Equal(playing, q.CurrentItem!.Title);
            Assert.Equal("soon", q.Next()!.Title);
        }
    }

    [Fact]
    public void AddingToAnEmptyQueueSelectsTheFirstItem()
    {
        var q = new PlaybackQueue();
        q.Add(I("a"));
        Assert.Equal("a", q.CurrentItem!.Title);
        Assert.False(q.HasNext);
    }

    [Theory]
    [InlineData("Ep2", "Ep10", -1)]
    [InlineData("ep02", "Ep2", 0)]
    [InlineData("a1b", "a1c", -1)]
    [InlineData("S01E09", "S01E10", -1)]
    [InlineData("x", "x1", -1)]
    public void NaturalOrderComparesNumbersAsNumbers(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(PlaybackQueue.Natural.Compare(a, b)));

    [Fact]
    public void FoldersExpandToTheirVideosInNaturalOrderAndNonVideosAreIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumina-q-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        try
        {
            foreach (var f in new[] { "Ep10.mkv", "Ep2.mkv", "notes.txt", "sub/Ep3.mp4", "Ep1.MKV" }) File.WriteAllText(Path.Combine(dir, f), "x");
            var single = Path.Combine(dir, "Ep10.mkv");
            var items = PlaybackQueue.FromPaths(new[] { dir, Path.Combine(dir, "missing.mkv"), Path.Combine(dir, "notes.txt") });
            Assert.Equal(new[] { "Ep1", "Ep2", "Ep10", "Ep3" }.Length, items.Count);
            Assert.Equal(new[] { "Ep1", "Ep2", "Ep10" }, items.Take(3).Select(x => x.Title));
            Assert.Single(PlaybackQueue.FromPaths(new[] { single }));
        }
        finally { Directory.Delete(dir, true); }
    }
}
