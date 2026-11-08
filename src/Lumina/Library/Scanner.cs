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

    const string ParseVersion = "2";

    static int Scan(string root, bool reparse = false)
    {
        // Offline NAS or unplugged drive: keep its rows instead of wiping them.
        if (!Directory.Exists(root)) return 0;

        var found = new List<Found>();
        foreach (var f in Directory.EnumerateFiles(root, "*", Opts))
        {
            if (!Ext.Contains(Path.GetExtension(f))) continue;
            var fi = new FileInfo(f);
            var p = NameParser.Parse(Path.GetFileNameWithoutExtension(f));
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
        return Db.SyncRoot(root, found, reparse);
    }
}
