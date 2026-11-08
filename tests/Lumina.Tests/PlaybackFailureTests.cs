using System.Collections.Concurrent;
using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class PlaybackFailureTests
{
    [Fact]
    public void AMissingFileEndsWithAnErrorReasonAndALoadingFailure()
    {
        if (!Fixtures.HasMpv) return;
        using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null");
        var got = new BlockingCollection<(int Reason, int Error)>();
        m.EndFile += (r, e) => got.Add((r, e));
        m.Load(Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N") + ".mkv"));
        Assert.True(got.TryTake(out var end, 5000));
        Assert.Equal(4, end.Reason);
        Assert.Equal(-13, end.Error);
        Assert.Contains("could not be opened", Mpv.FailureText(end.Error));
    }

    [Fact]
    public void ARefusedConnectionFailsTheSameWayInsteadOfHanging()
    {
        if (!Fixtures.HasMpv) return;
        using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null\nnetwork-timeout=3");
        var got = new BlockingCollection<(int Reason, int Error)>();
        m.EndFile += (r, e) => got.Add((r, e));
        m.Load("http://127.0.0.1:1/video.mkv");
        Assert.True(got.TryTake(out var end, 15000));
        Assert.Equal(4, end.Reason);
    }

    [Fact]
    public void ANormalEndIsNotAnError()
    {
        if (!Fixtures.HasMpv) return;
        using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null\nkeep-open=no");
        var got = new BlockingCollection<(int Reason, int Error)>();
        m.EndFile += (r, e) => got.Add((r, e));
        m.Load(Fixtures.Path_("library/Movies/Alpha Movie (1999).mkv"));
        Assert.True(got.TryTake(out var end, 15000));
        Assert.Equal(0, end.Reason);                                // reached the end
        Assert.Equal(0, end.Error);
    }

    [Theory]
    [InlineData(-13, "could not be opened")]
    [InlineData(-14, "sound output")]
    [InlineData(-15, "video output")]
    [InlineData(-16, "nothing in that file")]
    [InlineData(-17, "does not recognise")]
    [InlineData(-18, "not supported")]
    [InlineData(-999, "error (-999)")]
    public void ErrorCodesBecomeSentences(int code, string fragment) => Assert.Contains(fragment, Mpv.FailureText(code));
}
