namespace Lumina.Player;

/// <summary>Which optional buttons the on-screen controller shows on its right side, and in what order (left to right).</summary>
public static class ControllerLayout
{
    public static readonly IReadOnlyList<(string Id, string Label)> Known = new[]
    {
        ("cc", "Subtitles"), ("audio", "Audio tracks"), ("speed", "Speed"), ("queue", "Queue"), ("chap", "Chapters"), ("snap", "Snapshot"), ("gear", "Player settings"),
    };

    public static readonly IReadOnlyList<string> Default = Known.Select(k => k.Id).ToList();

    /// <summary>Unknown names and repeats are dropped, the gear is always kept (it is the way to Settings), nothing usable means the default.</summary>
    public static List<string> Parse(string? text)
    {
        var ids = Known.Select(k => k.Id).ToHashSet();
        var seen = new HashSet<string>();
        var list = (text ?? "").Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => ids.Contains(n) && seen.Add(n)).ToList();
        if (list.Count == 0) return Default.ToList();
        if (!seen.Contains("gear")) list.Add("gear");
        return list;
    }

    public static string Format(IEnumerable<string> ids) => string.Join("+", ids);

    public static bool IsDefault(string? text) => Parse(text).SequenceEqual(Default);
}
