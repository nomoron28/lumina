using System.Globalization;
using System.IO;

namespace Lumina.Player;

public record InfoRow(string Section, string Label, string Value);

/// <summary>Turns what mpv knows about the playing file into rows for the Media info dialog. Pure: takes a property reader.</summary>
public static class MediaInfo
{
    public static string Size(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return i == 0 ? $"{bytes:0} B" : bytes >= 100 ? $"{bytes:0} {units[i]}" : $"{bytes:0.0} {units[i]}";
    }

    public static string Channels(int n) => n switch
    {
        1 => "Mono", 2 => "Stereo", 6 => "5.1", 8 => "7.1", 3 => "2.1", 4 => "4.0", 5 => "5.0", 7 => "6.1", _ => n + " channels",
    };

    /// <summary>SDR, HDR10, HLG or Dolby Vision-style PQ, from the transfer function.</summary>
    public static string Dynamic(string? gamma) => gamma switch
    {
        "pq" => "HDR10 (PQ)", "hlg" => "HLG", null or "" => "", _ => "SDR",
    };

    static double? D(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static List<InfoRow> Build(Func<string, string?> get)
    {
        var rows = new List<InfoRow>();
        void Add(string section, string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add(new InfoRow(section, label, value!)); }

        var path = get("path") ?? "";
        var isUrl = path.Contains("://");
        Add("File", "Name", isUrl ? path : Path.GetFileName(path));
        if (!isUrl) Add("File", "Folder", Path.GetDirectoryName(path));
        if (D(get("file-size")) is { } size && size > 0) Add("File", "Size", Size(size));
        Add("File", "Container", get("file-format"));
        if (D(get("duration")) is { } dur) Add("File", "Duration", Timecode.Format(dur));
        if (int.TryParse(get("chapter-list/count"), out var ch) && ch > 0) Add("File", "Chapters", ch.ToString());

        // The playing video's decoded parameters
        var w = get("video-params/w"); var h = get("video-params/h");
        if (w != null && h != null) Add("Video", "Resolution", $"{w} x {h}");
        Add("Video", "Codec", get("video-format")?.ToUpperInvariant());
        if (D(get("container-fps")) is { } fps && fps > 0) Add("Video", "Frame rate", $"{fps:0.###} fps");
        Add("Video", "Pixel format", get("video-params/pixelformat"));
        Add("Video", "Dynamic range", Dynamic(get("video-params/gamma")));
        if (get("video-params/primaries") is { Length: > 0 } pr) Add("Video", "Colour primaries", pr);
        if (get("hwdec-current") is { } hw) Add("Video", "Hardware decoding", hw is "" or "no" ? "Off" : hw);

        var count = int.TryParse(get("track-list/count"), out var n) ? n : 0;
        int a = 0, s = 0;
        for (var i = 0; i < count; i++)
        {
            string? T(string k) => get($"track-list/{i}/{k}");
            var type = T("type");
            var lang = T("lang"); var title = T("title"); var codec = T("codec")?.ToUpperInvariant();
            var name = string.Join("  ", new[] { lang?.ToUpperInvariant(), title }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (type == "audio")
            {
                a++;
                var parts = new List<string?> { codec };
                if (int.TryParse(T("demux-channel-count"), out var cc)) parts.Add(Channels(cc));
                if (int.TryParse(T("demux-samplerate"), out var sr)) parts.Add($"{sr / 1000.0:0.#} kHz");
                if (T("selected") == "yes") parts.Add("playing");
                Add("Audio", $"Track {a}" + (name.Length > 0 ? $" ({name})" : ""), string.Join(", ", parts.Where(x => !string.IsNullOrEmpty(x))));
            }
            else if (type == "sub")
            {
                s++;
                var parts = new List<string?> { codec, T("external") == "yes" ? "external file" : null, T("selected") == "yes" ? "showing" : null };
                Add("Subtitles", $"Track {s}" + (name.Length > 0 ? $" ({name})" : ""), string.Join(", ", parts.Where(x => !string.IsNullOrEmpty(x))));
            }
        }
        return rows;
    }
}
