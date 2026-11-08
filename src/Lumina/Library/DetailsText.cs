using System.Globalization;
using Lumina.Player;

namespace Lumina.Library;

/// <summary>What Lumina knows about a file's streams, read once by a quiet background probe and cached.</summary>
public record FileSpec(long Size, string? Video, int? Width, int? Height, double? Fps, string? Audio, int? Channels, double? Mbps);

/// <summary>Plain-language lines for the details page. Pure, so the wording is unit tested.</summary>
public static class DetailsText
{
    /// <summary>"1 hr 40 min", "54 min", "2 hr".</summary>
    public static string Runtime(int minutes)
    {
        if (minutes <= 0) return "";
        var h = minutes / 60; var m = minutes % 60;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} hr" : $"{h} hr {m} min";
    }

    /// <summary>
    /// The usual name for a picture size: 4K, 1440p, 1080p, 720p, 480p, otherwise the height. Width decides when known,
    /// because widescreen films are cropped (1920x800 is still "1080p"); height decides when it is not.
    /// </summary>
    public static string Quality(int? width, int? height)
    {
        if (height is not { } h || h <= 0) return "";
        if (width is { } w and > 0)
            return w switch { >= 3400 => "4K", >= 2400 => "1440p", >= 1800 => "1080p", >= 1200 => "720p", >= 800 => "480p", _ => h + "p" };
        return h switch { >= 2000 => "4K", >= 1400 => "1440p", >= 1000 => "1080p", >= 680 => "720p", >= 460 => "480p", _ => h + "p" };
    }

    public static string VideoCodec(string? mpvName) => (mpvName ?? "").ToLowerInvariant() switch
    {
        "" => "", "h264" => "H.264", "hevc" => "HEVC", "av1" => "AV1", "vp9" => "VP9", "vp8" => "VP8", "mpeg4" => "MPEG-4",
        "mpeg2video" => "MPEG-2", "mpeg1video" => "MPEG-1", "vc1" => "VC-1", "wmv3" => "WMV", var other => other.ToUpperInvariant(),
    };

    public static string AudioCodec(string? mpvName) => (mpvName ?? "").ToLowerInvariant() switch
    {
        "" => "", "aac" => "AAC", "ac3" => "AC3", "eac3" => "E-AC3", "dts" => "DTS", "truehd" => "TrueHD", "flac" => "FLAC", "opus" => "Opus",
        "vorbis" => "Vorbis", "mp3" => "MP3", "pcm_s16le" or "pcm_s24le" or "pcm_s32le" => "PCM", var other => other.ToUpperInvariant(),
    };

    /// <summary>Channel counts the way people say them: 2.0, 5.1, 7.1.</summary>
    public static string ChannelLayout(int? n) => n switch { null or <= 0 => "", 1 => "1.0", 2 => "2.0", 3 => "2.1", 6 => "5.1", 7 => "6.1", 8 => "7.1", var c => c + " ch" };

    /// <summary>"332.6 MB   H.264 (360p)   AAC 2.0   0.5 Mbps   23.976 fps", leaving out whatever is not known.</summary>
    public static List<string> SpecParts(FileSpec s)
    {
        var parts = new List<string>();
        if (s.Size > 0) parts.Add(MediaInfo.Size(s.Size));
        var v = VideoCodec(s.Video); var q = Quality(s.Width, s.Height);
        if (v.Length > 0 || q.Length > 0) parts.Add(v + (v.Length > 0 && q.Length > 0 ? " " : "") + (q.Length > 0 ? $"({q})" : ""));
        var a = AudioCodec(s.Audio); var c = ChannelLayout(s.Channels);
        if (a.Length > 0) parts.Add(a + (c.Length > 0 ? " " + c : ""));
        if (s.Mbps is { } m && m > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{m:0.0} Mbps"));
        if (s.Fps is { } f && f > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{f:0.###} fps"));
        return parts;
    }

    /// <summary>Runtime, year and genres for the line above the synopsis. The certificate is shown as a badge, not here.</summary>
    public static List<string> MetaParts(int? runtimeMinutes, string? when, string? quality, IEnumerable<string> genres)
    {
        var l = new List<string>();
        if (runtimeMinutes is { } r && Runtime(r) is { Length: > 0 } rt) l.Add(rt);
        if (!string.IsNullOrEmpty(when)) l.Add(when);
        if (!string.IsNullOrEmpty(quality)) l.Add(quality);
        var g = string.Join(", ", genres.Take(3));
        if (g.Length > 0) l.Add(g);
        return l;
    }

    /// <summary>"13 Oct 2026" from a TMDB date. Anything that is not a date comes back empty.</summary>
    public static string AirDate(string? iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "";

    /// <summary>"E1 - No Strings". With more than one season the season leads: "S2E1 - Title". No name gives just the number.</summary>
    public static string EpisodeHeading(int? season, int? episode, string? name, bool showSeason)
    {
        var code = (showSeason && season is { } s ? $"S{s}" : "") + (episode is { } e ? $"E{e}" : "");
        return string.IsNullOrWhiteSpace(name) ? code : code.Length == 0 ? name! : $"{code} - {name}";
    }

    /// <summary>"AN" for Anne Gee Byrd, "D" for a single name. Shown where a person has no photo.</summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "" : words.Length == 1 ? words[0][..1].ToUpperInvariant() : (words[0][..1] + words[^1][..1]).ToUpperInvariant();
    }

    /// <summary>The score line: TMDB as a percentage, then IMDb, then Rotten Tomatoes. Only what exists.</summary>
    /// <summary>Rotten Tomatoes' brand rule: 60% and up is Fresh (tomato), 59% and below is Rotten (splat).</summary>
    public static bool IsFresh(string score) =>
        double.TryParse(score.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct >= 60;

    public static List<(string Source, string Value)> Scores(double? tmdb, string? imdb, string? rottenTomatoes)
    {
        var l = new List<(string, string)>();
        if (tmdb is { } t && t > 0) l.Add(("TMDB", $"{Math.Round(t * 10):0}%"));
        if (!string.IsNullOrEmpty(imdb)) l.Add(("IMDb", imdb!));
        if (!string.IsNullOrEmpty(rottenTomatoes)) l.Add(("Rotten Tomatoes", rottenTomatoes!));
        return l;
    }
}
