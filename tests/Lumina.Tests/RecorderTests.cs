using System.IO;
using System.Net;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class RecorderNameTests
{
    [Theory]
    [InlineData("Show Name", "Show Name")]
    [InlineData("A/B\\C:D*E?F\"G<H>I|J", "A B C D E F G H I J")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("ends with dots...", "ends with dots")]
    [InlineData("", "Recording")]
    [InlineData(null, "Recording")]
    [InlineData("???", "Recording")]
    [InlineData("CON", "Recording CON")]
    [InlineData("nul.txt", "Recording nul.txt")]
    [InlineData("COM3", "Recording COM3")]
    [InlineData("COMET", "COMET")]
    [InlineData("tab\there", "tab here")]
    public void NamesAreSafeOnWindows(string? title, string expected) => Assert.Equal(expected, Recorder.SafeName(title));

    [Fact]
    public void ALongTitleIsCutAndStillValid()
    {
        var n = Recorder.SafeName(new string('x', 300));
        Assert.Equal(80, n.Length);
        var cut = Recorder.SafeName(new string('y', 79) + " " + new string('z', 50));
        Assert.True(cut.Length <= 80 && cut == cut.TrimEnd(), "cut to length without a trailing space");
    }

    [Fact]
    public void FileNamesCarryTheDateAndTimeAndTheRightExtension()
    {
        var t = new DateTime(2026, 10, 17, 3, 5, 9);
        Assert.Equal("News 2026-10-17 03-05-09.mkv", Recorder.FileNameFor("News", t));
        Assert.Equal("News 2026-10-17 03-05-09.ts", Recorder.FileNameFor("News", t, ".ts"));
        Assert.Equal("Recording 2026-10-17 03-05-09.mkv", Recorder.FileNameFor("", t));
        Assert.DoesNotContain(':', Recorder.FileNameFor("x", t));
    }

    [Fact]
    public void ARecordingNeverOverwritesAnEarlierOne()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumina-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(Path.Combine(dir, "a.mkv"), Recorder.Unique(dir, "a.mkv"));
            File.WriteAllText(Path.Combine(dir, "a.mkv"), "1");
            Assert.Equal(Path.Combine(dir, "a (2).mkv"), Recorder.Unique(dir, "a.mkv"));
            File.WriteAllText(Path.Combine(dir, "a (2).mkv"), "2");
            Assert.Equal(Path.Combine(dir, "a (3).mkv"), Recorder.Unique(dir, "a.mkv"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(-1, true)]                     // unknown space is not a reason to refuse
    [InlineData(0, false)]
    [InlineData(499L * 1024 * 1024, false)]
    [InlineData(500L * 1024 * 1024, true)]
    [InlineData(5000L * 1024 * 1024, true)]
    public void TheDiskSpaceGuardWantsAtLeastHalfAGigabyte(long free, bool ok) => Assert.Equal(ok, Recorder.HasRoom(free));

    [Fact]
    public void TheFolderIsTheSettingOrVideosLuminaRecordings()
    {
        Assert.Equal(@"D:\Rec", Recorder.FolderFor(@"  D:\Rec  "));
        Assert.EndsWith("Lumina recordings", Recorder.FolderFor(""));
        Assert.Equal(Recorder.DefaultFolder, Recorder.FolderFor(null));
    }

    [Fact]
    public void AMissingDriveIsUnknownSpaceNotACrash() => Assert.True(Recorder.FreeSpace(@"Q:\does\not\exist") is -1 or >= 0);
}

/// <summary>A real recording from a local web server, with range requests like a streaming server.</summary>
public class RecorderStreamTests
{
    static (HttpListener Listener, string Url) Serve(byte[] data)
    {
        var (listener, baseUrl) = TestNet.StartListener("localhost");
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext c;
                try { c = await listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(() =>
                {
                    try
                    {
                        var range = c.Request.Headers["Range"];
                        long from = 0, to = data.Length - 1; var status = 200;
                        if (range != null && range.StartsWith("bytes="))
                        {
                            var p = range[6..].Split('-');
                            from = long.Parse(p[0]); if (p.Length > 1 && p[1].Length > 0) to = Math.Min(long.Parse(p[1]), data.Length - 1);
                            status = 206; c.Response.AddHeader("Content-Range", $"bytes {from}-{to}/{data.Length}");
                        }
                        c.Response.StatusCode = status; c.Response.AddHeader("Accept-Ranges", "bytes"); c.Response.ContentType = "video/x-matroska";
                        c.Response.ContentLength64 = to - from + 1;
                        // A real stream trickles in and stays open; recording only keeps what arrives after it starts. So send it slowly, in pieces.
                        var left = (int)(to - from + 1); var at = (int)from;
                        while (left > 0)
                        {
                            var n = Math.Min(24 * 1024, left);
                            c.Response.OutputStream.Write(data, at, n); c.Response.OutputStream.Flush();
                            at += n; left -= n; Thread.Sleep(90);
                        }
                        c.Response.Close();
                    }
                    catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException or InvalidOperationException) { }
                });
            }
        });
        return (listener, baseUrl + "stream.mkv");
    }

    static Mpv OpenStream(string url)
    {
        var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null\npause=no");
        using var loaded = new ManualResetEventSlim();
        m.FileLoaded += () => loaded.Set();
        m.Load(url);
        if (!loaded.Wait(10_000)) { m.Dispose(); throw new TimeoutException("the test stream did not open"); }
        return m;
    }

    [Fact]
    public void AStreamIsSavedToTheFolderAndStopGivesItsSize()
    {
        if (!Fixtures.HasMpv) return;
        var (listener, url) = Serve(File.ReadAllBytes(Fixtures.Path_("multi.mkv")));
        var dir = Path.Combine(Path.GetTempPath(), "lumina-rec-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var m = OpenStream(url);
            var (path, error) = Recorder.Start(m, url, dir, "Live: News/Evening", new DateTime(2026, 10, 17, 3, 0, 0));
            Assert.Null(error);
            Assert.StartsWith(dir, path!);
            Assert.EndsWith("Live News Evening 2026-10-17 03-00-00.mkv", path);
            Thread.Sleep(2500);
            var size = Recorder.Stop(m, path!);
            Assert.True(size > 100_000, $"only {size} bytes were saved");
            Assert.Equal("", m.Get("stream-record") ?? "");
        }
        finally { listener.Close(); try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void AFileOnDiskIsRefusedWithAnExplanationAndNothingIsCreated()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var dir = Path.Combine(Path.GetTempPath(), "lumina-rec-" + Guid.NewGuid().ToString("N"));
        var (path, error) = Recorder.Start(m, Fixtures.Path_("multi.mkv"), dir, "x", DateTime.Now);
        Assert.Null(path);
        Assert.Contains("already a file on your disk", error);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void NotEnoughSpaceRefusesBeforeAnythingIsWritten()
    {
        if (!Fixtures.HasMpv) return;
        var (listener, url) = Serve(File.ReadAllBytes(Fixtures.Path_("multi.mkv")));
        var dir = Path.Combine(Path.GetTempPath(), "lumina-rec-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var m = OpenStream(url);
            var (path, error) = Recorder.Start(m, url, dir, "x", DateTime.Now, _ => 10L * 1024 * 1024);
            Assert.Null(path);
            Assert.Contains("Not enough free space", error);
            Assert.Equal("", m.Get("stream-record") ?? "");
        }
        finally { listener.Close(); try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void AFolderThatCannotBeMadeGivesAPlainError()
    {
        if (!Fixtures.HasMpv) return;
        var (listener, url) = Serve(File.ReadAllBytes(Fixtures.Path_("multi.mkv")));
        try
        {
            using var m = OpenStream(url);
            var file = Path.Combine(Path.GetTempPath(), "lumina-rec-file-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(file, "x");                                              // a file where the folder should be
            var (path, error) = Recorder.Start(m, url, Path.Combine(file, "sub"), "x", DateTime.Now);
            Assert.Null(path);
            Assert.StartsWith("Lumina cannot use the recordings folder", error);
            File.Delete(file);
        }
        finally { listener.Close(); }
    }

    [Fact]
    public void RecordingTwiceInTheSameSecondNeverOverwrites()
    {
        if (!Fixtures.HasMpv) return;
        var (listener, url) = Serve(File.ReadAllBytes(Fixtures.Path_("multi.mkv")));
        var dir = Path.Combine(Path.GetTempPath(), "lumina-rec-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var m = OpenStream(url);
            var t = new DateTime(2026, 10, 17, 3, 0, 0);
            var (first, _) = Recorder.Start(m, url, dir, "x", t);
            Thread.Sleep(1200);
            Recorder.Stop(m, first!);
            var (second, _) = Recorder.Start(m, url, dir, "x", t);
            Assert.NotEqual(first, second);
            Assert.EndsWith("(2).mkv", second);
            Recorder.Stop(m, second!);
        }
        finally { listener.Close(); try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

/// <summary>What the player panel does for recording: ask the app, and show what it is told.</summary>
public class RecordControllerTests
{
    static bool WaitFor(Func<bool> ok) { var until = DateTime.UtcNow.AddSeconds(5); while (DateTime.UtcNow < until) { if (ok()) return true; Thread.Sleep(30); } return false; }

    [Fact]
    public void TheRowAsksTheAppToToggleRecording()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        m.Command("script-message", "lumina-do", "record");
        Assert.True(WaitFor(() => { lock (got) return got.Any(a => a.Length > 0 && a[0] == "lumina-record"); }));
    }

    [Fact]
    public void TheStateTheAppSendsIsPublishedAndNonsenseCountsAsNotRecording()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-record-state", "yes");
        Assert.True(WaitFor(() => m.Get("user-data/lumina/recording")?.Trim('"') == "yes"));
        m.Command("script-message", "lumina-record-state", "maybe");
        Assert.True(WaitFor(() => m.Get("user-data/lumina/recording")?.Trim('"') == "no"));
    }

    [Fact]
    public void TheFolderSettingIsEmptyByDefaultSoTheDefaultFolderApplies()
    {
        var d = Lumina.Settings.Catalog.Find("recording.folder")!;
        Assert.Equal("", d.Default);
        Assert.Equal("network", d.Category);
    }
}
