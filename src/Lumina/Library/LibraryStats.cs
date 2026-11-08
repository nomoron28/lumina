using System.Globalization;
using Lumina.Player;

namespace Lumina.Library;

/// <summary>The numbers on the library overview: how many films and episodes, how big, when it was last scanned.</summary>
public record LibraryStats(int Films, int Episodes, long Bytes)
{
    public static LibraryStats From(IEnumerable<MediaItem> items)
    {
        int films = 0, eps = 0; long bytes = 0;
        foreach (var i in items)
        {
            if (i.Kind == "show") eps++; else films++;
            bytes += Math.Max(0, i.Size);
        }
        return new LibraryStats(films, eps, bytes);
    }

    public string SizeText => Bytes <= 0 ? "0 B" : MediaInfo.Size(Bytes);

    /// <summary>"Today, 3:06 PM", "Yesterday, 9:30 AM", "12 Oct 2026", or "Not scanned yet".</summary>
    public static string LastUpdate(long? unixSeconds, DateTime nowLocal)
    {
        if (unixSeconds is not { } s || s <= 0) return "Not scanned yet";
        var t = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;
        var clock = t.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (t.Date == nowLocal.Date) return "Today, " + clock;
        if (t.Date == nowLocal.Date.AddDays(-1)) return "Yesterday, " + clock;
        return t.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
