using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lumina.Metadata;

/// <summary>Reads and writes %APPDATA%/Lumina/config.json. API keys live there, never in the repo.</summary>
public static partial class Config
{
    static string FilePath => Environment.GetEnvironmentVariable("LUMINA_CONFIG") ?? Path.Combine(
        Lumina.Windows.AppPaths.Data, "config.json");

    // The whole file is parsed once and reused until it changes on disk, because settings are read dozens of times per screen.
    static readonly object Gate = new();
    static (string Path, DateTime Written, long Length)? _stamp;
    static Dictionary<string, string> _cache = new();

    static Dictionary<string, string> Snapshot()
    {
        lock (Gate)
        {
            try
            {
                var path = FilePath;
                var fi = new FileInfo(path);
                var stamp = (path, fi.Exists ? fi.LastWriteTimeUtc : default, fi.Exists ? fi.Length : -1L);
                if (_stamp == stamp) return _cache;
                var d = new Dictionary<string, string>();
                if (fi.Exists)
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.String) d[prop.Name] = prop.Value.GetString() ?? "";
                }
                _cache = d;
                _stamp = stamp;
                return d;
            }
            catch (Exception e) when (e is IOException or JsonException) { return new Dictionary<string, string>(); }
        }
    }

    static string? Get(string name) => Snapshot().TryGetValue(name, out var v) ? v : null;

    // Keys shipped inside the program (see secrets.local.json and scripts/package.ps1): used when the user has not entered their own.
    // Not used when LUMINA_CONFIG points at a scratch config, so tests and the harness see "no key" exactly as before.
    static readonly Lazy<Dictionary<string, string>> Baked = new(() =>
    {
        var d = new Dictionary<string, string>();
        try
        {
            if (Environment.GetEnvironmentVariable("LUMINA_CONFIG") != null) return d;
            using var s = typeof(Config).Assembly.GetManifestResourceStream("Lumina.baked-keys.json");
            if (s == null) return d;
            using var doc = JsonDocument.Parse(s);
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() is { Length: > 0 } v) d[prop.Name] = v.Trim();
        }
        catch (Exception e) when (e is JsonException or IOException) { }
        return d;
    });

    /// <summary>The user's own value if they set one, otherwise the key shipped with the program.</summary>
    static string? KeyOrBaked(string name) => Get(name)?.Trim() is { Length: > 0 } own ? own : Baked.Value.GetValueOrDefault(name);

    public static string? OmdbKey => KeyOrBaked("omdbKey");

    /// <summary>Folder that holds config.json; generated files (custom key bindings) sit beside it.</summary>
    public static string Folder => Path.GetDirectoryName(FilePath)!;

    /// <summary>Value for the Settings page; empty when unset.</summary>
    public static string Raw(string name) => Get(name) ?? "";

    /// <summary>
    /// Set the given keys, keeping every other key in the file. An empty value removes the key.
    /// A file that is not valid JSON is moved to config.json.bak first so nothing is lost.
    /// </summary>
    public static void Save(IReadOnlyDictionary<string, string> values)
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root = new();
        if (File.Exists(path))
        {
            try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new(); }
            catch (JsonException) { File.Copy(path, path + ".bak", true); }
        }
        foreach (var (k, v) in values)
        {
            if (v.Length == 0) root.Remove(k);
            else root[k] = v;
        }
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, true);
        lock (Gate) _stamp = null;
    }

    /// <summary>TMDB v3 API key (32 chars) or v4 read token (long JWT); both accepted.</summary>
    public static string? TmdbKey => KeyOrBaked("tmdbKey");

    /// <summary>Trakt API app credentials from trakt.tv/oauth/applications (device sign-in needs both).</summary>
    public static string? TraktClientId => KeyOrBaked("traktClientId");

    public static string? TraktClientSecret => KeyOrBaked("traktClientSecret");

    public static string? OpenSubtitlesKey => KeyOrBaked("openSubtitlesKey");

    /// <summary>Comma separated language codes in order of preference, e.g. "en,es". Default en.</summary>
    public static string SubtitleLanguages => Get("subtitleLanguages") is { Length: > 0 } l ? l : "en";

    /// <summary>Extra mpv options, one key=value per line, applied after the shipped mpv.conf.</summary>
    public static string MpvOptions => Get("mpvOptions") ?? "";

    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z]{2,4})?$")]
    private static partial Regex LangCode();

    /// <summary>Lowercase, trim and de-duplicate a language list. Null if any entry is not a language code.</summary>
    public static string? NormalizeLanguages(string input)
    {
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant()).ToList();
        if (parts.Count == 0 || parts.Any(p => !LangCode().IsMatch(p))) return null;
        return string.Join(",", parts.Distinct());
    }
}
