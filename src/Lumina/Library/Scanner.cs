using System.IO;

namespace Lumina.Library;

public static class Scanner
{
    static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".mts", ".webm", ".wmv", ".flv", ".iso" };

    static readonly EnumerationOptions Opts = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>Scan every root; returns number of files added or changed.</summary>
    public static int ScanAll()
    {
        // After the name rules change, every file is read again once, so old titles ("Beef 2023") get the new form ("Beef", 2023) and are matched again
        var reparse = Db.KvGet("parse_v") != ParseVersion;
        var n = Db.Roots().Sum(r => Scan(r, reparse));
        if (reparse) Db.KvSet("parse_v", ParseVersion);
        return n;
    }

    const string ParseVersion = "3";   // 3: season folders and numbered folders make shows (issue #1)

    static int Scan(string root, bool reparse = false)
    {
        // Offline NAS or unplugged drive: keep its rows instead of wiping them.
        if (!Directory.Exists(root)) return 0;

        var found = new List<Found>();
        foreach (var f in Directory.EnumerateFiles(root, "*", Opts))
        {
            if (!Ext.Contains(Path.GetExtension(f))) continue;
            var fi = new FileInfo(f);
            var p = InSeasonFolder(f, root, NameParser.Parse(Path.GetFileNameWithoutExtension(f)));
            // A bare S01E02.mkv inside Show Name/Season 1/ takes the show from the folder.
            if (p.Title.Length == 0)
            {
                var dir = Path.GetDirectoryName(f) ?? "";
                if (p.Kind == "show") dir = Path.GetDirectoryName(dir) ?? dir;
                var (folder, folderYear) = p.Kind == "show" ? NameParser.SplitYear(Path.GetFileName(dir)) : (Path.GetFileName(dir), p.Year);
                p = p with { Title = folder, Year = folderYear ?? p.Year };
            }
            if (p.Title.Length == 0) p = p with { Title = Path.GetFileNameWithoutExtension(f) };
            found.Add(new Found(f, fi.Length, fi.LastWriteTimeUtc.Ticks, p));
        }
        NumberedFolders(found, root);
        return Db.SyncRoot(root, found, reparse);
    }

    static bool IsRoot(string dir, string root) =>
        string.Equals(Path.GetFullPath(dir).TrimEnd('\\', '/'), Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A file in "Show Name/Season 2/" is an episode of Show Name's season 2 even without an SxxEyy tag.</summary>
    static Parsed InSeasonFolder(string file, string root, Parsed p)
    {
        var dir = Path.GetDirectoryName(file);
        if (dir == null || IsRoot(dir, root) || NameParser.SeasonFolder(Path.GetFileName(dir)) is not int season) return p;
        var name = Path.GetFileNameWithoutExtension(file);
        if (p.Kind == "show") return NameParser.HasSeasonTag(name) ? p : p with { Season = season };
        var showDir = Path.GetDirectoryName(dir);
        var (title, year) = showDir != null && !IsRoot(showDir, root) ? NameParser.SplitYear(Path.GetFileName(showDir)) : (p.Title, p.Year);
        return NameParser.LooseEpisode(name, title) is int ep ? new Parsed("show", title, year, season, ep) : p;
    }

    /// <summary>Three or more files in one folder numbered 01, 02, 03 ("Anime Title 01.mkv") are a show's episodes.</summary>
    static void NumberedFolders(List<Found> found, string root)
    {
        foreach (var g in found.Select((x, i) => (x, i)).Where(t => t.x.P.Kind == "movie" && t.x.P.Year == null)
                     .GroupBy(t => Path.GetDirectoryName(t.x.Path) ?? ""))
        {
            if (g.Key.Length == 0 || IsRoot(g.Key, root)) continue;
            var (title, year) = NameParser.SplitYear(Path.GetFileName(g.Key));
            var eps = g.Select(t => (t.i, ep: NameParser.LooseEpisode(Path.GetFileNameWithoutExtension(t.x.Path), title, padded: true))).ToList();
            if (eps.Count(e => e.ep != null) < 3 || eps.Where(e => e.ep != null).Select(e => e.ep).Distinct().Count() < 3) continue;
            foreach (var (i, ep) in eps.Where(e => e.ep != null))
                found[i] = found[i] with { P = new Parsed("show", title, year, 1, ep) };
        }
    }
}
