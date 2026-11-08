using System.Text;
using System.Text.Json;

namespace Lumina.Player;

public sealed record ShortcutAction(string Id, string Label, string Command, string[] DefaultKeys);

/// <summary>
/// The keys Lumina understands and the user's changes to them. Pure: it only turns a table of actions plus overrides
/// into mpv input lines and finds clashes. Keys are mpv key names (SPACE, LEFT, m, Ctrl+s).
/// </summary>
public static class Shortcuts
{
    public static readonly IReadOnlyList<ShortcutAction> Actions = new ShortcutAction[]
    {
        new("playpause", "Play or pause", "cycle pause", new[] { "SPACE" }),
        new("skipback", "Skip back", "script-message lumina-skip -1", new[] { "LEFT" }),
        new("skipfwd", "Skip forward", "script-message lumina-skip 1", new[] { "RIGHT" }),
        new("volup", "Volume up", "script-message lumina-volume 5", new[] { "UP" }),
        new("voldown", "Volume down", "script-message lumina-volume -5", new[] { "DOWN" }),
        new("mute", "Mute", "cycle mute", new[] { "m" }),
        new("fullscreen", "Fullscreen", "script-message lumina-fullscreen", new[] { "f", "F11" }),
        new("exitfs", "Leave fullscreen, then back to the page", "script-message lumina-exit-fullscreen", new[] { "ESC" }),
        new("next", "Next item", "script-message lumina-next", new[] { "N" }),
        new("prev", "Previous item", "script-message lumina-prev", new[] { "P" }),
        new("subsearch", "Search subtitles", "script-message lumina-subs", new[] { "Ctrl+b" }),
        new("subcycle", "Next subtitle track", "script-message lumina-cycle sub", new[] { "v", "j" }),
        new("audiocycle", "Next audio track", "script-message lumina-cycle audio", new[] { "b", "SHARP" }),
        new("crop", "Next crop ratio", "script-message lumina-do crop", new[] { "c" }),
        new("aspect", "Next aspect ratio", "script-message lumina-do aspect", new[] { "a" }),
        new("snapshot", "Snapshot", "screenshot", new[] { "s" }),
        new("slower", "Slower", "script-message lumina-speed -0.1", new[] { "[" }),
        new("faster", "Faster", "script-message lumina-speed 0.1", new[] { "]" }),
        new("speedreset", "Normal speed", "set speed 1", new[] { "BS" }),
        new("framefwd", "Next frame", "frame-step", new[] { "." }),
        new("frameback", "Previous frame", "frame-back-step", new[] { "," }),
        new("chapnext", "Next chapter", "add chapter 1", new[] { "PGDWN" }),
        new("chapprev", "Previous chapter", "add chapter -1", new[] { "PGUP" }),
        new("miniplayer", "Miniplayer (picture in picture)", "script-message lumina-miniplayer", new[] { "w" }),
        new("bookmark", "Add or remove a bookmark here", "script-message lumina-bookmark", new[] { "y" }),
        new("hideui", "Hide or show the controls", "script-message lumina-do hide-ui", new[] { "h" }),
        new("stats", "Playback statistics", "script-binding stats/display-stats-toggle", new[] { "i" }),
        new("quit", "Close the video", "quit", new[] { "q" }),
    };

    static readonly Dictionary<string, ShortcutAction> ById = Actions.ToDictionary(a => a.Id);

    public static ShortcutAction? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>Case matters for single letters (m and M are different keys); everything else is compared ignoring case.</summary>
    static string Norm(string key) => key.Length == 1 ? key : key.ToUpperInvariant();

