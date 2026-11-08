using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;

namespace Lumina.Settings;

/// <summary>
/// Named bundles of playback settings (picture, sound, subtitles, playback, performance). Applying one sets every setting
/// in that scope, so a profile means the same thing whatever was set before, and remembers what it replaced so Undo is exact.
/// Saved profiles live in the database (kv), never in config.json.
/// </summary>
public static class Profiles
{
    static readonly string[] ScopeCategories = { "video", "audio", "subtitles", "playback", "performance" };
    static readonly HashSet<string> Personal = new() { "audio.device", "audio.fixedVolume", "audio.startVolume" };

    /// <summary>The settings a profile can hold.</summary>
    public static readonly IReadOnlyList<SettingDef> Scope = Catalog.All
        .Where(d => ScopeCategories.Contains(d.Category) && d.Kind is not (Kind.Button or Kind.Secret) && !Personal.Contains(d.Key))
        .ToList();

    static readonly Dictionary<string, Dictionary<string, string>> BuiltInSets = new()
    {
        ["Movies"] = new() { ["audio.eqPreset"] = "off", ["audio.spatial"] = "off", ["video.smooth"] = "false", ["subtitles.size"] = "46" },
        ["Anime"] = new() { ["subtitles.size"] = "52", ["subtitles.bold"] = "true", ["subtitles.outline"] = "3", ["playback.alang"] = "ja", ["video.smooth"] = "false" },
        ["Night"] = new() { ["audio.night"] = "true", ["audio.normalize"] = "true", ["audio.compressor"] = "strong", ["video.brightness"] = "-10" },
    };

    public static IReadOnlyList<string> BuiltIn => BuiltInSets.Keys.ToList();

    // ---------- saved ----------

    static Dictionary<string, Dictionary<string, string>> LoadSaved()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(Db.KvGet("profiles") ?? "{}") ?? new(); }
        catch (JsonException) { return new(); }
    }

    static void StoreSaved(Dictionary<string, Dictionary<string, string>> all) => Db.KvSet("profiles", JsonSerializer.Serialize(all));

    public static IReadOnlyList<string> Saved => LoadSaved().Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<string> All => BuiltIn.Concat(Saved).ToList();

    /// <summary>The scope's current non-default values.</summary>
    public static Dictionary<string, string> Capture() =>
        Scope.Where(d => AppSettings.Get(d.Key) != d.Default).ToDictionary(d => d.Key, d => AppSettings.Get(d.Key));

    /// <summary>Saves the current settings under a name. Built-in names are taken; saving over a saved one replaces it.</summary>
    public static void Save(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || BuiltInSets.ContainsKey(name)) throw new ArgumentException("That name is taken or empty.");
        var all = LoadSaved();
        all[name] = Capture();
        StoreSaved(all);
    }

    public static void Delete(string name)
    {
        var all = LoadSaved();
        if (all.Remove(name)) StoreSaved(all);
    }

    public static Dictionary<string, string>? Values(string name) =>
        BuiltInSets.TryGetValue(name, out var b) ? b : LoadSaved().TryGetValue(name, out var s) ? s : null;

    // ---------- apply / undo ----------

    /// <summary>Sets the whole scope: the profile's value where it has one, the default elsewhere. Returns the keys that changed.</summary>
    public static IReadOnlyList<string> Apply(string name)
    {
        var values = Values(name) ?? throw new KeyNotFoundException(name);
        var before = Scope.ToDictionary(d => d.Key, d => Config.Raw(d.Key));
        Db.KvSet("profile_undo", JsonSerializer.Serialize(new { name, before }));
        var after = Scope.ToDictionary(d => d.Key, d => values.TryGetValue(d.Key, out var v) ? v : d.Default);
        AppSettings.Set(after);
        return Scope.Where(d => AppSettings.Get(d.Key) != Normalized(d, before[d.Key])).Select(d => d.Key).ToList();
    }

    static string Normalized(SettingDef d, string raw) => raw.Length == 0 ? d.Default : AppSettings.Normalize(d, raw);

    /// <summary>Name of the profile the last Apply replaced, or null when there is nothing to undo.</summary>
    public static string? UndoName()
    {
        try { return JsonDocument.Parse(Db.KvGet("profile_undo") ?? "null").RootElement.GetProperty("name").GetString(); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>Puts every setting back exactly as it was before the last Apply (unset stays unset).</summary>
    public static bool Undo()
    {
        if (UndoName() is null) return false;
        var before = JsonDocument.Parse(Db.KvGet("profile_undo")!).RootElement.GetProperty("before").Deserialize<Dictionary<string, string>>()!;
        Config.Save(before);
        Db.KvDelete("profile_undo");
        return true;
    }

    // ---------- share ----------

    public static string Export(string name)
    {
        var v = Values(name) ?? throw new KeyNotFoundException(name);
        return JsonSerializer.Serialize(new { lumina_profile = 1, name, values = v }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Reads an exported profile and saves it. Unknown or out-of-scope keys are dropped, values are cleaned the way the Settings page cleans them.</summary>
    public static string Import(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("lumina_profile", out _) || !root.TryGetProperty("values", out var vs) || vs.ValueKind != JsonValueKind.Object)
                throw new FormatException("This is not a Lumina profile.");
            var name = (root.TryGetProperty("name", out var n) ? n.GetString() : null)?.Trim();
            if (string.IsNullOrEmpty(name)) throw new FormatException("The profile has no name.");
            var clean = new Dictionary<string, string>();
            foreach (var p in vs.EnumerateObject())
                if (Scope.FirstOrDefault(d => d.Key == p.Name) is { } d && p.Value.ValueKind == JsonValueKind.String)
                    clean[d.Key] = AppSettings.Normalize(d, p.Value.GetString() ?? "");
            while (BuiltInSets.ContainsKey(name)) name += " (imported)";
            var all = LoadSaved();
            all[name] = clean;
            StoreSaved(all);
            return name;
        }
        catch (JsonException) { throw new FormatException("This is not a Lumina profile."); }
    }
}
