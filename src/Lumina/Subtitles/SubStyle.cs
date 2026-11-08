using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumina.Library;

namespace Lumina.Subtitles;

/// <summary>How the subtitles look for one video: how late they are, how big, and how high on the picture (0 top, 100 bottom).</summary>
public record SubStyle(double Delay, double Scale, double Position)
{
    public const double MinScale = 0.3, MaxScale = 3, MaxDelay = 600;

    /// <summary>Values made safe: rounded to what the controls can produce, and kept inside what the player accepts.</summary>
    public static SubStyle Clamped(double delay, double scale, double position)
    {
        static double Safe(double v, double fallback) => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
        return new SubStyle(
            Math.Round(Math.Clamp(Safe(delay, 0), -MaxDelay, MaxDelay), 3),
            Math.Round(Math.Clamp(Safe(scale, 1), MinScale, MaxScale), 3),
            Math.Round(Math.Clamp(Safe(position, 100), 0, 100)));
    }

    /// <summary>True when it equals the player's normal look, so there is nothing worth remembering for this video.</summary>
    public bool IsDefault(double defaultPosition) =>
        Math.Abs(Delay) < 0.0005 && Math.Abs(Scale - 1) < 0.0005 && Math.Abs(Position - defaultPosition) < 0.5;

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, double> { ["delay"] = Delay, ["scale"] = Scale, ["pos"] = Position });

    /// <summary>Null for anything that is not a stored style.</summary>
    public static SubStyle? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
            if (d is null || !d.TryGetValue("delay", out var delay) || !d.TryGetValue("scale", out var scale) || !d.TryGetValue("pos", out var pos)) return null;
            return Clamped(delay, scale, pos);
        }
        catch (JsonException) { return null; }
    }

    public static SubStyle? Parse(string? delay, string? scale, string? position) =>
        double.TryParse(delay, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.TryParse(scale, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
        && double.TryParse(position, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? Clamped(d, s, p) : null;
}

/// <summary>Remembers a video's subtitle look in the database, so it comes back the next time that video plays.</summary>
public static class SubStyleStore
{
    static string Key(string path) =>
        "substyle:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(LockedPaths.Normalize(path))))[..16];

    /// <summary>Saves it, or forgets the video's entry when it is back to the normal look.</summary>
    public static void Save(string path, SubStyle style, double defaultPosition)
    {
        if (style.IsDefault(defaultPosition)) Db.KvDelete(Key(path));
        else Db.KvSet(Key(path), style.ToJson());
    }

    public static SubStyle? Load(string path) => SubStyle.FromJson(Db.KvGet(Key(path)));
}
