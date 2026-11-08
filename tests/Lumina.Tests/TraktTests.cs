using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Lumina.Library;
using Lumina.Sync;
using Xunit;

namespace Lumina.Tests;

public class TraktMergeTests
{
    static LocalState L(bool w = false, double pos = 0, long at = 0, double? dur = 1000) => new(w, pos, at, dur);

    [Fact]
    public void WatchedIsStickyAndNeverClearedByPull()
    {
        var m = TraktMerge.Merge(L(w: true, pos: 50, at: 500), new RemoteState(false, null, null, null));
        Assert.True(m.Watched);
        Assert.False(m.Changed);
        Assert.True(m.Push); // Trakt lacks it, so send it
    }

    [Fact]
    public void RemoteWatchedMarksLocalWatchedAndKeepsNewestTime()
    {
        var m = TraktMerge.Merge(L(at: 100), new RemoteState(true, 900, null, null));
        Assert.True(m.Watched);
        Assert.Equal(900, m.PlayedAt);
        Assert.True(m.Changed);
        Assert.False(m.Push);

        var both = TraktMerge.Merge(L(w: true, at: 2000), new RemoteState(true, 900, null, null));
        Assert.Equal(2000, both.PlayedAt); // local play is newer: keep it
        Assert.False(both.Changed);
        Assert.False(both.Push);
    }

    [Fact]
    public void NewerRemoteProgressBecomesPositionOlderIsIgnored()
    {
        var newer = TraktMerge.Merge(L(pos: 10, at: 100, dur: 2000), new RemoteState(false, null, 25, 500));
        Assert.Equal(500, newer.Position, 3); // 25% of 2000 s
        Assert.Equal(500, newer.PlayedAt);

        var older = TraktMerge.Merge(L(pos: 10, at: 900, dur: 2000), new RemoteState(false, null, 25, 500));
        Assert.Equal(10, older.Position);
        Assert.False(older.Changed);

        var noDuration = TraktMerge.Merge(L(pos: 10, at: 100, dur: null), new RemoteState(false, null, 25, 500));
        Assert.Equal(10, noDuration.Position); // cannot convert percent without a duration
    }

    [Fact]
    public void WatchedItemIgnoresRemoteProgress()
    {
        var m = TraktMerge.Merge(L(w: true, pos: 0, at: 100, dur: 2000), new RemoteState(false, null, 40, 5000));
        Assert.Equal(0, m.Position);
    }

    [Fact]
    public void NothingRemoteNothingLocalIsNoOp()
    {
        var m = TraktMerge.Merge(L(), null);
        Assert.False(m.Changed);
        Assert.False(m.Push);
    }
}

public class ScrobbleOnPauseTests
{
    [Theory]
    [InlineData(0.5, "stop")]    // saves a resume point
    [InlineData(40, "stop")]
    [InlineData(79.9, "stop")]
    [InlineData(80, null)]       // a stop here would mark it watched: leave that to the real end
    [InlineData(97, null)]
    public void PauseBecomesAStopOnlyBelowTheWatchedLine(double pct, string? expected) =>
        Assert.Equal(expected, Lumina.Player.PlaybackSession.ActionOnPause(pct));

    [Fact]
    public void LocalWatchedLineMatchesTraktsEightyPercent() =>
        Assert.Equal(0.8, Lumina.Player.PlaybackSession.WatchedFraction);
}

