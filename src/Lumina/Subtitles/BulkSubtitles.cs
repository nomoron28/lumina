using System.IO;
using System.Net.Http;

namespace Lumina.Subtitles;

/// <summary>One video to find subtitles for.</summary>
public record BulkItem(string Path, string Title, int? Year, int? Season, int? Episode);

/// <summary>What a bulk run did. Stopped says why it ended early (daily limit, bad key, no connection), or is null when it went through the list.</summary>
public record BulkResult(int Found, int AlreadyHad, int NotFound, int Failed, string? Stopped, int Total)
{
    public int Done => Found + AlreadyHad + NotFound + Failed;

    /// <summary>"9 found, 2 already had them, 1 not found" and, if it stopped early, why.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Found > 0) parts.Add($"{Found} found");
            if (AlreadyHad > 0) parts.Add($"{AlreadyHad} already had them");
            if (NotFound > 0) parts.Add($"{NotFound} not found");
            if (Failed > 0) parts.Add($"{Failed} failed");
            var text = parts.Count == 0 ? "Nothing to do" : string.Join(", ", parts);
            return Stopped != null ? $"{text}. Stopped: {Stopped}" : text;
        }
    }
}

/// <summary>
/// Finds subtitles for many videos in a row, quietly: videos that already have some are skipped, and it stops when carrying on would be pointless
/// (the daily download limit, a rejected key, no key, or several failures in a row). What it does for each video is passed in, so the rules are testable.
/// </summary>
public static class BulkSubtitles
{
    public static async Task<BulkResult> RunAsync(IReadOnlyList<BulkItem> items, Func<BulkItem, Task<SubResult>> fetch, Func<string, bool> alreadyHas,
        Action<int, int, string>? progress = null, CancellationToken cancel = default, TimeSpan? pause = null, int maxFailuresInARow = 3, Func<TimeSpan, Task>? delay = null)
    {
        int found = 0, had = 0, missing = 0, failed = 0, streak = 0;
        string? stopped = null;
        for (var i = 0; i < items.Count; i++)
        {
            if (cancel.IsCancellationRequested) { stopped = "cancelled"; break; }
            var item = items[i];
            progress?.Invoke(i + 1, items.Count, item.Title);
            if (alreadyHas(item.Path)) { had++; continue; }
            SubResult r;
            try { r = await fetch(item); }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException) { r = new SubResult(null, "unreachable: " + e.Message); }
            if (r.Path != null) { found++; streak = 0; }
            else if (IsFatal(r.Message)) { stopped = r.Message; break; }
            else if (r.Message.StartsWith("No subtitles found", StringComparison.Ordinal)) { missing++; streak = 0; }
            else { failed++; if (++streak >= maxFailuresInARow) { stopped = r.Message; break; } }
            if (pause is { } p && p > TimeSpan.Zero && i < items.Count - 1) await (delay ?? (t => Task.Delay(t)))(p);   // the service allows a few requests a second
        }
        return new BulkResult(found, had, missing, failed, stopped, items.Count);
    }

    /// <summary>Messages after which every further request would fail the same way.</summary>
    public static bool IsFatal(string message) =>
        message.Contains("daily download limit", StringComparison.OrdinalIgnoreCase)
        || message.Contains("rejected the API key", StringComparison.OrdinalIgnoreCase)
        || message.StartsWith("Add an OpenSubtitles key", StringComparison.Ordinal);
}
