namespace Lumina.Player;

/// <summary>Reads the user's own mpv.conf for Settings, Advanced, Use my own mpv.conf, minus lines that would break Lumina's window and controller.</summary>
public static class UserMpvConf
{
    public static string Path => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mpv", "mpv.conf");

    static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "config-dir", "include", "profile", "wid", "osc", "input-conf", "input-default-bindings", "input-builtin-bindings",
        "scripts", "script", "load-scripts", "idle", "force-window", "ontop", "ontop-level", "border", "fullscreen", "fs",
        "geometry", "autofit", "autofit-larger", "autofit-smaller", "screen", "fs-screen", "title-bar", "player-operation-mode",
    };

    static bool IsBlocked(string key) => Blocked.Contains(key) || key.StartsWith("window-", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("force-window", StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps the safe option lines. Skipped lines are returned as written. A [profile] section and everything after it is dropped.</summary>
    public static (string Kept, List<string> Skipped) Filter(string text)
    {
        var kept = new List<string>();
        var skipped = new List<string>();
        var inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[')
            {
                inSection = !line.Equals("[default]", StringComparison.OrdinalIgnoreCase);
                if (inSection) skipped.Add(line);
                continue;
            }
            if (inSection) { skipped.Add(line); continue; }
            var key = line.Split('=', 2)[0].Trim().TrimStart('-');
            if (key.StartsWith("no-", StringComparison.OrdinalIgnoreCase)) key = key[3..];
            if (IsBlocked(key)) skipped.Add(line); else kept.Add(line);
        }
        return (string.Join("\n", kept), skipped);
    }

    /// <summary>The filtered file, or empty when it is missing or unreadable. Skipped lines go to the log.</summary>
    public static string Load()
    {
        try
        {
            if (!System.IO.File.Exists(Path)) return "";
            var (kept, skipped) = Filter(System.IO.File.ReadAllText(Path));
            foreach (var s in skipped) Lumina.Diagnostics.Log.Info("Skipped from your mpv.conf (Lumina manages this): " + s);
            return kept;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            Lumina.Diagnostics.Log.Info("Could not read your mpv.conf: " + e.Message);
            return "";
        }
    }
}