public class TraktParseTests
{
    static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void ParsesWatchedAndPlayback()
    {
        var d = Trakt.ParseRemote(
            J("""[{"last_watched_at":"2026-01-02T03:04:05.000Z","movie":{"ids":{"tmdb":603}}},{"movie":{"ids":{"tmdb":null}}}]"""),
            J("""[{"show":{"ids":{"tmdb":1396}},"seasons":[{"number":1,"episodes":[{"number":1,"last_watched_at":"2026-02-01T00:00:00.000Z"},{"number":2,"last_watched_at":"2026-02-02T00:00:00.000Z"}]}]}]"""),
            J("""[{"progress":42.5,"paused_at":"2026-03-01T00:00:00.000Z","type":"episode","episode":{"season":1,"number":3},"show":{"ids":{"tmdb":1396}}},{"progress":10,"paused_at":"2026-03-02T00:00:00.000Z","type":"movie","movie":{"ids":{"tmdb":603}}}]"""));

        Assert.True(d[Trakt.Key("movie", 603, null, null)].Watched);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).ToUnixTimeSeconds(), d[Trakt.Key("movie", 603, null, null)].WatchedAt);
        Assert.Equal(10, d[Trakt.Key("movie", 603, null, null)].ProgressPct); // progress merged onto the watched entry
        Assert.True(d[Trakt.Key("show", 1396, 1, 2)].Watched);
        var paused = d[Trakt.Key("show", 1396, 1, 3)];
        Assert.False(paused.Watched);
        Assert.Equal(42.5, paused.ProgressPct);
        Assert.Equal(4, d.Count); // the movie with no tmdb id is skipped
    }

    [Fact]
    public void HistoryBodyGroupsEpisodesUnderShowAndSeason()
    {
        var rows = new[]
        {
            new LocalRow(1, "movie", 27205, null, null, 0, true, 1_700_000_000, 100),
            new LocalRow(2, "show", 1396, 1, 1, 0, true, 1_700_000_100, 100),
            new LocalRow(3, "show", 1396, 1, 2, 0, true, 0, 100),
            new LocalRow(4, "show", 1396, 2, 1, 0, true, 1_700_000_300, 100),
        };
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Trakt.HistoryBody(rows)));
        var root = doc.RootElement;
        Assert.Equal(27205, root.GetProperty("movies")[0].GetProperty("ids").GetProperty("tmdb").GetInt64());
        Assert.Equal("2023-11-14T22:13:20.000Z", root.GetProperty("movies")[0].GetProperty("watched_at").GetString());
        var show = root.GetProperty("shows")[0];
        Assert.Equal(1, root.GetProperty("shows").GetArrayLength());
        Assert.Equal(2, show.GetProperty("seasons").GetArrayLength());
        Assert.Equal(2, show.GetProperty("seasons")[0].GetProperty("episodes").GetArrayLength());
    }
}

/// <summary>Tiny local stand-in for api.trakt.tv that records every request.</summary>
sealed class FakeTrakt : IDisposable
{
    public record Seen(string Method, string Path, string Body, string? Auth);

    HttpListener _l;
    public List<Seen> Requests { get; } = new();
    public Func<Seen, (int Status, string Json)> Handler { get; set; } = _ => (404, "{}");
    public string Url { get; }

    public FakeTrakt()
    {
        var (listener, url) = TestNet.StartListener("localhost");
        _l = listener;
        Url = url;
        _ = Task.Run(async () =>
        {
            while (_l.IsListening)
            {
                HttpListenerContext c;
                try { c = await _l.GetContextAsync(); } catch { return; }
                var body = await new StreamReader(c.Request.InputStream).ReadToEndAsync();
                var seen = new Seen(c.Request.HttpMethod, c.Request.Url!.AbsolutePath, body, c.Request.Headers["Authorization"]);
                lock (Requests) Requests.Add(seen);
                var (status, json) = Handler(seen);
                c.Response.StatusCode = status;
                var bytes = Encoding.UTF8.GetBytes(json);
                c.Response.ContentType = "application/json";
                await c.Response.OutputStream.WriteAsync(bytes);
                c.Response.Close();
            }
        });
    }

    public void Dispose() => _l.Close();
}

