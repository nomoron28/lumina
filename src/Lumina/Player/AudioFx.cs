using System.Globalization;
using Lumina.Settings;

namespace Lumina.Player;

/// <summary>Everything the audio effects need, read from settings. Kept as plain data so composing the filter chain is pure.</summary>
public sealed record AudioFxSettings(
    IReadOnlyList<double> Bands, bool EqOn, string Compressor, string Spatial, double StereoWidth, bool Normalize, bool Night)
{
    public static AudioFxSettings FromSettings()
    {
        var bands = Enumerable.Range(1, AudioFx.BandHz.Length).Select(i => AppSettings.Num("audio.eq" + i)).ToArray();
        return new AudioFxSettings(bands, AppSettings.Get("audio.eqPreset") != "off", AppSettings.Get("audio.compressor"),
            AppSettings.Get("audio.spatial"), AppSettings.Num("audio.stereoWidth"), AppSettings.Bool("audio.normalize"), AppSettings.Bool("audio.night"));
    }
}

/// <summary>Turns the audio effect settings into one mpv audio filter chain (an ffmpeg filter graph), or nothing when all are off.</summary>
public static class AudioFx
{
    public static readonly double[] BandHz = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    /// <summary>Gains in dB for the ten bands above. The names are what the Equalizer dropdown and the player panel show.</summary>
    public static readonly IReadOnlyDictionary<string, double[]> Presets = new Dictionary<string, double[]>
    {
        ["bass"] = new double[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
        ["treble"] = new double[] { 0, 0, 0, 0, 0, 1, 2, 4, 5, 6 },
        ["vocal"] = new double[] { -2, -2, -1, 1, 3, 4, 3, 1, 0, -1 },
        ["rock"] = new double[] { 5, 4, 2, -1, -2, -1, 2, 4, 5, 5 },
        ["pop"] = new double[] { -1, 1, 3, 4, 3, 0, -1, -1, 1, 2 },
        ["jazz"] = new double[] { 3, 2, 1, 2, -2, -2, 0, 1, 2, 3 },
        ["classical"] = new double[] { 4, 3, 2, 1, -1, -1, 0, 2, 3, 4 },
        ["dance"] = new double[] { 6, 5, 2, 0, 0, -2, -3, -2, 3, 4 },
        ["movie"] = new double[] { 4, 3, 1, 0, 0, 1, 2, 2, 1, 0 },
    };

    public static readonly Choice[] PresetChoices =
    {
        new("off", "Off"), new("bass", "Bass boost"), new("treble", "Treble boost"), new("vocal", "Vocal"), new("rock", "Rock"), new("pop", "Pop"),
        new("jazz", "Jazz"), new("classical", "Classical"), new("dance", "Dance"), new("movie", "Movie"), new("custom", "Custom"),
    };

    public static readonly Choice[] CompressorChoices = { new("off", "Off"), new("gentle", "Gentle"), new("medium", "Medium"), new("strong", "Strong") };
    public static readonly Choice[] SpatialChoices = { new("off", "Off"), new("headphones", "Headphones"), new("room", "Room"), new("hall", "Hall") };

    static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The filter graph text, e.g. <c>volume=-6dB,equalizer=f=31:t=o:w=1:g=6</c>, or null when nothing is switched on.</summary>
    public static string? Graph(AudioFxSettings s)
    {
        var f = new List<string>();

        if (s.EqOn)
        {
            var gains = BandHz.Select((_, i) => i < s.Bands.Count ? Math.Clamp(s.Bands[i], -12, 12) : 0).ToArray();
            if (gains.Any(g => g != 0))
            {
                // Boosting raises the peak level; trim by the biggest boost first so the result does not clip.
                var headroom = Math.Max(0, gains.Max());
                if (headroom > 0) f.Add($"volume=-{N(headroom)}dB");
                for (var i = 0; i < gains.Length; i++)
                    if (gains[i] != 0) f.Add($"equalizer=f={N(BandHz[i])}:t=o:w=1:g={N(gains[i])}");
            }
        }

        switch (s.Compressor)
        {
            case "gentle": f.Add("acompressor=threshold=0.1:ratio=2:attack=20:release=300:makeup=1.4"); break;
            case "medium": f.Add("acompressor=threshold=0.063:ratio=4:attack=15:release=250:makeup=2"); break;
            case "strong": f.Add("acompressor=threshold=0.032:ratio=8:attack=10:release=200:makeup=3"); break;
        }

        switch (s.Spatial)
        {
            case "headphones": f.Add("crossfeed=strength=0.5"); break;
            case "room": f.Add("aecho=0.8:0.88:60:0.35"); break;
            case "hall": f.Add("aecho=0.8:0.9:1000|1800:0.3|0.25"); break;
        }

        var width = Math.Clamp(s.StereoWidth, 0, 100);
        if (width > 0) f.Add($"extrastereo=m={N(1 + width / 100 * 3)}");

        if (s.Normalize) f.Add("dynaudnorm=f=150:g=15");
        if (s.Night) f.Add("acompressor=threshold=0.063:ratio=6:attack=20:release=250");

        return f.Count == 0 ? null : string.Join(",", f);
    }

    /// <summary>The mpv value for <c>af</c>: the graph wrapped in the lavfi bridge, or empty to clear the chain.</summary>
    public static string Compose(AudioFxSettings s) => Graph(s) is { } g ? $"lavfi=[{g}]" : "";

    public static string Next(Choice[] choices, string current)
    {
        var i = Array.FindIndex(choices, c => c.Value == current);
        return choices[(i + 1) % choices.Length].Value;
    }
}
