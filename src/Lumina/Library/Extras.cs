using System.IO;
using System.Text.RegularExpressions;

namespace Lumina.Library;

/// <summary>A trailer, featurette or other bonus video that belongs to a title instead of being one.</summary>
public sealed record Extra(string Path, string Kind, string Name);

/// <summary>
/// Trailers and bonus videos, named the way Plex and Jellyfin name them: "Inception-trailer.mkv" beside the film,
/// "trailer.mkv" in the film's own folder, or any file in an "Extras", "Trailers", "Featurettes" (and so on) folder.
/// The scanner leaves them out of the library; the details page lists them under their title. Samples are left out of both.
/// </summary>
public static partial class Extras
{
    // suffix or folder word -> what the details page calls it
    static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trailer"] = "Trailer", ["trailers"] = "Trailer",
        ["featurette"] = "Featurette", ["featurettes"] = "Featurette",
        ["behindthescenes"] = "Behind the scenes", ["behind the scenes"] = "Behind the scenes",
        ["deleted"] = "Deleted scene", ["deletedscene"] = "Deleted scene", ["deleted scenes"] = "Deleted scene",
        ["interview"] = "Interview", ["interviews"] = "Interview",
        ["scene"] = "Scene", ["scenes"] = "Scene",
        ["short"] = "Short", ["shorts"] = "Short",
        ["clip"] = "Clip", ["clips"] = "Clip",
        ["extra"] = "Extra", ["extras"] = "Extra",
        ["other"] = "Extra", ["others"] = "Extra",
    };

    [GeneratedRegex(@"-(trailer|featurette|behindthescenes|deleted|deletedscene|interview|scene|short|clip|extra|other)$", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"(^|-)sample$", RegexOptions.IgnoreCase)]
    private static partial Regex SampleName();

    static string Stem(string path) => System.IO.Path.GetFileNameWithoutExtension(path);
    static string ParentName(string path) => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? "");

    /// <summary>A release's sample clip: never shown anywhere.</summary>
    public static bool IsSample(string path) =>
        SampleName().IsMatch(Stem(path)) || ParentName(path) is { } d && (d.Equals("sample", StringComparison.OrdinalIgnoreCase) || d.Equals("samples", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What kind of extra this file is, or null when it is a film or an episode. An extras folder counts only below a
    /// library root: a root called "Shorts" is a folder of short films.
    /// </summary>
    public static string? KindOf(string path, string? root = null)
    {
        var stem = Stem(path);
        if (Suffix().Match(stem) is { Success: true } m) return Kinds[m.Groups[1].Value];
        if (stem.Equals("trailer", StringComparison.OrdinalIgnoreCase)) return "Trailer";   // "trailer.mkv" in the film's folder
        var dir = System.IO.Path.GetDirectoryName(path);
        if (dir == null || root != null && Norm(dir).Equals(Norm(root), StringComparison.OrdinalIgnoreCase)) return null;
        return Kinds.TryGetValue(System.IO.Path.GetFileName(dir), out var k) ? k : null;
    }

    /// <summary>The scanner skips these: samples and extras.</summary>
    public static bool Skip(string path, string root) => IsSample(path) || KindOf(path, root) != null;

    /// <summary>
    /// The extras of a title, found next to its files. Beside a film: files named after it with an extras suffix. In a folder
    /// of its own (not a library root, where many films share one folder): any extras file, and every file in extras folders.
    /// For a show, the show's folder is searched too (the one above "Season 1").
    /// </summary>
    public static List<Extra> For(IEnumerable<string> titleFiles, IEnumerable<string> roots)
    {
        var files = titleFiles.ToList();
        var stems = files.Select(Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rootSet = roots.Select(Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (System.IO.Path.GetDirectoryName(f) is not { } d) continue;
            dirs.Add(d);
            if (NameParser.SeasonFolder(System.IO.Path.GetFileName(d)) != null && System.IO.Path.GetDirectoryName(d) is { } show) dirs.Add(show);
        }

        var found = new Dictionary<string, Extra>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            var own = !rootSet.Contains(Norm(dir));
            foreach (var f in SafeFiles(dir))
            {
                if (IsSample(f) || KindOf(f) is not { } kind) continue;
                var stem = Stem(f);
                var m = Suffix().Match(stem);
                var owner = m.Success ? stem[..m.Index] : null;
                if (!own && (owner == null || !stems.Contains(owner))) continue;   // in a shared folder, only extras named after this title
                found[f] = new Extra(f, kind, owner != null && !stems.Contains(owner) && owner.Trim().Length > 0 ? owner.Trim() : kind);
            }
            if (!own) continue;
            foreach (var sub in SafeDirs(dir))
            {
                if (!Kinds.TryGetValue(System.IO.Path.GetFileName(sub), out var kind)) continue;
                foreach (var f in SafeFiles(sub))
                    if (!IsSample(f)) found[f] = new Extra(f, kind, Stem(f));
            }
        }
        return found.Values.Where(e => Scanner.IsVideo(e.Path)).OrderBy(e => e.Kind != "Trailer").ThenBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Norm(string dir) => System.IO.Path.GetFullPath(dir).TrimEnd('\\', '/');
    static IEnumerable<string> SafeFiles(string dir) { try { return Directory.GetFiles(dir); } catch (Exception) { return Array.Empty<string>(); } }
    static IEnumerable<string> SafeDirs(string dir) { try { return Directory.GetDirectories(dir); } catch (Exception) { return Array.Empty<string>(); } }
}
