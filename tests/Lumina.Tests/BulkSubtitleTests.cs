using System.IO;
using System.Net;
using System.Text;
using Lumina.Library;
using Lumina.Subtitles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

public class BulkRulesTests
{
    static BulkItem I(string name) => new(@"C:\v\" + name + ".mkv", name, null, null, null);
    static readonly IReadOnlyList<BulkItem> Four = new[] { I("a"), I("b"), I("c"), I("d") };

    static Func<BulkItem, Task<SubResult>> Script(Dictionary<string, SubResult> by) => it => Task.FromResult(by[it.Title]);

    [Fact]
    public async Task EveryVideoIsCountedInOneOfTheFourWays()
    {
        var r = await BulkSubtitles.RunAsync(Four, Script(new()
        {
            ["a"] = new SubResult(@"C:\s\a.srt", "ok"),
            ["b"] = new SubResult(null, "No subtitles found on OpenSubtitles"),
            ["c"] = new SubResult(null, "OpenSubtitles unreachable"),
            ["d"] = new SubResult(@"C:\s\d.srt", "ok"),
        }), _ => false);
        Assert.Equal((2, 0, 1, 1, null, 4), (r.Found, r.AlreadyHad, r.NotFound, r.Failed, r.Stopped, r.Total));
        Assert.Equal(4, r.Done);
        Assert.Equal("2 found, 1 not found, 1 failed", r.Summary);
    }

    [Fact]
    public async Task VideosThatAlreadyHaveSubtitlesAreSkippedWithoutAsking()
    {
        var asked = new List<string>();
        var r = await BulkSubtitles.RunAsync(Four, it => { asked.Add(it.Title); return Task.FromResult(new SubResult(@"C:\s.srt", "ok")); }, p => p.EndsWith("a.mkv") || p.EndsWith("c.mkv"));
        Assert.Equal(new[] { "b", "d" }, asked);
        Assert.Equal((2, 2), (r.Found, r.AlreadyHad));
        Assert.Equal("2 found, 2 already had them", r.Summary);
    }

    [Theory]
    [InlineData("OpenSubtitles daily download limit reached: resets at midnight")]
    [InlineData("OpenSubtitles rejected the API key")]
    [InlineData("Add an OpenSubtitles key in Settings to search for subtitles")]
    public async Task ADailyLimitABadKeyOrNoKeyStopsTheWholeRunAtOnce(string message)
    {
        var asked = 0;
        var r = await BulkSubtitles.RunAsync(Four, _ => { asked++; return Task.FromResult(asked == 1 ? new SubResult(@"C:\s.srt", "ok") : new SubResult(null, message)); }, _ => false);
        Assert.Equal(2, asked);                                  // the one that worked and the one that hit the wall; nothing after
        Assert.Equal(message, r.Stopped);
        Assert.Equal(1, r.Found);
        Assert.Contains("Stopped:", r.Summary);
    }

    [Fact]
    public async Task ThreeFailuresInARowStopItButTwoWithASuccessBetweenDoNot()
    {
        var fail = new SubResult(null, "OpenSubtitles unreachable");
        var ok = new SubResult(@"C:\s.srt", "ok");
        var items = Enumerable.Range(0, 6).Select(i => I("x" + i)).ToList();
        var script = new Queue<SubResult>(new[] { fail, fail, ok, fail, fail, ok });
        var r = await BulkSubtitles.RunAsync(items, _ => Task.FromResult(script.Dequeue()), _ => false);
        Assert.Null(r.Stopped);
        Assert.Equal((2, 4), (r.Found, r.Failed));

        var all = new Queue<SubResult>(new[] { fail, fail, fail, ok, ok, ok });
        var r2 = await BulkSubtitles.RunAsync(items, _ => Task.FromResult(all.Dequeue()), _ => false);
        Assert.Equal("OpenSubtitles unreachable", r2.Stopped);
        Assert.Equal(3, r2.Failed);
        Assert.Equal(0, r2.Found);
    }

    [Fact]
    public async Task ACancelStopsBeforeTheNextVideoAndSaysSo()
    {
        using var cts = new CancellationTokenSource();
        var n = 0;
        var r = await BulkSubtitles.RunAsync(Four, _ => { if (++n == 2) cts.Cancel(); return Task.FromResult(new SubResult(@"C:\s.srt", "ok")); }, _ => false, cancel: cts.Token);
        Assert.Equal(2, r.Found);
        Assert.Equal("cancelled", r.Stopped);
    }

    [Fact]
    public async Task ExceptionsFromTheNetworkCountAsFailuresNotCrashes()
    {
        var r = await BulkSubtitles.RunAsync(new[] { I("a"), I("b") }, it => it.Title == "a" ? throw new IOException("disk") : Task.FromResult(new SubResult(@"C:\s.srt", "ok")), _ => false);
        Assert.Equal((1, 1), (r.Found, r.Failed));
    }

    [Fact]
    public async Task ProgressReportsEachVideoInOrderAndAnEmptyListDoesNothing()
    {
        var seen = new List<(int, int, string)>();
        await BulkSubtitles.RunAsync(Four, _ => Task.FromResult(new SubResult(@"C:\s.srt", "ok")), _ => false, (i, n, t) => seen.Add((i, n, t)));
        Assert.Equal(new[] { (1, 4, "a"), (2, 4, "b"), (3, 4, "c"), (4, 4, "d") }, seen);
        var none = await BulkSubtitles.RunAsync(Array.Empty<BulkItem>(), _ => throw new InvalidOperationException("must not be called"), _ => false);
        Assert.Equal("Nothing to do", none.Summary);
    }

    [Fact]
    public async Task ThePauseBetweenRequestsIsHonouredButNotAfterTheLastOne()
    {
        var pauses = new List<TimeSpan>();
        await BulkSubtitles.RunAsync(new[] { I("a"), I("b"), I("c") }, _ => Task.FromResult(new SubResult(@"C:\s.srt", "ok")), _ => false,
            pause: TimeSpan.FromMilliseconds(300), delay: t => { pauses.Add(t); return Task.CompletedTask; });
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300) }, pauses);     // two gaps between three requests, none after the last
    }

    [Fact]
    public async Task NoPauseIsTakenForVideosThatAlreadyHaveSubtitlesOrWhenNoneIsSet()
    {
        var pauses = 0;
        await BulkSubtitles.RunAsync(new[] { I("a"), I("b") }, _ => Task.FromResult(new SubResult(@"C:\s.srt", "ok")), _ => true, pause: TimeSpan.FromSeconds(1), delay: _ => { pauses++; return Task.CompletedTask; });
        await BulkSubtitles.RunAsync(new[] { I("a"), I("b") }, _ => Task.FromResult(new SubResult(@"C:\s.srt", "ok")), _ => false, delay: _ => { pauses++; return Task.CompletedTask; });
        Assert.Equal(0, pauses);
    }

    [Theory]
    [InlineData("OpenSubtitles daily download limit reached", true)]
    [InlineData("OpenSubtitles rejected the API key", true)]
    [InlineData("Add an OpenSubtitles key in Settings to search for subtitles", true)]
    [InlineData("OpenSubtitles unreachable", false)]
    [InlineData("OpenSubtitles timed out", false)]
    [InlineData("No subtitles found on OpenSubtitles", false)]
    public void OnlyTheHopelessMessagesAreFatal(string message, bool fatal) => Assert.Equal(fatal, BulkSubtitles.IsFatal(message));
}

