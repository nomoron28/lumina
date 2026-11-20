using System.Text.RegularExpressions;

namespace Lumina.Library;

/// <summary>A stretch of a video the player offers to skip: "intro", "recap" or "credits", in seconds.</summary>
public sealed record Segment(string Kind, double Start, double End);

/// <summary>A mark the user set while watching. Credits need only a start; they run to the end of the file.</summary>
public sealed record SkipMark(string Kind, double? Start, double? End);

/// <summary>
/// Where the intro, recap and credits are. Marks set on this file come first, then marks set on another episode of the
/// same season, then the file's chapter names ("Opening", "Previously on", "Credits"). Finding them from the audio is for later.
/// </summary>
public static partial class SkipMarks
{
    public static readonly string[] Kinds = { "intro", "recap", "credits" };

    [GeneratedRegex(@"^(intro(duction)?|opening( credits| theme| song)?|op\d*|title sequence|main titles?)$", RegexOptions.IgnoreCase)]
    private static partial Regex IntroName();
    [GeneratedRegex(@"^(recap|previously( on.*)?|last time( on.*)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex RecapName();
    [GeneratedRegex(@"^((end(ing)?|closing) ?(credits|theme|song)?|credits|ed\d*|outro)$", RegexOptions.IgnoreCase)]
    private static partial Regex CreditsName();

    public static string? KindOfChapter(string title)
    {
        var t = title.Trim();
        return IntroName().IsMatch(t) ? "intro" : RecapName().IsMatch(t) ? "recap" : CreditsName().IsMatch(t) ? "credits" : null;
    }

    /// <summary>A chapter named like an intro runs until the next chapter starts (or the file ends).</summary>
    public static List<Segment> FromChapters(IReadOnlyList<(string Title, double Time)> chapters, double duration)
    {
        var list = new List<Segment>();
        for (int i = 0; i < chapters.Count; i++)
        {
            if (KindOfChapter(chapters[i].Title) is not { } kind) continue;
            var end = i + 1 < chapters.Count ? chapters[i + 1].Time : duration;
            if (end > chapters[i].Time) list.Add(new Segment(kind, chapters[i].Time, end));
        }
        return list;
    }

    /// <summary>A mark becomes a segment once it is complete and fits in this file.</summary>
    public static Segment? FromMark(SkipMark m, double duration)
    {
        if (m.Start is not double s || s < 0 || (duration > 0 && s >= duration)) return null;
        var e = m.Kind == "credits" ? (duration > 0 ? duration : m.End ?? 0) : m.End ?? 0;
        if (duration > 0) e = Math.Min(e, duration);
        return e > s ? new Segment(m.Kind, s, e) : null;
    }

    /// <summary>For each kind, the file's own mark, else the season's, else the chapters'.</summary>
    public static List<Segment> Combine(IEnumerable<SkipMark> fileMarks, IEnumerable<SkipMark> seasonMarks, IEnumerable<Segment> chapters, double duration)
    {
        var own = fileMarks.Select(m => FromMark(m, duration)).OfType<Segment>().ToList();
        var season = seasonMarks.Select(m => FromMark(m, duration)).OfType<Segment>().ToList();
        var chap = chapters.ToList();
        var result = new List<Segment>();
        foreach (var kind in Kinds)
        {
            var pick = own.Where(x => x.Kind == kind).ToList();
            if (pick.Count == 0) pick = season.Where(x => x.Kind == kind).ToList();
            if (pick.Count == 0) pick = chap.Where(x => x.Kind == kind).ToList();
            result.AddRange(pick);
        }
        return result.OrderBy(x => x.Start).ToList();
    }

    public static string FileScope(string path) => "file:" + path;

    /// <summary>The season a mark is shared with: the show by its TMDB id when matched, otherwise by name.</summary>
    public static string? SeasonScope(MediaItem item) =>
        item.Kind == "show" && item.Season is int s ? $"season:{(item.TmdbId > 0 ? "tmdb:" + item.TmdbId : "title:" + item.FileTitle.ToLowerInvariant())}:{s}" : null;
}