    /// <summary>A key mpv could bind: one character, a known name, a function key, or Ctrl+ / Shift+ followed by one of those.</summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains(' ') && key != " ") return false;
        var k = key!;
        while (k.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase) || k.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase)) k = k[(k.IndexOf('+') + 1)..];
        if (k.Length == 1) return !char.IsControl(k[0]);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SPACE", "SHARP", "LEFT", "RIGHT", "UP", "DOWN", "ENTER", "ESC", "BS", "DEL", "INS", "HOME", "END", "PGUP", "PGDWN", "TAB",
        };
        return names.Contains(k) || (k.Length is 2 or 3 && (k[0] is 'F' or 'f') && int.TryParse(k[1..], out var n) && n is >= 1 and <= 24);
    }

    // ---- overrides: action id -> key (empty value never stored) ----

    public static Dictionary<string, string> ParseOverrides(string? json)
    {
        var d = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(json)) return d;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return d;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (ById.ContainsKey(p.Name) && p.Value.ValueKind == JsonValueKind.String && IsValidKey(p.Value.GetString())) d[p.Name] = p.Value.GetString()!;
        }
        catch (JsonException) { }
        return d;
    }

    static readonly JsonSerializerOptions Plain = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };   // keep "+" readable in Ctrl+s

    public static string SerializeOverrides(IReadOnlyDictionary<string, string> o) => o.Count == 0 ? "" : JsonSerializer.Serialize(o, Plain);

    /// <summary>The keys in force for one action.</summary>
    public static string[] KeysFor(ShortcutAction a, IReadOnlyDictionary<string, string> overrides) =>
        overrides.TryGetValue(a.Id, out var k) ? new[] { k } : a.DefaultKeys;

    /// <summary>Every action that currently uses this key, other than <paramref name="except"/>.</summary>
    public static List<ShortcutAction> UsersOf(string key, IReadOnlyDictionary<string, string> overrides, string? except = null) =>
        Actions.Where(a => a.Id != except && KeysFor(a, overrides).Any(k => Norm(k) == Norm(key))).ToList();

    /// <summary>
    /// Give an action a key. If another action holds it, that action is returned in <paramref name="displaced"/> and gets this action's
    /// old key (a swap), so nothing ends up with no key by accident. Returns the new overrides.
    /// </summary>
    public static Dictionary<string, string> Assign(IReadOnlyDictionary<string, string> current, string actionId, string key, out ShortcutAction? displaced)
    {
        displaced = null;
        var result = new Dictionary<string, string>(current);
        var me = ById[actionId];
        var holder = UsersOf(key, current, except: actionId).FirstOrDefault();
        if (holder != null)
        {
            displaced = holder;
            var myOld = KeysFor(me, current)[0];
            result[holder.Id] = myOld;
            if (holder.DefaultKeys.Length == 1 && Norm(holder.DefaultKeys[0]) == Norm(myOld)) result.Remove(holder.Id);   // swapped back to its own default
        }
        result[actionId] = key;
        if (me.DefaultKeys.Length == 1 && Norm(me.DefaultKeys[0]) == Norm(key)) result.Remove(actionId);
        return result;
    }

    /// <summary>
    /// Put an action back on its standard key(s). If another action has taken one of those keys, it gets the key this action was using,
    /// so a reset can never leave two actions on one key.
    /// </summary>
    public static Dictionary<string, string> Reset(IReadOnlyDictionary<string, string> current, string actionId)
    {
        var result = new Dictionary<string, string>(current);
        var me = ById[actionId];
        if (!current.TryGetValue(actionId, out var mine)) return result;
        result.Remove(actionId);
        foreach (var def in me.DefaultKeys)
        {
            var holder = UsersOf(def, result, except: actionId).FirstOrDefault();
            if (holder == null) continue;
            result[holder.Id] = mine;                                                    // the holder moves to the key being vacated
            if (holder.DefaultKeys.Length == 1 && Norm(holder.DefaultKeys[0]) == Norm(mine)) result.Remove(holder.Id);
        }
        return result;
    }

    /// <summary>Clashes inside a set of overrides (two actions on one key). Empty when the set is safe to apply.</summary>
    public static List<string> Conflicts(IReadOnlyDictionary<string, string> overrides)
    {
        var seen = new Dictionary<string, string>();
        var bad = new List<string>();
        foreach (var a in Actions)
            foreach (var k in KeysFor(a, overrides))
            {
                if (seen.TryGetValue(Norm(k), out var other) && other != a.Id) bad.Add($"{k}: {ById[other].Label} and {a.Label}");
                else seen[Norm(k)] = a.Id;
            }
        return bad;
    }

    /// <summary>
    /// mpv input lines for the shipped defaults plus the changes: each default key an override took away is unbound, then the new key is bound.
    /// Only actions with an override are written, so everything else keeps mpv's and the shipped input.conf's behaviour.
    /// </summary>
    public static string Generate(IReadOnlyDictionary<string, string> overrides)
    {
        var sb = new StringBuilder("# generated by Lumina from Settings, Controls. Do not edit.\n");
        foreach (var a in Actions.Where(a => overrides.ContainsKey(a.Id)))
        {
            foreach (var old in a.DefaultKeys)
                if (!overrides.Values.Any(v => Norm(v) == Norm(old))) sb.Append(old).Append(" ignore\n");   // freed, unless another action took it over
            // "repeatable" lets holding the key repeat seeks and volume like the original keys do
            sb.Append(overrides[a.Id]).Append(a.Command.StartsWith("add ") || a.Command.Contains("lumina-skip") ? " repeatable " : " ").Append(a.Command).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Text for the key chip: SPACE reads as Space, LEFT as Left, single letters as typed.</summary>
    public static string Display(string key)
    {
        var parts = key.Split('+');
        var last = parts[^1];
        parts[^1] = last.ToUpperInvariant() switch
        {
            "SHARP" => "#", "BS" => "Backspace", "PGUP" => "Page Up", "PGDWN" => "Page Down", "INS" => "Insert", "DEL" => "Delete", "ESC" => "Esc",
            var u when u.Length > 1 && u[0] == 'F' && int.TryParse(u[1..], out _) => u,                          // F11
            _ when last.Length > 1 => char.ToUpperInvariant(last[0]) + last[1..].ToLowerInvariant(),             // Space, Left
            _ => last,                                                                                           // a single letter stays as typed
        };
        return string.Join("+", parts);
    }
}
