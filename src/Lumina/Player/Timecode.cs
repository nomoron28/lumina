using System.Globalization;

namespace Lumina.Player;

public static class Timecode
{
    /// <summary>
    /// "90", "1:30", "01:02:03", "1:02:03.5", "1h2m3s", "90s" to seconds. Null for anything else or a negative result.
    /// </summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().ToLowerInvariant();
        var inv = CultureInfo.InvariantCulture;
        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length is < 2 or > 3) return null;
            double total = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var last = i == parts.Length - 1;
                if (!double.TryParse(parts[i], NumberStyles.Float, inv, out var v) || v < 0) return null;
                if (!last && v != Math.Floor(v)) return null;               // only seconds may be fractional
                if (i > 0 && v >= 60) return null;                          // 1:75 is a typo, not 2:15
                total = total * 60 + v;
            }
            return total;
        }
        if (text.Any(c => c is 'h' or 'm' or 's'))
        {
            double total = 0;
            var any = false;
            var num = "";
            foreach (var c in text)
            {
                if (char.IsDigit(c) || c == '.') { num += c; continue; }
                if (c is not ('h' or 'm' or 's') || !double.TryParse(num, NumberStyles.Float, inv, out var cur)) return null;
                total += c == 'h' ? cur * 3600 : c == 'm' ? cur * 60 : cur;
                num = "";
                any = true;
            }
            return any && num.Length == 0 ? total : null;   // "5m3" has a number with no unit
        }
        return double.TryParse(text, NumberStyles.Float, inv, out var secs) && secs >= 0 ? secs : null;
    }

    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
