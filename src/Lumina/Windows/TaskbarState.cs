using System.Text.Json;

namespace Lumina.Windows;

public enum ProgressKind { None, Normal, Paused, Indeterminate }

public readonly record struct TaskbarState(ProgressKind Kind, double Value)
{
    /// <summary>What the Windows taskbar button should show for the player's current numbers.</summary>
    public static TaskbarState From(bool playing, double position, double duration, bool paused, bool buffering)
    {
        if (!playing) return new(ProgressKind.None, 0);
        if (duration <= 0 || double.IsNaN(duration) || buffering) return new(ProgressKind.Indeterminate, 0);
        var v = Math.Clamp(position / duration, 0, 1);
        if (double.IsNaN(v)) v = 0;
        return new(paused ? ProgressKind.Paused : ProgressKind.Normal, v);
    }

    public override string ToString() => $"{Kind}:{Value:0.###}";
}

/// <summary>Short list of recently played files for the taskbar jump list. Pure list handling; storage is the caller's.</summary>
public static class RecentFiles
{
    public const int Max = 10;

    public static List<string> Add(IEnumerable<string> current, string path, int max = Max)
    {
        var list = new List<string> { path };
        list.AddRange(current.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
        return list.Take(max).ToList();
    }

    /// <summary>Drop what is gone from disk; streams are kept (there is nothing to check).</summary>
    public static List<string> Prune(IEnumerable<string> list, Func<string, bool> exists) =>
        list.Where(p => p.Contains("://") || exists(p)).ToList();

    public static string Serialize(IEnumerable<string> list) => JsonSerializer.Serialize(list);

    public static List<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json)?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new(); }
        catch (JsonException) { return new(); }
    }
}