public class SidecarTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-side-" + Guid.NewGuid().ToString("N"));
    public SidecarTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    string Video(string name) { var p = Path.Combine(_dir, name); File.WriteAllText(p, "x"); return p; }
    void Sub(string name) => File.WriteAllText(Path.Combine(_dir, name), "1\n00:00:01,000 --> 00:00:02,000\nhi\n");

    [Theory]
    [InlineData("Movie.srt", true)]
    [InlineData("Movie.en.srt", true)]
    [InlineData("movie.ENGLISH.forced.SRT", true)]
    [InlineData("Movie.ass", true)]
    [InlineData("Movie 2.srt", false)]            // another video's subtitles
    [InlineData("Movies.srt", false)]
    [InlineData("Movie.txt", false)]
    [InlineData("Other.srt", false)]
    public void ASubtitleFileNextToTheVideoWithTheSameStemCounts(string sub, bool expected)
    {
        var v = Video("Movie.mkv");
        Sub(sub);
        Assert.Equal(expected, OpenSubtitles.HasSidecar(v));
    }

    [Fact]
    public void NoFilesAtAllOrAMissingFolderIsNo()
    {
        Assert.False(OpenSubtitles.HasSidecar(Video("Movie.mkv")));
        Assert.False(OpenSubtitles.HasSidecar(Path.Combine(_dir, "nope", "x.mkv")));
        Assert.False(OpenSubtitles.HasSidecar("x.mkv"));
    }
}

