using System.Text.RegularExpressions;
using Lumina.Player;

namespace Lumina.Library;

/// <summary>
/// Several files of one film (a 1080p and a 4K copy): which one Play opens by default, and how each is named in the Version picker.
/// </summary>
public static class Versions
{
    /// <summary>
    /// The versions in the order the picker lists them, the one Play should open first: the one watched last, otherwise the biggest file
    /// (usually the best picture). Equal files keep a steady order by path.
    /// </summary>
    public static List<MediaItem> Order(IEnumerable<MediaItem> files) =>
        files.OrderByDescending(i => i.PlayedAt).ThenByDescending(i => i.Size).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();

    static readonly (Regex Pattern, string Name)[] Tokens =
    {
        (new(@"(?<![a-z0-9])(2160p|4k|uhd)(?![a-z0-9])", RegexOptions.IgnoreCase), "4K"),
        (new(@"(?<![a-z0-9])1440p(?![a-z0-9])", RegexOptions.IgnoreCase), "1440p"),
        (new(@"(?<![a-z0-9])1080[pi](?![a-z0-9])", RegexOptions.IgnoreCase), "1080p"),
        (new(@"(?<![a-z0-9])720p(?![a-z0-9])", RegexOptions.IgnoreCase), "720p"),
        (new(@"(?<![a-z0-9])(576|480)p(?![a-z0-9])", RegexOptions.IgnoreCase), "SD"),
        (new(@"(?<![a-z0-9])remux(?![a-z0-9])", RegexOptions.IgnoreCase), "Remux"),
        (new(@"(?<![a-z0-9])(blu-?ray|bdrip|brrip)(?![a-z0-9])", RegexOptions.IgnoreCase), "BluRay"),
        (new(@"(?<![a-z0-9])(web-?dl|webrip)(?![a-z0-9])", RegexOptions.IgnoreCase), "WEB"),
        (new(@"(?<![a-z0-9])(dolby[ .]?vision|dv)(?![a-z0-9])", RegexOptions.IgnoreCase), "Dolby Vision"),
        (new(@"(?<![a-z0-9])hdr(10\+?)?(?![a-z0-9])", RegexOptions.IgnoreCase), "HDR"),
        (new(@"(?<![a-z0-9])(x265|h\.?265|hevc)(?![a-z0-9])", RegexOptions.IgnoreCase), "HEVC"),
        (new(@"(?<![a-z0-9])(x264|h\.?264|avc)(?![a-z0-9])", RegexOptions.IgnoreCase), "H.264"),
    };

    /// <summary>What the file name says about the picture ("4K · HDR · HEVC"), in a fixed order, empty when it says nothing.</summary>
    public static string Qualities(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        return string.Join(" · ", Tokens.Where(t => t.Pattern.IsMatch(name)).Select(t => t.Name));
    }

    /// <summary>One line for the picker: the qualities and the size, or the file name when it gives no clue ("1080p · BluRay · 2.1 GB", "Film (2020) extended.mkv · 2.1 GB").</summary>
    public static string Label(MediaItem v)
    {
        var q = Qualities(v.Path);
        return (q.Length > 0 ? q : System.IO.Path.GetFileName(v.Path)) + " · " + MediaInfo.Size(v.Size);
    }
}
