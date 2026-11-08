using System.Text.Json;

namespace Lumina.Metadata;

/// <summary>One person on a title's cast and crew row. Role is the character, or "Director" / "Creator".</summary>
public record Credit(string Person, string Role, string? Photo);

/// <summary>Everything the details page shows beyond the basic match: runtime, rating certificate, genres, logo and people.</summary>
public record Detail(int? Runtime, string? Certification, IReadOnlyList<string> Genres, string? Tagline, string? Logo, string? ImdbId, IReadOnlyList<Credit> Cast)
{
    public static readonly Detail Empty = new(null, null, Array.Empty<string>(), null, null, null, Array.Empty<Credit>());
}

/// <summary>Scores from OMDb, already formatted for display ("6.2", "86%"). Null when OMDb does not have one.</summary>
public record Ratings(string? Imdb, string? RottenTomatoes)
{
    public bool Any => Imdb != null || RottenTomatoes != null;
}

/// <summary>OMDb lookups by IMDb id. Needs the user's free key; without one nothing here runs.</summary>
public static class Omdb
{
    static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    // LUMINA_OMDB_API points tests at a local fake server.
    static string Api => Environment.GetEnvironmentVariable("LUMINA_OMDB_API") ?? "https://www.omdbapi.com/";

    public static string? Key => Config.OmdbKey;

    public static Ratings ParseRatings(JsonElement root)
    {
        if (root.TryGetProperty("Response", out var ok) && ok.GetString() == "False") return new Ratings(null, null);
        string? imdb = null, rt = null;
        if (root.TryGetProperty("Ratings", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var r in rs.EnumerateArray())
            {
                var source = r.TryGetProperty("Source", out var s) ? s.GetString() : null;
                var value = r.TryGetProperty("Value", out var v) ? v.GetString() : null;
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (source == "Internet Movie Database") imdb = value.Replace("/10", "");
                else if (source == "Rotten Tomatoes") rt = value;
            }
        if (imdb == null && root.TryGetProperty("imdbRating", out var ir) && ir.GetString() is { } t && t != "N/A") imdb = t;
        return new Ratings(imdb, rt);
    }

    public static async Task<Ratings> FetchAsync(string imdbId)
    {
        var key = Key ?? throw new InvalidOperationException("no OMDb key");
        using var res = await Http.GetAsync(Api + "?i=" + Uri.EscapeDataString(imdbId) + "&apikey=" + Uri.EscapeDataString(key));
        res.EnsureSuccessStatusCode();   // the message holds the status only, never the address or key
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        return ParseRatings(doc.RootElement);
    }
}
