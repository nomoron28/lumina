using System.IO;

namespace Lumina.Library;

public static partial class Scanner
{
    static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".mts", ".webm", ".wmv", ".flv", ".iso" };

    public static bool IsVideo(string path) => Ext.Contains(Path.GetExtension(path));

    static readonly EnumerationOptions Opts = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>Scan every root; returns number of files added or changed.</summary>
    public static int ScanAll()
    {
        // After the name rules change, every file is read again once, so old titles ("Beef 2023") get the new form ("Beef", 2023) and are matched again
        var reparse = Db.KvGet("parse_v") != ParseVersion;
        var overrides = Db.KindOverrides();
        var moved = Db.ShowOverrides();
        var n = Db.Roots().Sum(r => Scan(r, overrides, moved, reparse));
        if (reparse) Db.KvSet("parse_v", ParseVersion);
        Db.ShareMatches();
        return n;
    }

    const string ParseVersion = "4";   // 3: season folders and numbered folders make shows (issue #1); 4: "Show Specials" joins Show (issue #16)

    /// <summary>What the user said this file is: its own setting first, then its folder's. Null when they said nothing.</summary>
    static string? Forced(Dictionary<string, string> overrides, string file) =>
        overrides.TryGetValue(file, out var k) ? k : overrides.TryGetValue(Path.GetDirectoryName(file) ?? "", out k) ? k : null;

    static int Scan(string root, Dictionary<string, string> overrides, Dictionary<string, (string Title, int? Year, int Season)> moved, bool reparse = false)
    {
        // Offline NAS or unplugged drive: keep its rows instead of wiping them.
        if (!Directory.Exists(root)) return 0;

        var found = new List<Found>();
        foreach (var f in Directory.EnumerateFiles(root, "*", Opts))
        {
            if (!Ext.Contains(Path.GetExtension(f))) continue;
            if (Forced(overrides, f) == null && Extras.Skip(f, root)) continue;   // trailers and samples belong to a title (unless the user filed one)
            var fi = new FileInfo(f);
            var name = Path.GetFileNameWithoutExtension(f);
            var p = Forced(overrides, f) == "movie" ? NameParser.AsMovie(name) : InSeasonFolder(f, root, NameParser.Parse(name));
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
        NumberedFolders(found, root, overrides);
        ForcedShows(found, root, overrides);
        SpecialsOfShows(found, moved);
        MovedEpisodes(found, moved);
        return Db.SyncRoot(root, found, reparse);
    }

    /// <summary>Files the user moved into another show: that show's title and season, episodes kept or numbered in name order.</summary>
    static void MovedEpisodes(List<Found> found, Dictionary<string, (string Title, int? Year, int Season)> moved)
    {
        foreach (var g in found.Select((x, i) => (x, i)).Where(t => moved.ContainsKey(t.x.Path)).GroupBy(t => moved[t.x.Path]))
        {
            var n = 0;
            foreach (var (x, i) in g.OrderBy(t => Path.GetFileName(t.x.Path), StringComparer.OrdinalIgnoreCase))
            {
                n++;
                var ep = x.P.Kind == "show" && x.P.Episode is int e ? e : NameParser.LooseEpisode(Path.GetFileNameWithoutExtension(x.Path), x.P.Title) ?? n;
                found[i] = x with { P = new Parsed("show", g.Key.Title, g.Key.Year, g.Key.Season, ep) };
            }
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(.+?)[\s._-]+(specials?|ovas?|oads?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SpecialsTitle();

    static string Key(string title) => new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>"One Punch Man Specials" or "One Punch Man OVA" is the Specials season of One Punch Man when that show is here too (issue #16).</summary>
    static void SpecialsOfShows(List<Found> found, Dictionary<string, (string Title, int? Year, int Season)> moved)
    {
        var shows = found.Where(f => f.P.Kind == "show").GroupBy(f => Key(f.P.Title)).ToDictionary(g => g.Key, g => g.First().P);
        for (int i = 0; i < found.Count; i++)
        {
            var f = found[i];
            if (f.P.Kind != "show" || moved.ContainsKey(f.Path) || SpecialsTitle().Match(f.P.Title) is not { Success: true } m) continue;
            if (!shows.TryGetValue(Key(m.Groups[1].Value), out var show)) continue;
            found[i] = f with { P = f.P with { Title = show.Title, Year = show.Year, Season = 0 } };
        }
    }

    /// <summary>Files the user said are a show but whose names say nothing of episodes: numbered in name order, season from the folder or 1.</summary>
    static void ForcedShows(List<Found> found, string root, Dictionary<string, string> overrides)
    {
        foreach (var g in found.Select((x, i) => (x, i)).Where(t => t.x.P.Kind == "movie" && Forced(overrides, t.x.Path) == "show")
                     .GroupBy(t => Path.GetDirectoryName(t.x.Path) ?? ""))
        {
            var season = NameParser.SeasonFolder(Path.GetFileName(g.Key));
            var showDir = season != null ? Path.GetDirectoryName(g.Key) ?? g.Key : g.Key;
            var (title, year) = IsRoot(showDir, root) ? (g.First().x.P.Title, g.First().x.P.Year) : NameParser.SplitYear(Path.GetFileName(showDir));
            var n = 0;
            foreach (var (x, i) in g.OrderBy(t => Path.GetFileName(t.x.Path), StringComparer.OrdinalIgnoreCase))
            {
                n++;
                var ep = NameParser.LooseEpisode(Path.GetFileNameWithoutExtension(x.Path), title) ?? n;
                found[i] = x with { P = new Parsed("show", title, year, season ?? 1, ep) };
            }
        }
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
    static void NumberedFolders(List<Found> found, string root, Dictionary<string, string> overrides)
    {
        foreach (var g in found.Select((x, i) => (x, i)).Where(t => t.x.P.Kind == "movie" && t.x.P.Year == null && Forced(overrides, t.x.Path) == null)
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
