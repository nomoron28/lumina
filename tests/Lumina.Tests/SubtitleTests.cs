using System.IO;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;
using Lumina.Player;
using Lumina.Subtitles;
using Xunit;

namespace Lumina.Tests;

public class SubtitleHelperTests
{
    static string Temp(byte[] data)
    {
        var p = Path.Combine(Path.GetTempPath(), "lumina-hash-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(p, data);
        return p;
    }

    [Fact]
    public void HashOfZeroFileIsItsSize()
    {
        var p = Temp(new byte[200_000]);
        try { Assert.Equal((200_000UL).ToString("x16"), OpenSubtitles.Hash(p)); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void HashAddsFirstAndLastWordsLittleEndianAndWraps()
    {
        var d = new byte[200_000];
        d[0] = 1;                                   // first word = 1
        BitConverter.GetBytes(2UL).CopyTo(d, 200_000 - 8); // last word = 2
        var p = Temp(d);
        try { Assert.Equal((200_000UL + 3).ToString("x16"), OpenSubtitles.Hash(p)); }
        finally { File.Delete(p); }

        var w = new byte[200_000];
        Array.Fill(w, (byte)0xFF, 0, 8); // first word = 2^64-1, so total wraps to size-1
        p = Temp(w);
        try { Assert.Equal((200_000UL - 1).ToString("x16"), OpenSubtitles.Hash(p)); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void HashIgnoresMiddleAndRejectsTinyFiles()
    {
        var d = new byte[300_000];
        d[150_000] = 99; // outside both 64 KiB windows
        var p = Temp(d);
        var q = Temp(new byte[1000]);
        try
        {
            Assert.Equal((300_000UL).ToString("x16"), OpenSubtitles.Hash(p));
            Assert.Null(OpenSubtitles.Hash(q));
        }
        finally { File.Delete(p); File.Delete(q); }
    }

    [Fact]
    public void CanonicalQueryIsSortedLowercasePlusSpaces()
    {
        var q = OpenSubtitles.CanonicalQuery(new Dictionary<string, string>
        {
            ["year"] = "1999", ["query"] = "The Matrix", ["languages"] = "en", ["season_number"] = "", ["moviehash"] = "ABCDEF",
        });
        Assert.Equal("languages=en&moviehash=abcdef&query=the+matrix&year=1999", q);
    }

    static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void ParsesSearchResults()
    {
        var hits = OpenSubtitles.ParseSearch(J("""
            {"data":[
              {"attributes":{"language":"en","release":"R1","download_count":500,"moviehash_match":true,"from_trusted":false,"hearing_impaired":false,"machine_translated":false,"ai_translated":false,"files":[{"file_id":11}]}},
              {"attributes":{"language":"es","release":"R2","files":[{"file_id":12}]}},
              {"attributes":{"language":"en","files":[]}}
            ]}
            """));
        Assert.Equal(2, hits.Count); // the one with no files is dropped
        Assert.Equal(new SubHit(11, "en", "R1", 500, true, false, false, false), hits[0]);
        Assert.Equal(new SubHit(12, "es", "R2", 0, false, false, false, false), hits[1]);
    }

    [Fact]
    public void PickPrefersLanguageOrderThenQuality()
    {
        var hits = new List<SubHit>
        {
            new(1, "en", "popular machine", 9000, false, false, false, true),
            new(2, "en", "trusted human", 10, false, true, false, false),
            new(3, "en", "hash match", 1, true, false, false, false),
            new(4, "es", "spanish", 99999, true, true, false, false),
        };
        Assert.Equal(3, OpenSubtitles.Pick(hits, new[] { "en", "es" })!.FileId);        // hash match wins within en
        Assert.Equal(4, OpenSubtitles.Pick(hits, new[] { "es", "en" })!.FileId);        // language order beats score
        Assert.Equal(2, OpenSubtitles.Pick(hits.Take(2).ToList(), new[] { "en" })!.FileId); // human trusted beats popular machine
        Assert.Null(OpenSubtitles.Pick(hits, new[] { "fr" }));
    }
}

/// <summary>Real libmpv, no window and no sound: checks the event parsing and subtitle calls the app depends on.</summary>
[Collection("db")]
public class MpvEventTests
{
    [Fact]
    public void ClientMessageFileLoadedAndSubAdd()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"))) return;
        var ff = @"C:\ffmpeg\bin\ffmpeg.exe";
        if (!File.Exists(ff)) return;

        var dir = Path.Combine(Path.GetTempPath(), "lumina-ev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var clip = Path.Combine(dir, "clip.mkv");
        var srt = Path.Combine(dir, "a.srt");
        File.WriteAllText(srt, "1\n00:00:00,500 --> 00:00:02,000\nHello\n");
        using (var proc = System.Diagnostics.Process.Start(ff,
            $"-loglevel error -y -f lavfi -i testsrc=size=320x180:rate=25 -t 10 -c:v libx264 -preset ultrafast \"{clip}\""))
            proc!.WaitForExit();

        try
        {
            using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null");
            string[]? msg = null;
            var loaded = new ManualResetEventSlim();
            m.ClientMessage += a => msg = a;
            m.FileLoaded += () => loaded.Set();

            m.Load(clip);
            Assert.True(loaded.Wait(5000), "file-loaded event never arrived");
            Assert.False(m.HasSubtitleTrack());

            m.Command("script-message", "lumina-subs", "extra arg");
            for (int i = 0; i < 50 && msg == null; i++) Thread.Sleep(100);
            Assert.Equal(new[] { "lumina-subs", "extra arg" }, msg);

            m.SubAdd(srt);
            Assert.True(m.HasSubtitleTrack());
        }
        finally { Directory.Delete(dir, true); }
    }
}

[Collection("db")]
public class SubtitleCacheTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-subc-" + Guid.NewGuid().ToString("N"));

    public SubtitleCacheTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "cfg.json"));
        Db.Init();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task MissingKeyGivesHelpfulMessageAndNoNetwork()
    {
        var r = await OpenSubtitles.FetchAsync("x.mkv", "Title", null, null, null);
        Assert.Null(r.Path);
        Assert.Contains("OpenSubtitles key", r.Message);
        Assert.Contains("Settings", r.Message);
    }

    [Fact]
    public void CachedOnlyWhenFileStillExists()
    {
        var sub = Path.Combine(_dir, "s.srt");
        Assert.Null(OpenSubtitles.Cached("movie.mkv", "abc"));
        Db.KvSet("sub:abc:en", sub);
        Assert.Null(OpenSubtitles.Cached("movie.mkv", "abc")); // recorded but file missing
        File.WriteAllText(sub, "x");
        Assert.Equal(sub, OpenSubtitles.Cached("movie.mkv", "abc"));
    }
}

[Collection("db")]
public class OpenSubtitlesLiveTest
{
    // Real OpenSubtitles call (uses one of the daily download credits). Runs only when OPENSUBS_KEY is set.
    [Fact]
    public async Task SearchAndDownload()
    {
        var key = Environment.GetEnvironmentVariable("OPENSUBS_KEY");
        if (string.IsNullOrEmpty(key)) return;

        var dir = Path.Combine(Path.GetTempPath(), "lumina-osl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "cfg.json"), "{\"openSubtitlesKey\":\"" + key + "\"}");
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(dir, "cfg.json"));
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_SUBS", Path.Combine(dir, "subs"));
        try
        {
            Db.Init();
            var video = Path.Combine(dir, "The Matrix (1999).mkv");
            File.WriteAllBytes(video, new byte[200_000]); // hashes to nothing real, so this exercises the title fallback

            var r = await OpenSubtitles.FetchAsync(video, "The Matrix", 1999, null, null);
            Assert.NotNull(r.Path);
            Assert.StartsWith("Subtitles: en", r.Message);
            Assert.Contains("-->", File.ReadAllText(r.Path!)); // really an srt

            // second call is served from the local cache with no new download
            var hash = OpenSubtitles.Hash(video);
            Assert.Equal(r.Path, OpenSubtitles.Cached(video, hash));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUMINA_CONFIG", null);
            Environment.SetEnvironmentVariable("LUMINA_SUBS", null);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
