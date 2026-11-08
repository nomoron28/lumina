using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]    // uses LUMINA_THUMBS
public class ThumbnailTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-thumbs-" + Guid.NewGuid().ToString("N"));
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_THUMBS");

    public ThumbnailTests() { Directory.CreateDirectory(_dir); Environment.SetEnvironmentVariable("LUMINA_THUMBS", Path.Combine(_dir, "cache")); }
    public void Dispose() { Environment.SetEnvironmentVariable("LUMINA_THUMBS", _old); try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(12, 12)]          // short clips get the minimum
    [InlineData(600, 60)]         // ten minutes: one every ten seconds
    [InlineData(3600, 120)]       // an hour and over: capped
    [InlineData(20000, 120)]
    public void ThePlanIsOneEveryTenSecondsWithinLimits(double seconds, int count) => Assert.Equal(count, Thumbnails.Plan(seconds).Count);

    [Fact]
    public void PicturesStandForTheMiddleOfTheirSliceAndTheStepCoversTheWholeVideo()
    {
        var (count, step) = Thumbnails.Plan(600);
        Assert.Equal(600, count * step, 6);
        Assert.Equal(5, Thumbnails.TimeOf(0, 10));
        Assert.Equal(15, Thumbnails.TimeOf(1, 10));
    }

    [Fact]
    public void NearestGivesTheSliceThenTheClosestEarlierPictureAndNullWhenNothingExistsYet()
    {
        var d = Path.Combine(_dir, "set"); Directory.CreateDirectory(d);
        Assert.Null(Thumbnails.Nearest(d, 10, 10, 35));
        File.WriteAllText(Thumbnails.FramePath(d, 1), "x");
        File.WriteAllText(Thumbnails.FramePath(d, 3), "x");
        Assert.Equal(Thumbnails.FramePath(d, 3), Thumbnails.Nearest(d, 10, 10, 35));    // slice 3 exists
        Assert.Equal(Thumbnails.FramePath(d, 3), Thumbnails.Nearest(d, 10, 10, 59));    // slice 5 does not exist yet: the earlier one
        Assert.Equal(Thumbnails.FramePath(d, 1), Thumbnails.Nearest(d, 10, 10, 25));    // slice 2 is missing: back to slice 1
        Assert.Null(Thumbnails.Nearest(d, 10, 10, 5));                                  // before the first picture
        Assert.Equal(Thumbnails.FramePath(d, 3), Thumbnails.Nearest(d, 10, 10, 9999));  // past the end is the last slice, then earlier
        Assert.Equal(Thumbnails.FramePath(d, 1), Thumbnails.Nearest(d, 10, 10, -5) ?? Thumbnails.FramePath(d, 1));
        Assert.Null(Thumbnails.Nearest(d, 0, 0, 5));
    }

    [Fact]
    public void TheFolderChangesWhenTheFileChangesAndIsStableOtherwise()
    {
        var f = Path.Combine(_dir, "a.mkv");
        File.WriteAllText(f, "one");
        var first = Thumbnails.FolderFor(f);
        Assert.Equal(first, Thumbnails.FolderFor(f));
        File.WriteAllText(f, "two-two");
        Assert.NotEqual(first, Thumbnails.FolderFor(f));
        Assert.StartsWith(Thumbnails.Root, first);
    }

    [Fact]
    public void TheCacheIsTrimmedOldestFirstDownToTheLimit()
    {
        var root = Thumbnails.Root;
        for (var i = 0; i < 5; i++)
        {
            var d = Path.Combine(root, "f" + i); Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, "0000.jpg"), new byte[1000]);
            Directory.SetLastWriteTimeUtc(d, DateTime.UtcNow.AddMinutes(-(10 - i)));     // f0 is the oldest
        }
        Assert.Equal(0, Thumbnails.Trim(5000));
        Assert.Equal(2, Thumbnails.Trim(3000));
        Assert.Equal(new[] { "f2", "f3", "f4" }, new DirectoryInfo(root).GetDirectories().Select(x => x.Name).OrderBy(n => n));
        Assert.Equal(0, Thumbnails.Trim(10_000_000));
    }

    [Fact]
    public void TrimmingNothingIsHarmless() => Assert.Equal(0, Thumbnails.Trim(0));

    [Fact]
    public void ARealVideoGivesAPictureForEverySliceAtTheRightSizeAndASecondCallDoesNoWork()
    {
        if (!Fixtures.HasMpv) return;
        var video = Fixtures.Path_("multi.mkv");
        var (count, _) = Thumbnails.Plan(12.0);
        var seen = new List<int>();
        Assert.True(Thumbnails.Generate(video, 12.0, CancellationToken.None, seen.Add));
        var dir = Thumbnails.FolderFor(video);
        Assert.True(Thumbnails.IsComplete(dir));
        for (var i = 0; i < count; i++)
        {
            var p = Thumbnails.FramePath(dir, i);
            Assert.True(File.Exists(p), $"picture {i} is missing");
            var bytes = File.ReadAllBytes(p);
            Assert.True(bytes.Length > 300 && bytes[0] == 0xFF && bytes[1] == 0xD8, $"picture {i} is not a JPEG");
            Assert.InRange(JpegWidth(bytes), Thumbnails.Width - 2, Thumbnails.Width + 2);       // mpv may round the width by a pixel
        }
        Assert.Equal(count, seen.Max());
        Assert.False(Directory.Exists(Path.Combine(dir, "work")));          // the scratch folder is gone

        var stamp = File.GetLastWriteTimeUtc(Thumbnails.FramePath(dir, 0));
        seen.Clear();
        Assert.True(Thumbnails.Generate(video, 12.0, CancellationToken.None, seen.Add));
        Assert.Empty(seen);                                                  // nothing was rendered again
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Thumbnails.FramePath(dir, 0)));
    }

    [Fact]
    public void CancellingStopsPromptlyAndLeavesAFolderThatIsNotMarkedComplete()
    {
        if (!Fixtures.HasMpv) return;
        var video = Fixtures.Path_("multi.mkv");
        using var cts = new CancellationTokenSource();
        var n = 0;
        var ok = Thumbnails.Generate(video, 12.0, cts.Token, made => { if (++n == 3) cts.Cancel(); });
        Assert.False(ok);
        Assert.False(Thumbnails.IsComplete(Thumbnails.FolderFor(video)));
        Assert.InRange(n, 3, 4);                                             // it did not carry on
    }

    [Fact]
    public void AFileThatCannotBeOpenedOrIsMissingGivesNoPicturesAndNoCrash()
    {
        Assert.False(Thumbnails.Generate(Path.Combine(_dir, "missing.mkv"), 60, CancellationToken.None));
        Assert.False(Thumbnails.Generate(Path.Combine(_dir, "missing.mkv"), 0, CancellationToken.None));
        if (!Fixtures.HasMpv) return;
        var junk = Path.Combine(_dir, "junk.mkv");
        File.WriteAllText(junk, "not a video at all");
        Assert.False(Thumbnails.Generate(junk, 60, CancellationToken.None));
        Assert.False(Thumbnails.IsComplete(Thumbnails.FolderFor(junk)));
    }

    /// <summary>Width from a JPEG's start-of-frame marker.</summary>
    static int JpegWidth(byte[] b)
    {
        for (var i = 2; i + 9 < b.Length;)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marker = b[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC) return (b[i + 7] << 8) | b[i + 8];
            i += 2 + ((b[i + 2] << 8) | b[i + 3]);
        }
        return -1;
    }
}

