using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;

namespace Lumina.Sync;

public class TraktException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public record DeviceCode(string Code, string UserCode, string Url, int ExpiresIn, int Interval);

/// <summary>Trakt.tv: device sign-in (no password or redirect), scrobbling while playing, two-way history sync.</summary>
public static class Trakt
{
    // LUMINA_TRAKT_API points tests at a local fake server.
    static string Api => Environment.GetEnvironmentVariable("LUMINA_TRAKT_API") ?? "https://api.trakt.tv/";
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Lumina/0.1");
        h.DefaultRequestHeaders.Add("trakt-api-version", "2");
        return h;
    }

    // ---- state ----

    /// <summary>A client id is enough; the secret is only sent when one is set (apps registered with PKCE have none).</summary>
    public static bool HasCredentials => !string.IsNullOrEmpty(Config.TraktClientId);

    static Dictionary<string, object?> WithClient(Dictionary<string, object?> body)
    {
        body["client_id"] = Config.TraktClientId;
        if (!string.IsNullOrEmpty(Config.TraktClientSecret)) body["client_secret"] = Config.TraktClientSecret;
        return body;
    }

    public static bool IsConnected => HasCredentials && Db.KvGet("trakt.access") != null;

    public static void Disconnect()
    {
        foreach (var k in new[] { "trakt.access", "trakt.refresh", "trakt.expires" }) Db.KvDelete(k);
    }

    static void SaveTokens(JsonElement t)
    {
        var created = t.TryGetProperty("created_at", out var c) ? c.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Db.KvSet("trakt.access", t.GetProperty("access_token").GetString()!);
        Db.KvSet("trakt.refresh", t.GetProperty("refresh_token").GetString()!);
        Db.KvSet("trakt.expires", (created + t.GetProperty("expires_in").GetInt64()).ToString());
    }

    // ---- low level ----

    static HttpRequestMessage Req(HttpMethod m, string path, object? body, string? bearer)
    {
        var req = new HttpRequestMessage(m, Api + path);
        req.Headers.Add("trakt-api-key", Config.TraktClientId);
        if (bearer != null) req.Headers.Authorization = new("Bearer", bearer);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    static async Task<JsonDocument> Send(HttpMethod m, string path, object? body = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await ValidToken();
            using var req = Req(m, path, body, token);
            using var res = await Http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { await Refresh(); continue; }
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2));
                continue;
            }
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new TraktException((int)res.StatusCode, $"Trakt error {(int)res.StatusCode}");
            return JsonDocument.Parse(text.Length == 0 ? "null" : text);
        }
    }

    static async Task<string> ValidToken()
    {
        var access = Db.KvGet("trakt.access") ?? throw new TraktException(401, "Trakt is not connected");
        if (long.TryParse(Db.KvGet("trakt.expires"), out var exp) && exp - DateTimeOffset.UtcNow.ToUnixTimeSeconds() < 60)
            await Refresh();
        return Db.KvGet("trakt.access") ?? access;
    }

    static async Task Refresh()
    {
        var refresh = Db.KvGet("trakt.refresh") ?? throw new TraktException(401, "Trakt is not connected");
        using var req = Req(HttpMethod.Post, "oauth/token", WithClient(new()
        {
            ["refresh_token"] = refresh, ["redirect_uri"] = "urn:ietf:wg:oauth:2.0:oob", ["grant_type"] = "refresh_token",
        }), null);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            // A rejected refresh token means the grant is gone: drop it so the UI offers Connect again.
            if (res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) Disconnect();
            throw new TraktException((int)res.StatusCode, "Trakt session expired, connect again");
        }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        SaveTokens(doc.RootElement);
    }

    // ---- device sign-in ----

    public static async Task<DeviceCode> StartDeviceAuth()
    {
        if (!HasCredentials) throw new TraktException(0, "Add your Trakt client id and secret in Settings first");
        using var req = Req(HttpMethod.Post, "oauth/device/code", new { client_id = Config.TraktClientId }, null);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new TraktException((int)res.StatusCode, $"Trakt rejected the client id ({(int)res.StatusCode})");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        return new DeviceCode(r.GetProperty("device_code").GetString()!, r.GetProperty("user_code").GetString()!,
            r.GetProperty("verification_url").GetString()!, r.GetProperty("expires_in").GetInt32(), r.GetProperty("interval").GetInt32());
    }

    /// <summary>Poll until the user approves the code on trakt.tv. False if denied, expired or cancelled.</summary>
    public static async Task<bool> WaitForDeviceAuth(DeviceCode d, CancellationToken ct)
    {
        var interval = d.Interval;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(d.ExpiresIn);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(interval), ct); }
            catch (TaskCanceledException) { return false; }

            using var req = Req(HttpMethod.Post, "oauth/device/token",
                WithClient(new() { ["code"] = d.Code }), null);
            using var res = await Http.SendAsync(req, ct);
            switch ((int)res.StatusCode)
            {
                case 200:
                    using (var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct))) SaveTokens(doc.RootElement);
                    return true;
                case 400: break;                 // still waiting for the user
                case 429: interval++; break;     // slow down
                default: return false;           // 404 bad code, 409 used, 410 expired, 418 denied
            }
        }
        return false;
    }

    // ---- scrobble ----

    /// <summary>Tell Trakt what is playing: start, or stop (below 80% it saves a resume point, above it marks watched). Best effort: failures are swallowed, a later sync reconciles.</summary>
    public static async Task ScrobbleAsync(string action, MediaItem item, double progressPct)
    {
        if (!IsConnected || item.TmdbId <= 0) return;
        object body = item.Kind == "movie"
            ? new { movie = new { ids = new { tmdb = item.TmdbId } }, progress = Math.Round(progressPct, 2) }
            : new
            {
                show = new { ids = new { tmdb = item.TmdbId } },
                episode = new { season = item.Season, number = item.Episode },
                progress = Math.Round(progressPct, 2),
            };
        try { (await Send(HttpMethod.Post, "scrobble/" + action, body)).Dispose(); }
        catch (Exception e) when (e is TraktException or HttpRequestException or TaskCanceledException) { }
    }

    // ---- sync ----

    public static string Key(string kind, long tmdb, int? season, int? episode) =>
        kind == "movie" ? $"m:{tmdb}" : $"e:{tmdb}:{season}:{episode}";

    static long Ts(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToUnixTimeSeconds() : 0;

    static long? TmdbId(JsonElement obj) =>
        obj.TryGetProperty("ids", out var ids) && ids.TryGetProperty("tmdb", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : null;

    /// <summary>Merge /sync/watched/movies, /sync/watched/shows and /sync/playback into one lookup keyed by <see cref="Key"/>.</summary>
    public static Dictionary<string, RemoteState> ParseRemote(JsonElement movies, JsonElement shows, JsonElement playback)
    {
        var d = new Dictionary<string, RemoteState>();
        if (movies.ValueKind == JsonValueKind.Array)
            foreach (var m in movies.EnumerateArray())
                if (TmdbId(m.GetProperty("movie")) is { } id)
                    d[Key("movie", id, null, null)] = new RemoteState(true, Ts(m, "last_watched_at"), null, null);

        if (shows.ValueKind == JsonValueKind.Array)
            foreach (var s in shows.EnumerateArray())
            {
                if (TmdbId(s.GetProperty("show")) is not { } id || !s.TryGetProperty("seasons", out var seasons)) continue;
                foreach (var se in seasons.EnumerateArray())
                    foreach (var ep in se.GetProperty("episodes").EnumerateArray())
                        d[Key("show", id, se.GetProperty("number").GetInt32(), ep.GetProperty("number").GetInt32())] =
                            new RemoteState(true, Ts(ep, "last_watched_at"), null, null);
            }

        if (playback.ValueKind == JsonValueKind.Array)
            foreach (var p in playback.EnumerateArray())
            {
                var pct = p.GetProperty("progress").GetDouble();
                var paused = Ts(p, "paused_at");
                string? key = null;
                if (p.GetProperty("type").GetString() == "movie" && TmdbId(p.GetProperty("movie")) is { } mid)
                    key = Key("movie", mid, null, null);
                else if (p.GetProperty("type").GetString() == "episode" && TmdbId(p.GetProperty("show")) is { } sid)
                {
                    var e = p.GetProperty("episode");
                    key = Key("show", sid, e.GetProperty("season").GetInt32(), e.GetProperty("number").GetInt32());
                }
                if (key == null) continue;
                d[key] = (d.TryGetValue(key, out var have) ? have : new RemoteState(false, null, null, null)) with { ProgressPct = pct, PausedAt = paused };
            }
        return d;
    }

    static string Iso(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.000'Z'");

    /// <summary>Request body for /sync/history from locally watched items Trakt does not have.</summary>
    /// <summary>Remove plays from the user's Trakt history, after they marked the items unwatched here.</summary>
    public static async Task<string> RemoveHistoryAsync(IEnumerable<LocalRow> rows)
    {
        var list = rows.Where(r => r.TmdbId > 0).ToList();
        if (list.Count == 0 || !IsConnected) return "";
        try { (await Send(HttpMethod.Post, "sync/history/remove", HistoryBody(list))).Dispose(); return ""; }
        catch (TraktException e) { return e.Message; }
        catch (HttpRequestException) { return "Trakt unreachable, the watched marks may come back on the next sync"; }
        catch (TaskCanceledException) { return "Trakt timed out"; }
    }

    public static object HistoryBody(IEnumerable<LocalRow> rows)
    {
        var list = rows.ToList();
        return new
        {
            movies = list.Where(r => r.Kind == "movie")
                .Select(r => new { watched_at = Iso(r.PlayedAt > 0 ? r.PlayedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds()), ids = new { tmdb = r.TmdbId } }).ToList(),
            shows = list.Where(r => r.Kind == "show").GroupBy(r => r.TmdbId).Select(g => new
            {
                ids = new { tmdb = g.Key },
                seasons = g.GroupBy(r => r.Season).Select(sg => new
                {
                    number = sg.Key,
                    episodes = sg.Select(r => new { number = r.Episode, watched_at = Iso(r.PlayedAt > 0 ? r.PlayedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds()) }).ToList(),
                }).ToList(),
            }).ToList(),
        };
    }

    /// <summary>Pull Trakt state into the local db and push locally watched items Trakt lacks. Returns a one-line summary.</summary>
    public static async Task<string> SyncAsync()
    {
        try
        {
            using var movies = await Send(HttpMethod.Get, "sync/watched/movies");
            using var shows = await Send(HttpMethod.Get, "sync/watched/shows");
            using var playback = await Send(HttpMethod.Get, "sync/playback");
            var remote = ParseRemote(movies.RootElement, shows.RootElement, playback.RootElement);

            int pulled = 0;
            var push = new List<LocalRow>();
            foreach (var row in Db.LocalWatchRows())
            {
                remote.TryGetValue(Key(row.Kind, row.TmdbId, row.Season, row.Episode), out var r);
                var m = TraktMerge.Merge(new LocalState(row.Watched, row.Position, row.PlayedAt, row.Duration, row.UnwatchedAt), r);
                if (m.Changed) { Db.SetWatch(row.Id, m.Position, m.Watched, m.PlayedAt); pulled++; }
                if (m.Push) push.Add(row);
            }
            if (push.Count > 0) (await Send(HttpMethod.Post, "sync/history", HistoryBody(push))).Dispose();
            return $"Trakt synced: {pulled} updated here, {push.Count} sent";
        }
        catch (TraktException e) { return e.Message; }
        catch (HttpRequestException) { return "Trakt unreachable"; }
        catch (TaskCanceledException) { return "Trakt timed out"; }
    }
}
