using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;

namespace Lumina.Subtitles;

public record SubHit(long FileId, string Language, string Release, int Downloads, bool HashMatch, bool Trusted, bool HearingImpaired, bool Machine);

public record SubResult(string? Path, string Message);

/// <summary>OpenSubtitles.com REST API v1: hash search, title fallback, download to a local cache.</summary>
public static class OpenSubtitles
{
    // LUMINA_OPENSUBTITLES_API points tests at a local fake server.
    static string Api => Environment.GetEnvironmentVariable("LUMINA_OPENSUBTITLES_API") ?? "https://api.opensubtitles.com/api/v1/";
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Lumina v0.1"); // the API rejects vague user agents
        return h;
    }

    public static string SubsDir => Environment.GetEnvironmentVariable("LUMINA_SUBS") ?? Path.Combine(
        Lumina.Windows.AppPaths.Cache, "subs");

    // ---- pure helpers (unit tested) ----

    /// <summary>OpenSubtitles hash: file size plus the 64-bit little-endian words of the first and last 64 KiB, mod 2^64. Null if under 64 KiB.</summary>
    public static string? Hash(string path)
    {
        const int Chunk = 65536;
        using var f = File.OpenRead(path);
        if (f.Length < Chunk) return null;
        ulong sum = (ulong)f.Length;
        var buf = new byte[Chunk];
        foreach (var offset in new[] { 0L, f.Length - Chunk })
        {
            f.Seek(offset, SeekOrigin.Begin);
            f.ReadExactly(buf);
            for (int i = 0; i < Chunk; i += 8) sum += BitConverter.ToUInt64(buf, i);
        }
        return sum.ToString("x16");
    }

    /// <summary>The API 301-redirects any query that is not sorted by key, lowercase, with + for spaces.</summary>
    public static string CanonicalQuery(IEnumerable<KeyValuePair<string, string>> p) =>
        string.Join("&", p.Where(kv => kv.Value.Length > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value.ToLowerInvariant()).Replace("%20", "+")));

    public static List<SubHit> ParseSearch(JsonElement root)
    {
        var l = new List<SubHit>();
        if (!root.TryGetProperty("data", out var data)) return l;
        foreach (var d in data.EnumerateArray())
        {
            var a = d.GetProperty("attributes");
            if (!a.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) continue;
            l.Add(new SubHit(
                files[0].GetProperty("file_id").GetInt64(), Str(a, "language") ?? "",
                Str(a, "release") ?? "", a.TryGetProperty("download_count", out var dc) && dc.ValueKind == JsonValueKind.Number ? dc.GetInt32() : 0,
                Bool(a, "moviehash_match"), Bool(a, "from_trusted"), Bool(a, "hearing_impaired"),
                Bool(a, "machine_translated") || Bool(a, "ai_translated")));
        }
        return l;
    }

    static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>First preferred language that has any hit; within it, hash match beats everything, then human over machine, trusted, not hearing-impaired, downloads.</summary>
    public static SubHit? Pick(IReadOnlyList<SubHit> hits, IReadOnlyList<string> langs)
    {
        foreach (var lang in langs)
        {
            var best = hits.Where(h => h.Language.Equals(lang, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(Score).FirstOrDefault();
            if (best != null) return best;
        }
        return null;
    }

    static long Score(SubHit h) =>
        (h.HashMatch ? 1_000_000 : 0) + (h.Machine ? 0 : 100_000) + (h.Trusted ? 50_000 : 0)
        + (h.HearingImpaired ? 0 : 10_000) + Math.Min(h.Downloads, 9_999);

    // ---- network ----

    static async Task<JsonDocument> Call(HttpMethod method, string pathAndQuery, string? json = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(method, Api + pathAndQuery);
            req.Headers.Add("Api-Key", Config.OpenSubtitlesKey);
            req.Headers.Accept.ParseAdd("application/json");
            if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1.5)); // 5 requests/second limit
                continue;
            }
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new SubsException(Friendly((int)res.StatusCode, body));
            return JsonDocument.Parse(body);
        }
    }

    static string Friendly(int status, string body)
    {
        string? msg = null;
        try { msg = Str(JsonDocument.Parse(body).RootElement, "message"); } catch (JsonException) { }
        return status switch
        {
            401 or 403 => "OpenSubtitles rejected the API key",
            406 => "OpenSubtitles daily download limit reached" + (msg != null ? ": " + msg.Trim() : ""),
            _ => $"OpenSubtitles error {status}" + (msg != null ? ": " + msg.Trim() : ""),
        };
    }

    public class SubsException(string message) : Exception(message);

    public static string Describe(SubHit h) =>
        $"{h.Language.ToUpperInvariant()}  {h.Release}" + (h.HashMatch ? "  (exact match)" : "") + (h.HearingImpaired ? "  (hearing impaired)" : "") + (h.Machine ? "  (machine translated)" : "") + (h.Downloads > 0 ? $"  {h.Downloads:N0} downloads" : "");

    static async Task<List<SubHit>> Search(string langs, string? hash, string? title, int? year, int? season, int? episode)
    {
        var p = new Dictionary<string, string> { ["languages"] = langs };
        if (hash != null) p["moviehash"] = hash;
        if (title != null) p["query"] = title;
        if (year != null) p["year"] = year.ToString()!;
        if (season != null) p["season_number"] = season.ToString()!;
        if (episode != null) p["episode_number"] = episode.ToString()!;
        using var doc = await Call(HttpMethod.Get, "subtitles?" + CanonicalQuery(p));
        return ParseSearch(doc.RootElement);
    }

    static async Task<string> Download(SubHit hit)
    {
        var dest = Path.Combine(SubsDir, hit.FileId + ".srt");
        if (File.Exists(dest)) return dest; // already fetched: no quota spent
        using var doc = await Call(HttpMethod.Post, "download", "{\"file_id\":" + hit.FileId + "}");
        var link = Str(doc.RootElement, "link") ?? throw new SubsException("OpenSubtitles returned no download link");
        var bytes = await Http.GetByteArrayAsync(link);
        Directory.CreateDirectory(SubsDir);
        await File.WriteAllBytesAsync(dest + ".part", bytes);
        File.Move(dest + ".part", dest, true);
        return dest;
    }

    static readonly string[] SidecarExtensions = { ".srt", ".ass", ".ssa", ".sub", ".vtt" };

    /// <summary>True when a subtitle file sits next to the video: "Movie.srt", "Movie.en.srt", "Movie.English.forced.srt".</summary>
    public static bool HasSidecar(string videoPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(videoPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            return Directory.EnumerateFiles(dir).Any(f =>
                SidecarExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)
                && (Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(f).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Every candidate for a file, best first: exact hash matches, then title matches, in the user's languages.</summary>
    public static async Task<List<SubHit>> SearchAsync(string videoPath, string title, int? year, int? season, int? episode)
    {
        if (string.IsNullOrEmpty(Config.OpenSubtitlesKey)) throw new SubsException("Add an OpenSubtitles key in Settings to search for subtitles");
        var langs = Config.SubtitleLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var langParam = string.Join(",", langs.Order(StringComparer.Ordinal));
        var hash = await Task.Run(() => Hash(videoPath));
        var all = new List<SubHit>();
        if (hash != null) all.AddRange((await Search(langParam, hash, null, null, null, null)).Where(h => h.HashMatch));
        all.AddRange((await Search(langParam, null, title, year, season, episode)).Where(h => all.All(a => a.FileId != h.FileId)));
        var rank = langs.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => x.i, StringComparer.OrdinalIgnoreCase);
        return all.OrderBy(h => rank.TryGetValue(h.Language, out var i) ? i : 99).ThenByDescending(Score).ToList();
    }

    /// <summary>Downloads a chosen candidate and remembers it as this file's subtitle, so it loads by itself next time.</summary>
    public static async Task<string> ChooseAsync(string videoPath, SubHit hit)
    {
        var path = await Download(hit);
        Db.KvSet(CacheKey(videoPath, await Task.Run(() => Hash(videoPath))), path);
        return path;
    }

    /// <summary>Cached subtitle path for this file if one was downloaded before.</summary>
    public static string? Cached(string videoPath, string? hash)
    {
        var p = Db.KvGet(CacheKey(videoPath, hash));
        return p != null && File.Exists(p) ? p : null;
    }

    static string CacheKey(string videoPath, string? hash) => "sub:" + (hash ?? videoPath) + ":" + Config.SubtitleLanguages;

    /// <summary>
    /// Find and download the best subtitle for a file. Hash match first; title, year, season and episode as the fallback.
    /// ponytail: query fallback guesses from the title, so it can pick a wrong release; the on-screen text names what was chosen.
    /// </summary>
    public static async Task<SubResult> FetchAsync(string videoPath, string title, int? year, int? season, int? episode)
    {
        if (string.IsNullOrEmpty(Config.OpenSubtitlesKey))
            return new SubResult(null, "Add an OpenSubtitles key in Settings to search for subtitles");
        try
        {
            var langs = Config.SubtitleLanguages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var langParam = string.Join(",", langs.Order(StringComparer.Ordinal));
            var hash = await Task.Run(() => Hash(videoPath));

            SubHit? pick = null;
            if (hash != null)
            {
                var byHash = (await Search(langParam, hash, null, null, null, null)).Where(h => h.HashMatch).ToList();
                pick = Pick(byHash, langs);
            }
            pick ??= Pick(await Search(langParam, null, title, year, season, episode), langs);
            if (pick == null) return new SubResult(null, "No subtitles found on OpenSubtitles");

            var path = await Download(pick);
            Db.KvSet(CacheKey(videoPath, hash), path);
            return new SubResult(path, $"Subtitles: {pick.Language} - {pick.Release}" + (pick.HashMatch ? " (exact match)" : ""));
        }
        catch (SubsException e) { return new SubResult(null, e.Message); }
        catch (HttpRequestException) { return new SubResult(null, "OpenSubtitles unreachable"); }
        catch (TaskCanceledException) { return new SubResult(null, "OpenSubtitles timed out"); }
    }
}