/// <summary>What the controller tells the host while the pointer is on the seek bar.</summary>
public class ThumbnailMessageTests
{
    static List<string[]> Collect(Mpv m)
    {
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        return got;
    }

    static string[][] Snapshot(List<string[]> got) { lock (got) return got.Where(a => a.Length > 0 && a[0].StartsWith("lumina-thumb")).ToArray(); }

    static bool WaitFor(Func<bool> ok) { var until = DateTime.UtcNow.AddSeconds(5); while (DateTime.UtcNow < until) { if (ok()) return true; Thread.Sleep(30); } return false; }

    [Fact]
    public void PointingAtATimeSendsThatTimeAndSpotOnceAndTheTimeIsPublished()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = Collect(m);
        m.Command("script-message", "lumina-do", "thumb-test", "4.5");
        Assert.True(WaitFor(() => Snapshot(got).Length == 1));
        Assert.Equal(new[] { "lumina-thumb", "4.50", "400", "300" }, Snapshot(got)[0]);
        Assert.Equal("4.50", m.Get("user-data/lumina/thumb_time")?.Trim('"'));
    }

    [Fact]
    public void LeavingTheBarHidesOnceAndTwiceIsHarmless()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = Collect(m);
        m.Command("script-message", "lumina-do", "thumb-test", "off");               // nothing was showing: nothing to hide
        m.Command("script-message", "lumina-do", "thumb-test", "2");
        m.Command("script-message", "lumina-do", "thumb-test", "off");
        m.Command("script-message", "lumina-do", "thumb-test", "off");
        Assert.True(WaitFor(() => Snapshot(got).Length >= 2));
        Thread.Sleep(300);
        Assert.Equal(new[] { "lumina-thumb", "lumina-thumb-hide" }, Snapshot(got).Select(a => a[0]).ToArray());
        Assert.Equal("", m.Get("user-data/lumina/thumb_time")?.Trim('"'));
    }

    [Fact]
    public void MovingAlongTheBarSendsEachTime()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = Collect(m);
        foreach (var t in new[] { "1", "2.25", "9.99" }) m.Command("script-message", "lumina-do", "thumb-test", t);
        Assert.True(WaitFor(() => Snapshot(got).Length == 3));
        Assert.Equal(new[] { "1.00", "2.25", "9.99" }, Snapshot(got).Select(a => a[1]).ToArray());
    }

    [Fact]
    public void NonsenseTimesAreIgnored()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = Collect(m);
        m.Command("script-message", "lumina-do", "thumb-test", "banana");              // not a number counts as "off", and nothing was showing
        Thread.Sleep(400);
        Assert.Empty(Snapshot(got));
    }
}

[Collection("db")]
public class ThumbnailSettingTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), "lumina-thsetting-" + Guid.NewGuid().ToString("N") + ".json");
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_CONFIG");
    public ThumbnailSettingTests() => Environment.SetEnvironmentVariable("LUMINA_CONFIG", _file);
    public void Dispose() { Environment.SetEnvironmentVariable("LUMINA_CONFIG", _old); File.Delete(_file); }

    [Fact]
    public void OnByDefaultAndOnlyTheOffSwitchReachesTheController()
    {
        Assert.Equal("true", Lumina.Settings.Catalog.Find("interface.seekThumbs")!.Default);
        Assert.DoesNotContain(Lumina.Settings.AppSettings.MpvLines(), l => l.Contains("lumina-osc-thumbs"));
        Lumina.Settings.AppSettings.Set("interface.seekThumbs", "false");
        Assert.Contains(Lumina.Settings.AppSettings.MpvLines(), l => l.StartsWith("script-opts=") && l.Contains("lumina-osc-thumbs=no"));
    }
}
