using System.Text.RegularExpressions;

namespace Lumina.Library;

public record Parsed(string Kind, string Title, int? Year, int? Season, int? Episode);

/// <summary>"Show.Name.S02E07.1080p.x265.mkv" or "Movie Name (2019) 2160p.mkv" -> structured fields.</summary>
public static partial class NameParser
{
    [GeneratedRegex(@"[Ss](\d{1,2})[ ._-]?[Ee](\d{1,3})|\b(\d{1,2})x(\d{2,3})\b")]
    private static partial Regex Ep();

    [GeneratedRegex(@"\b(19\d{2}|20\d{2})\b")]
    private static partial Regex Year();

    // First of these marks the start of release junk when there is no year.
    [GeneratedRegex(@"\b(480p|576p|720p|1080[pi]|2160p|4k|uhd|bluray|blu-ray|bdrip|brrip|web-?dl|webrip|hdtv|dvdrip|remux|hdr10?|dv|x264|x265|h\.?264|h\.?265|hevc|avc|av1|xvid|dts|ddp?\d|aac|atmos|10bit|proper|repack|extended|remastered)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Junk();

    public static Parsed Parse(string fileNameNoExt)
    {
        var s = fileNameNoExt.Replace('.', ' ').Replace('_', ' ');
        s = Regex.Replace(s, @"\[[^\]]*\]", " "); // [Group] tags

        var ep = Ep().Match(s);
        if (ep.Success)
        {
            var season = int.Parse(ep.Groups[1].Success ? ep.Groups[1].Value : ep.Groups[3].Value);
            var episode = int.Parse(ep.Groups[2].Success ? ep.Groups[2].Value : ep.Groups[4].Value);
            var (show, showYear) = SplitYear(Clean(s[..ep.Index]));
            return new Parsed("show", show, showYear, season, episode);
        }

        // Last plausible year that is not the very start, so 2012 and 1917 stay titles.
        Match? ym = null;
        foreach (Match m in Year().Matches(s)) if (m.Index > 0) ym = m;
        if (ym != null) return new Parsed("movie", Clean(s[..ym.Index]), int.Parse(ym.Value), null, null);

        var j = Junk().Match(s);
        return new Parsed("movie", Clean(j.Success ? s[..j.Index] : s), null, null, null);
    }

    /// <summary>"Beef 2023" (from "Beef (2023)") -> "Beef" and 2023, so the year is a hint for the search and not part of the name. A year that is the whole title stays a title.</summary>
    public static (string Title, int? Year) SplitYear(string title)
    {
        var m = TrailingYear().Match(title);
        if (!m.Success || int.Parse(m.Groups[2].Value) > DateTime.Now.Year + 1) return (title, null);
        return (m.Groups[1].Value.Trim(' ', '-'), int.Parse(m.Groups[2].Value));
    }

    [GeneratedRegex(@"^(.*\S)\s+\(?((?:19|20)\d{2})\)?$")]
    private static partial Regex TrailingYear();

    static string Clean(string t) => Regex.Replace(t.Replace('(', ' ').Replace(')', ' '), @"\s+", " ").Trim(' ', '-');
}