[Collection("db")]
public class TraktFlowTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-trakt-" + Guid.NewGuid().ToString("N"));
    readonly FakeTrakt _srv = new();

    public TraktFlowTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), "{\"traktClientId\":\"cid\",\"traktClientSecret\":\"sec\"}");
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "cfg.json"));
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_TRAKT_API", _srv.Url);
        Db.Init();
    }

    public void Dispose()
    {
        _srv.Dispose();
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", null);
        Environment.SetEnvironmentVariable("LUMINA_TRAKT_API", null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    static string Tokens(string access, string refresh, long expiresIn = 7200) =>
        $"{{\"access_token\":\"{access}\",\"refresh_token\":\"{refresh}\",\"expires_in\":{expiresIn},\"created_at\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";

    void Connect(string access = "A1", string refresh = "R1", long expiresIn = 7200)
    {
        Db.KvSet("trakt.access", access);
        Db.KvSet("trakt.refresh", refresh);
        Db.KvSet("trakt.expires", (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn).ToString());
    }

    [Fact]
    public async Task DeviceFlowWaitsThenStoresTokens()
    {
        int polls = 0;
        _srv.Handler = r => r.Path switch
        {
            "/oauth/device/code" => (200, "{\"device_code\":\"dc\",\"user_code\":\"ABCD1234\",\"verification_url\":\"https://trakt.tv/activate\",\"expires_in\":600,\"interval\":1}"),
            "/oauth/device/token" => ++polls == 1 ? (400, "") : (200, Tokens("A1", "R1")),
            _ => (404, "{}"),
        };

        Assert.False(Trakt.IsConnected);
        var d = await Trakt.StartDeviceAuth();
        Assert.Equal("ABCD1234", d.UserCode);
        Assert.True(await Trakt.WaitForDeviceAuth(d, CancellationToken.None));
        Assert.Equal(2, polls); // first answer was still-pending
        Assert.True(Trakt.IsConnected);
        Assert.Equal("A1", Db.KvGet("trakt.access"));

        var poll = _srv.Requests.First(r => r.Path == "/oauth/device/token");
        Assert.Contains("\"code\":\"dc\"", poll.Body);
        Assert.Contains("\"client_secret\":\"sec\"", poll.Body);
    }

    [Fact]
    public async Task ClientIdAloneIsEnoughAndNoSecretIsSent()
    {
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), "{\"traktClientId\":\"cid\"}");
        _srv.Handler = r => r.Path switch
        {
            "/oauth/device/code" => (200, "{\"device_code\":\"dc\",\"user_code\":\"ABCD1234\",\"verification_url\":\"https://trakt.tv/activate\",\"expires_in\":600,\"interval\":1}"),
            "/oauth/device/token" => (200, Tokens("A1", "R1")),
            _ => (404, "{}"),
        };
        Assert.True(Trakt.HasCredentials);
        var d = await Trakt.StartDeviceAuth();
        Assert.True(await Trakt.WaitForDeviceAuth(d, CancellationToken.None));
        var poll = _srv.Requests.First(r => r.Path == "/oauth/device/token");
        Assert.Contains("\"client_id\":\"cid\"", poll.Body);
        Assert.DoesNotContain("client_secret", poll.Body);
    }

    [Fact]
    public async Task DeniedCodeEndsWaiting()
    {
        _srv.Handler = r => r.Path == "/oauth/device/token" ? (418, "") : (404, "{}");
        var d = new DeviceCode("dc", "X", "https://trakt.tv/activate", 600, 1);
        Assert.False(await Trakt.WaitForDeviceAuth(d, CancellationToken.None));
        Assert.False(Trakt.IsConnected);
    }

    [Fact]
    public async Task SyncPullsRemoteWatchedAndPushesLocalOnly()
    {
        Connect();
        var lib = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "The Matrix (1999).mkv"), "x");
        File.WriteAllText(Path.Combine(lib, "Inception (2010).mkv"), "x");
        Db.AddRoot(lib);
        Scanner.ScanAll();
        Db.SetMatch("movie", "The Matrix", 1999, new Meta("movie", 603, "The Matrix", null, null, null, 0, 1999));
        Db.SetMatch("movie", "Inception", 2010, new Meta("movie", 27205, "Inception", null, null, null, 0, 2010));
        Db.SaveWatch(Db.List().Single(i => i.Title == "Inception").Id, 900, 1000, true); // watched here only

        _srv.Handler = r => (r.Method, r.Path) switch
        {
            ("GET", "/sync/watched/movies") => (200, "[{\"last_watched_at\":\"2026-01-02T00:00:00.000Z\",\"movie\":{\"ids\":{\"tmdb\":603}}}]"),
            ("GET", "/sync/watched/shows") => (200, "[]"),
            ("GET", "/sync/playback") => (200, "[]"),
            ("POST", "/sync/history") => (201, "{\"added\":{\"movies\":1}}"),
            _ => (404, "{}"),
        };

        var msg = await Trakt.SyncAsync();
        Assert.Equal("Trakt synced: 1 updated here, 1 sent", msg);

        var items = Db.List();
        Assert.True(items.Single(i => i.Title == "The Matrix").Watched); // pulled
        Assert.True(items.Single(i => i.Title == "Inception").Watched);

        var push = _srv.Requests.Single(r => r.Path == "/sync/history");
        Assert.Contains("27205", push.Body);
        Assert.DoesNotContain("603", push.Body); // Trakt already had it
        Assert.Equal("Bearer A1", push.Auth);

        // second sync is quiet: nothing changed here, but Inception is still not on the fake server, so it is resent
        Assert.Equal("Trakt synced: 0 updated here, 1 sent", await Trakt.SyncAsync());
    }

    [Fact]
    public async Task MarkingUnwatchedRemovesTheHistoryFromTraktAndASyncDoesNotBringItBack()
    {
        Connect();
        var lib = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "The Matrix (1999).mkv"), "x");
        Db.AddRoot(lib);
        Scanner.ScanAll();
        Db.SetMatch("movie", "The Matrix", 1999, new Meta("movie", 603, "The Matrix", null, null, null, 0, 1999));
        var id = Db.List().Single().Id;
        Db.MarkWatched(new[] { id }, true);

        // Trakt still lists it as watched (an old play), as it would if the removal could not be sent.
        _srv.Handler = r => (r.Method, r.Path) switch
        {
            ("GET", "/sync/watched/movies") => (200, "[{\"last_watched_at\":\"2020-01-01T00:00:00.000Z\",\"movie\":{\"ids\":{\"tmdb\":603}}}]"),
            ("GET", "/sync/watched/shows") => (200, "[]"),
            ("GET", "/sync/playback") => (200, "[]"),
            ("POST", "/sync/history/remove") => (200, "{\"deleted\":{\"movies\":1}}"),
            _ => (404, "{}"),
        };

        Db.MarkWatched(new[] { id }, false);
        var rows = Db.LocalWatchRows();
        Assert.Equal("", await Trakt.RemoveHistoryAsync(rows));
        var removal = _srv.Requests.Single(r => r.Path == "/sync/history/remove");
        Assert.Contains("603", removal.Body);

        await Trakt.SyncAsync();                                  // the fake still reports the 2020 play
        Assert.False(Db.List().Single().Watched);                 // but the later "unwatched" wins
    }

    [Fact]
    public async Task RemovalWhenTraktIsUnreachableReportsItAndKeepsTheLocalChoice()
    {
        Connect();
        _srv.Dispose();                                            // nobody home
        var row = new LocalRow(1, "movie", 603, null, null, 0, false, 0, null, 5);
        var msg = await Trakt.RemoveHistoryAsync(new[] { row });
        Assert.Contains("Trakt", msg);
        Assert.NotEqual("", msg);
    }

    [Fact]
    public async Task RemovalWithNothingMatchedOrNotConnectedSendsNothing()
    {
        var row = new LocalRow(1, "movie", 0, null, null, 0, false, 0, null, 5);   // no TMDB id: Trakt could not identify it
        Connect();
        Assert.Equal("", await Trakt.RemoveHistoryAsync(new[] { row }));
        Assert.Empty(_srv.Requests);
    }

    [Fact]
    public async Task ExpiredAccessTokenIsRefreshedOnA401AndRetried()
    {
        Connect("OLD", "R1");
        _srv.Handler = r => (r.Method, r.Path) switch
        {
            ("POST", "/oauth/token") => (200, Tokens("NEW", "R2")),
            ("GET", _) when r.Auth == "Bearer OLD" => (401, "{}"),
            ("GET", "/sync/watched/movies") or ("GET", "/sync/watched/shows") or ("GET", "/sync/playback") => (200, "[]"),
            _ => (404, "{}"),
        };

        Assert.Equal("Trakt synced: 0 updated here, 0 sent", await Trakt.SyncAsync());
        Assert.Equal("NEW", Db.KvGet("trakt.access"));
        Assert.Equal("R2", Db.KvGet("trakt.refresh"));
        var refresh = _srv.Requests.Single(r => r.Path == "/oauth/token");
        Assert.Contains("\"grant_type\":\"refresh_token\"", refresh.Body);
        Assert.Contains("\"refresh_token\":\"R1\"", refresh.Body);
    }

    [Fact]
    public async Task NearExpiryTokenIsRefreshedBeforeTheCall()
    {
        Connect("OLD", "R1", expiresIn: 10);
        _srv.Handler = r => r.Path == "/oauth/token" ? (200, Tokens("NEW", "R2")) : (200, "[]");
        await Trakt.SyncAsync();
        Assert.Equal("Bearer NEW", _srv.Requests.First(r => r.Path == "/sync/watched/movies").Auth);
    }

    [Fact]
    public async Task RejectedRefreshDisconnects()
    {
        Connect("OLD", "R1");
        _srv.Handler = r => r.Path == "/oauth/token" ? (400, "{}") : (401, "{}");
        var msg = await Trakt.SyncAsync();
        Assert.Contains("connect again", msg);
        Assert.False(Trakt.IsConnected);
    }

    [Fact]
    public async Task ScrobbleSendsMovieAndEpisodeBodies()
    {
        Connect();
        _srv.Handler = _ => (201, "{}");
        await Trakt.ScrobbleAsync("start", new MediaItem { Kind = "movie", TmdbId = 603 }, 12.3456);
        await Trakt.ScrobbleAsync("stop", new MediaItem { Kind = "show", TmdbId = 1396, Season = 2, Episode = 5 }, 91);
        await Trakt.ScrobbleAsync("start", new MediaItem { Kind = "movie", TmdbId = 0 }, 1); // no tmdb id: nothing sent

        Assert.Equal(2, _srv.Requests.Count);
        Assert.Equal("/scrobble/start", _srv.Requests[0].Path);
        using var a = JsonDocument.Parse(_srv.Requests[0].Body);
        Assert.Equal(603, a.RootElement.GetProperty("movie").GetProperty("ids").GetProperty("tmdb").GetInt64());
        Assert.Equal(12.35, a.RootElement.GetProperty("progress").GetDouble());
        using var b = JsonDocument.Parse(_srv.Requests[1].Body);
        Assert.Equal("/scrobble/stop", _srv.Requests[1].Path);
        Assert.Equal(2, b.RootElement.GetProperty("episode").GetProperty("season").GetInt32());
        Assert.Equal(5, b.RootElement.GetProperty("episode").GetProperty("number").GetInt32());
        Assert.Equal(1396, b.RootElement.GetProperty("show").GetProperty("ids").GetProperty("tmdb").GetInt64());
    }

    [Fact]
    public async Task ScrobbleFailureIsSwallowed()
    {
        Connect();
        _srv.Handler = _ => (409, "{}"); // Trakt answers 409 for a duplicate scrobble
        await Trakt.ScrobbleAsync("stop", new MediaItem { Kind = "movie", TmdbId = 603 }, 95); // must not throw
    }
}