/// <summary>The real search and download code against a fake OpenSubtitles.</summary>
[Collection("db")]
public class OpenSubtitlesFlowTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-osflow-" + Guid.NewGuid().ToString("N"));
    readonly Dictionary<string, string?> _old = new();
    readonly FakeWeb _web;

    public OpenSubtitlesFlowTests()
    {
        Directory.CreateDirectory(_dir);
        _web = new FakeWeb { Handler = Handle };
        foreach (var (k, v) in new Dictionary<string, string> { ["LUMINA_DB"] = Path.Combine(_dir, "t.db"), ["LUMINA_CONFIG"] = Path.Combine(_dir, "cfg.json"),
                     ["LUMINA_SUBS"] = Path.Combine(_dir, "subs"), ["LUMINA_OPENSUBTITLES_API"] = _web.Base })
        { _old[k] = Environment.GetEnvironmentVariable(k); Environment.SetEnvironmentVariable(k, v); }
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), "{\"openSubtitlesKey\":\"testkey\",\"subtitleLanguages\":\"en,es\"}");
        Db.Init();
    }

    public void Dispose()
    {
        _web.Dispose();
        foreach (var (k, v) in _old) Environment.SetEnvironmentVariable(k, v);
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    static string Hit(long id, string lang, string release, int downloads = 10, bool hash = false) =>
        $"{{\"attributes\":{{\"language\":\"{lang}\",\"release\":\"{release}\",\"download_count\":{downloads},\"moviehash_match\":{hash.ToString().ToLower()},\"files\":[{{\"file_id\":{id}}}]}}}}";

    (int, string, int) Handle(HttpListenerRequest r)
    {
        var path = r.Url!.AbsolutePath; var q = r.Url.Query;
        if (path.EndsWith("/subtitles"))
        {
            if (q.Contains("query=alpha")) return (200, "{\"data\":[" + Hit(1, "es", "Alpha.ES", 500) + "," + Hit(2, "en", "Alpha.EN.low", 5) + "," + Hit(3, "en", "Alpha.EN.best", 900) + "]}", 0);
            if (q.Contains("query=beta")) return (200, "{\"data\":[]}", 0);
            if (q.Contains("query=gamma")) return (406, "{\"message\":\"You have downloaded your allowed 20 subtitles\"}", 0);
            if (q.Contains("query=delta")) return (401, "{}", 0);
            return (200, "{\"data\":[" + Hit(9, "en", "Other") + "]}", 0);
        }
        if (path.EndsWith("/download")) return (200, "{\"link\":\"" + _web.Base + "files/dl.srt\"}", 0);
        if (path.EndsWith("/files/dl.srt")) return (200, "1\n00:00:01,000 --> 00:00:02,000\nhello\n", 0);
        return (404, "{}", 0);
    }

    string Video(string name) { var p = Path.Combine(_dir, name); File.WriteAllText(p, "x"); return p; }

    [Fact]
    public async Task TheSearchListsEveryCandidateBestFirstInTheUsersLanguageOrder()
    {
        var hits = await OpenSubtitles.SearchAsync(Video("Alpha.mkv"), "Alpha", null, null, null);
        Assert.Equal(new long[] { 3, 2, 1 }, hits.Select(h => h.FileId));               // English (first language) by quality, then Spanish
        Assert.Contains("Alpha.EN.best", OpenSubtitles.Describe(hits[0]));
        Assert.Contains("900 downloads", OpenSubtitles.Describe(hits[0]));
    }

    [Fact]
    public async Task ChoosingOneDownloadsItAndMakesItTheFilesSubtitleForNextTime()
    {
        var video = Video("Alpha.mkv");
        var hits = await OpenSubtitles.SearchAsync(video, "Alpha", null, null, null);
        var path = await OpenSubtitles.ChooseAsync(video, hits[1]);
        Assert.True(File.Exists(path));
        Assert.Contains("hello", File.ReadAllText(path));
        Assert.Equal(path, OpenSubtitles.Cached(video, null));                          // a small file has no hash, so the path is the key
    }

    [Fact]
    public async Task BulkRunsTheRealCodeFindsStoresAndStopsAtTheDailyLimit()
    {
        var items = new[] { "Alpha", "Beta", "Gamma", "Epsilon" }.Select(n => new BulkItem(Video(n + ".mkv"), n, null, null, null)).ToList();
        var r = await BulkSubtitles.RunAsync(items, i => OpenSubtitles.FetchAsync(i.Path, i.Title, i.Year, i.Season, i.Episode), p => OpenSubtitles.Cached(p, null) != null || OpenSubtitles.HasSidecar(p));
        Assert.Equal((1, 1), (r.Found, r.NotFound));
        Assert.Contains("daily download limit", r.Stopped);
        Assert.NotNull(OpenSubtitles.Cached(items[0].Path, null));                      // Alpha was stored
        Assert.Null(OpenSubtitles.Cached(items[3].Path, null));                         // the run stopped before Epsilon
        // run it again: Alpha is now skipped, Beta is asked again, Gamma stops it again
        var again = await BulkSubtitles.RunAsync(items, i => OpenSubtitles.FetchAsync(i.Path, i.Title, i.Year, i.Season, i.Episode), p => OpenSubtitles.Cached(p, null) != null || OpenSubtitles.HasSidecar(p));
        Assert.Equal(1, again.AlreadyHad);
        Assert.Equal(0, again.Found);
    }

    [Fact]
    public async Task ARejectedKeyIsReportedInPlainWords()
    {
        var r = await OpenSubtitles.FetchAsync(Video("Delta.mkv"), "Delta", null, null, null);
        Assert.Null(r.Path);
        Assert.Equal("OpenSubtitles rejected the API key", r.Message);
    }

    [Fact]
    public async Task NoKeyMeansASearchRefusesBeforeAnyRequest()
    {
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), "{}");
        await Assert.ThrowsAsync<OpenSubtitles.SubsException>(() => OpenSubtitles.SearchAsync(Video("Alpha.mkv"), "Alpha", null, null, null));
        Assert.Empty(_web.Requests);
    }
}
