using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumina.Library;

namespace Lumina.Metadata;

/// <summary>TMDB lookups: match titles, fetch episode names, cache posters under %LOCALAPPDATA%\Lumina\art.</summary>
public static class Tmdb
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    // LUMINA_TMDB_API and LUMINA_TMDB_IMG point tests at a local fake server.
    // Some networks block api.themoviedb.org (the connection is reset) but let TMDB's own alias api.tmdb.org through: try the other one when a host cannot be reached.
    static readonly string[] Hosts = { "https://api.themoviedb.org/3/", "https://api.tmdb.org/3/" };
    static int _host;
    static string Api => Environment.GetEnvironmentVariable("LUMINA_TMDB_API") ?? Hosts[_host];
    static string ImgBase => Environment.GetEnvironmentVariable("LUMINA_TMDB_IMG") ?? "https://image.tmdb.org/t/p/";
    const long ArtLimitBytes = 600L * 1024 * 1024;

    // ---- parsing (pure, unit tested) ----

    /// <summary>First search result. ponytail: no fuzzy re-ranking, wrong picks possible for ambiguous titles; add a Fix match action if it bites.</summary>
    public static Meta? ParseFirstHit(JsonElement root, bool tv)
    {
        if (!root.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var r = results[0];
        var date = Str(r, tv ? "first_air_date" : "release_date");
        return new Meta(
            tv ? "show" : "movie", r.GetProperty("id").GetInt64(),
            Str(r, tv ? "name" : "title") ?? "", Str(r, "overview"), Str(r, "poster_path"), Str(r, "backdrop_path"),
            r.TryGetProperty("vote_average", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0,
            date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null);
    }

    public static List<Ep> ParseSeason(JsonElement root)
    {
        var l = new List<Ep>();
        if (!root.TryGetProperty("episodes", out var eps)) return l;
        foreach (var e in eps.EnumerateArray())
            l.Add(new Ep(e.GetProperty("episode_number").GetInt32(), Str(e, "name") ?? "", Str(e, "overview"), Str(e, "still_path"),
                Str(e, "air_date"), Int(e, "runtime"), e.TryGetProperty("vote_average", out var va) && va.ValueKind == JsonValueKind.Number && va.GetDouble() > 0 ? va.GetDouble() : null));
        return l;
    }

    /// <summary>The named child, only when it is a JSON object (a null or odd value from the server is treated as missing).</summary>
    static bool Obj(JsonElement e, string name, out JsonElement v)
    {
        v = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.Object;
    }

    static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n > 0 ? n : null;

    /// <summary>
    /// Details of one title from movie/{id} or tv/{id} fetched with append_to_response (credits, images, release_dates or content_ratings, external_ids).
    /// Anything missing just stays empty. The cast row is the director (or creator) first, then up to 12 actors.
    /// </summary>
    public static Detail ParseDetail(JsonElement root, bool tv)
    {
        var genres = new List<string>();
        if (root.TryGetProperty("genres", out var gs) && gs.ValueKind == JsonValueKind.Array)
            foreach (var g in gs.EnumerateArray()) if (g.ValueKind == JsonValueKind.Object && Str(g, "name") is { } n) genres.Add(n);

        int? runtime = tv
            ? (root.TryGetProperty("episode_run_time", out var ert) && ert.ValueKind == JsonValueKind.Array && ert.GetArrayLength() > 0 && ert[0].TryGetInt32(out var er) && er > 0 ? er : null)
            : Int(root, "runtime");

        string? cert = null;
        if (tv && Obj(root, "content_ratings", out var cr) && cr.TryGetProperty("results", out var crs))
            cert = PickCertification(crs, e => Str(e, "rating"));
        else if (!tv && Obj(root, "release_dates", out var rd) && rd.TryGetProperty("results", out var rds))
            cert = PickCertification(rds, e =>
                e.TryGetProperty("release_dates", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => Str(x, "certification")).FirstOrDefault(c => c != null) : null);

        string? imdb = Str(root, "imdb_id");
        if (imdb == null && Obj(root, "external_ids", out var ex)) imdb = Str(ex, "imdb_id");

        string? logo = null;
        if (Obj(root, "images", out var im) && im.TryGetProperty("logos", out var logos) && logos.ValueKind == JsonValueKind.Array)
        {
            var best = logos.EnumerateArray()
                .Where(l => Str(l, "file_path") != null && (Str(l, "iso_639_1") is "en" or null))
                .OrderByDescending(l => Str(l, "iso_639_1") == "en")
                .ThenByDescending(l => l.TryGetProperty("vote_average", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0)
                .FirstOrDefault();
            if (best.ValueKind == JsonValueKind.Object) logo = Str(best, "file_path");
        }

        var cast = new List<Credit>();
        if (tv && root.TryGetProperty("created_by", out var cb) && cb.ValueKind == JsonValueKind.Array)
        {
            var c1 = cb.EnumerateArray().FirstOrDefault();
            if (c1.ValueKind == JsonValueKind.Object && Str(c1, "name") is { } cn) cast.Add(new Credit(cn, "Creator", Str(c1, "profile_path")));
        }
        if (Obj(root, "credits", out var cs))
        {
            if (!tv && cs.TryGetProperty("crew", out var crew) && crew.ValueKind == JsonValueKind.Array)
            {
                var dir = crew.EnumerateArray().FirstOrDefault(x => Str(x, "job") == "Director");
                if (dir.ValueKind == JsonValueKind.Object && Str(dir, "name") is { } dn) cast.Add(new Credit(dn, "Director", Str(dir, "profile_path")));
            }
            if (cs.TryGetProperty("cast", out var actors) && actors.ValueKind == JsonValueKind.Array)
                foreach (var a in actors.EnumerateArray().Take(12))
                    if (Str(a, "name") is { } an) cast.Add(new Credit(an, Str(a, "character") ?? "", Str(a, "profile_path")));
        }

        return new Detail(runtime, cert, genres, Str(root, "tagline"), logo, imdb, cast);
    }

    /// <summary>The US certificate if there is one, otherwise the first the list offers.</summary>
    static string? PickCertification(JsonElement results, Func<JsonElement, string?> read)
    {
        if (results.ValueKind != JsonValueKind.Array) return null;
        string? first = null;
        foreach (var e in results.EnumerateArray())
        {
            var c = read(e);
            if (c == null) continue;
            if (Str(e, "iso_3166_1") == "US") return c;
            first ??= c;
        }
        return first;
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    // ---- network ----

    /// <summary>The cheapest request that needs a valid key: the Welcome tour's Test button. Throws like any other request.</summary>
    public static async Task PingAsync() { using var _ = await GetJson("configuration", ""); }

    static async Task<JsonDocument> GetJson(string path, string query)
    {
        var key = Config.TmdbKey ?? throw new InvalidOperationException("no TMDB key");
        var bearer = key.Length > 40; // v4 tokens are long JWTs, v3 keys are 32 chars
        var tail = path + "?language=en-US&" + query + (bearer ? "" : "&api_key=" + key);
        for (int attempt = 0, switched = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Api + tail);
            if (bearer) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            HttpResponseMessage res;
            try { res = await Http.SendAsync(req); }
            catch (HttpRequestException) when (Environment.GetEnvironmentVariable("LUMINA_TMDB_API") == null && switched < Hosts.Length - 1)
            {
                _host = (_host + 1) % Hosts.Length; switched++; attempt--; continue;   // could not connect at all: use the other host from now on
            }
            using var _ = res;
            if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2));
                continue;
            }
            res.EnsureSuccessStatusCode(); // message holds the status only, never the url or key
            return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        }
    }

    /// <summary>One search result as the "Edit metadata" list shows it: the match to store, and the full release date for display.</summary>
    public record Candidate(Meta Meta, string? Date);

    public static List<Candidate> ParseMany(JsonElement root, bool tv, int max = 15)
    {
        var l = new List<Candidate>();
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return l;
        foreach (var r in results.EnumerateArray())
        {
            if (l.Count >= max) break;
            if (!r.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
            var date = Str(r, tv ? "first_air_date" : "release_date");
            var meta = new Meta(
                tv ? "show" : "movie", id.GetInt64(), Str(r, tv ? "name" : "title") ?? "", Str(r, "overview"), Str(r, "poster_path"), Str(r, "backdrop_path"),
                r.TryGetProperty("vote_average", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0,
                date is { Length: >= 4 } && int.TryParse(date[..4], out var y) ? y : null);
            l.Add(new Candidate(meta, date));
        }
        return l;
    }

    /// <summary>Every match TMDB offers for a typed search, for the "Edit metadata" window.</summary>
    public static async Task<List<Candidate>> SearchMany(string kind, string query, int? year = null)
    {
        var tv = kind == "show";
        var q = "query=" + Uri.EscapeDataString(query);
        if (year is { } y) q += (tv ? "&first_air_date_year=" : "&year=") + y;
        using var doc = await GetJson(tv ? "search/tv" : "search/movie", q);
        return ParseMany(doc.RootElement, tv);
    }

    /// <summary>A small poster for the result list, kept in the art cache. Null when there is none or it cannot be fetched.</summary>
    public static async Task<string?> SmallPosterAsync(string? posterPath)
    {
        if (string.IsNullOrEmpty(posterPath)) return null;
        var dest = Db.ArtPath("/w92" + posterPath);
        try
        {
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var bytes = await Http.GetByteArrayAsync(ImgBase + "w92" + posterPath);
                await File.WriteAllBytesAsync(dest + ".part", bytes);
                File.Move(dest + ".part", dest, true);
            }
            return dest;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException) { return null; }
    }

    public static async Task<Meta?> Search(string kind, string title, int? year)
    {
        var tv = kind == "show";
        var q = "query=" + Uri.EscapeDataString(title);
        if (year is { } y) q += (tv ? "&first_air_date_year=" : "&year=") + y;
        using var doc = await GetJson(tv ? "search/tv" : "search/movie", q);
        var hit = ParseFirstHit(doc.RootElement, tv);
        if (hit == null && year != null) return await Search(kind, title, null); // wrong year in the filename
        return hit;
    }

    static async Task Download(string posterPath, string size)
    {
        var dest = Db.ArtPath(posterPath);
        if (File.Exists(dest)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var bytes = await Http.GetByteArrayAsync(ImgBase + size + posterPath);
        await File.WriteAllBytesAsync(dest + ".part", bytes);
        File.Move(dest + ".part", dest, true);
    }

    /// <summary>
    /// Match every unmatched title, fetch season episode names, download missing posters.
    /// Returns an error message, or empty on success. Unmatched work stays queued for the next run.
    /// </summary>
    public static async Task<string> FillAsync(Action<string> report)
    {
        if (string.IsNullOrEmpty(Config.TmdbKey)) return "Add a TMDB key in Settings to get posters";
        try
        {
            var pending = Db.PendingTitles();
            for (int i = 0; i < pending.Count; i++)
            {
                var (kind, title, year) = pending[i];
                report($"Matching {i + 1}/{pending.Count}: {title}");
                var (qt, qy) = Db.SearchTerms(kind, title, year);   // "Fix match" can point a title at a different TMDB search
                Db.SetMatch(kind, title, year, await Search(kind, qt, qy));
            }

            var seasons = Db.SeasonsNeedingEpisodes();
            for (int i = 0; i < seasons.Count; i++)
            {
                report($"Episode names {i + 1}/{seasons.Count}");
                using var doc = await GetJson($"tv/{seasons[i].Id}/season/{seasons[i].Season}", "");
                Db.SetSeason(seasons[i].Id, seasons[i].Season, ParseSeason(doc.RootElement));
            }

            var need = Db.NeedingDetail();
            for (int i = 0; i < need.Count; i++)
            {
                report($"Details {i + 1}/{need.Count}");
                var tv = need[i].Kind == "show";
                try
                {
                    using var doc = await GetJson($"{(tv ? "tv" : "movie")}/{need[i].Id}",
                        "append_to_response=" + (tv ? "credits,images,content_ratings,external_ids" : "credits,images,release_dates") + "&include_image_language=en,null");
                    Db.SetDetail(need[i].Kind, need[i].Id, ParseDetail(doc.RootElement, tv));
                }
                catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { Db.SetDetail(need[i].Kind, need[i].Id, Detail.Empty); }   // nothing more to learn: stop asking
                catch (HttpRequestException) { }                                                                                                       // try again next scan
            }

            if (Omdb.Key != null)
            {
                var rate = Db.NeedingRatings();
                for (int i = 0; i < rate.Count; i++)
                {
                    report($"Scores {i + 1}/{rate.Count}");
                    try { Db.SetRatings(rate[i].Kind, rate[i].Id, await Omdb.FetchAsync(rate[i].ImdbId)); }
                    catch (HttpRequestException) { }
                    catch (System.Text.Json.JsonException) { }
                }
            }

            var posters = Db.PosterPaths().Where(p => !File.Exists(Db.ArtPath(p))).ToList();
            for (int i = 0; i < posters.Count; i++)
            {
                report($"Posters {i + 1}/{posters.Count}");
                try { await Download(posters[i], "w342"); }
                catch (HttpRequestException) { } // one dead poster must not stop the rest
            }
            var backdrops = Db.BackdropPaths().Where(p => !File.Exists(Db.ArtPath(p))).ToList();
            for (int i = 0; i < backdrops.Count; i++)
            {
                report($"Backdrops {i + 1}/{backdrops.Count}");
                try { await Download(backdrops[i], "w780"); }
                catch (HttpRequestException) { }
            }
            foreach (var (paths, size, what) in new[] { (Db.LogoPaths(), "w500", "Logos"), (Db.StillPaths(), "w300", "Episode pictures"), (Db.CastPhotoPaths(), "w185", "Cast photos") })
            {
                var missing = paths.Where(p => !File.Exists(Db.ArtPath(p))).ToList();
                for (int i = 0; i < missing.Count; i++)
                {
                    report($"{what} {i + 1}/{missing.Count}");
                    try { await Download(missing[i], size); }
                    catch (HttpRequestException) { }
                }
            }
            Db.TrimArt(ArtLimitBytes);
            return "";
        }
        catch (HttpRequestException e) { return "TMDB: " + e.Message; }
        catch (TaskCanceledException) { return "TMDB timed out"; }
    }
}
