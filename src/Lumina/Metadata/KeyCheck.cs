using System.Net;
using System.Net.Http;

namespace Lumina.Metadata;

/// <summary>The three optional service keys: what each adds, where to get one free, and a check that the saved key works.</summary>
public static class KeyCheck
{
    public sealed record Service(string Key, string Name, string What, string SignUp);

    public static readonly IReadOnlyList<Service> Services = new Service[]
    {
        new("tmdbKey", "TMDB", "Posters, summaries, cast and episode names", "https://www.themoviedb.org/settings/api"),
        new("omdbKey", "OMDb", "IMDb and Rotten Tomatoes scores", "https://www.omdbapi.com/apikey.aspx"),
        new("openSubtitlesKey", "OpenSubtitles", "Subtitle search while a video plays", "https://www.opensubtitles.com/en/consumers"),
    };

    /// <summary>Null when the saved key works, otherwise a short reason to show next to the field.</summary>
    public static async Task<string?> TestAsync(Service s)
    {
        if (string.IsNullOrWhiteSpace(Config.Raw(s.Key))) return "Paste a key first";
        try
        {
            await (s.Key switch
            {
                "tmdbKey" => Tmdb.PingAsync(),
                "omdbKey" => Omdb.PingAsync(),
                _ => Lumina.Subtitles.OpenSubtitles.PingAsync(),
            });
            return null;
        }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { return $"{s.Name} did not accept this key"; }
        catch (HttpRequestException e) when (e.StatusCode is { } code) { return $"{s.Name} answered with error {(int)code}. Try again later"; }
        catch (Lumina.Subtitles.OpenSubtitles.SubsException e) { return e.Message.Contains("rejected", StringComparison.Ordinal) ? $"{s.Name} did not accept this key" : e.Message; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return $"Could not reach {s.Name}. Check your connection"; }
    }
}
