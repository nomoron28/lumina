using System.Globalization;
using System.IO;
using System.Text.Json;
using Lumina.Player;

namespace Lumina.Library;

/// <summary>
/// Reads a file's codec, size and frame rate with a short-lived headless mpv, so the details page can show them without playing the file.
/// The answer is cached by path, size and modified time, so a file is opened at most once until it changes.
/// </summary>
public static class FileProbe
{
    static readonly SemaphoreSlim One = new(1, 1);   // one probe at a time: opening several big files together would hurt playback

    static string Key(FileInfo f) => $"spec:{f.FullName}|{f.Length}|{f.LastWriteTimeUtc.Ticks}";

    public static FileSpec? Cached(string path)
    {
        try
        {
            var f = new FileInfo(path);
            if (!f.Exists) return null;
            return Db.KvGet(Key(f)) is { } json ? JsonSerializer.Deserialize<FileSpec>(json) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or Microsoft.Data.Sqlite.SqliteException) { return null; }
    }

    /// <summary>Cached answer, or a fresh probe. Null for a file that is not on disk or cannot be opened. Never throws.</summary>
    public static async Task<FileSpec?> GetAsync(string path, CancellationToken cancel = default)
    {
        if (Cached(path) is { } hit) return hit;
        if (!File.Exists(path)) return null;
        await One.WaitAsync(cancel);
        try
        {
            if (Cached(path) is { } again) return again;
            var spec = await Task.Run(() => Read(path, TimeSpan.FromSeconds(12)), cancel);
            if (spec != null)
                try { var f = new FileInfo(path); Db.KvSet(Key(f), JsonSerializer.Serialize(spec)); } catch (Exception e) when (e is IOException or Microsoft.Data.Sqlite.SqliteException) { }
            return spec;
        }
        catch (OperationCanceledException) { return null; }
        finally { One.Release(); }
    }

    /// <summary>Open the file headless, wait for it to load, read the streams, close. Null if mpv cannot open it in time.</summary>
    public static FileSpec? Read(string path, TimeSpan timeout)
    {
        Mpv? m = null;
        try
        {
            var empty = Path.Combine(Path.GetTempPath(), "lumina-probe");   // no mpv.conf and no controller script in here, so nothing else starts
            Directory.CreateDirectory(empty);
            m = new Mpv(IntPtr.Zero, empty, "vo=null\nao=null\npause=yes\naudio-display=no\nsub-auto=no");
            using var loaded = new ManualResetEventSlim();
            m.FileLoaded += () => loaded.Set();
            m.Load(path);
            if (!loaded.Wait(timeout)) return null;
            return FromProperties(m.Get, new FileInfo(path).Length);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ObjectDisposedException or DllNotFoundException) { return null; }
        finally { m?.Dispose(); }
    }

    static double? D(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    static int? I(string? s) => D(s) is { } d ? (int)d : null;

    /// <summary>Pure: builds the spec from mpv properties (the first video and first audio track).</summary>
    public static FileSpec FromProperties(Func<string, string?> get, long size)
    {
        var count = I(get("track-list/count")) ?? 0;
        string? video = null, audio = null; int? w = null, h = null, ch = null; double? fps = null;
        for (var i = 0; i < count; i++)
        {
            string? T(string k) => get($"track-list/{i}/{k}");
            var type = T("type");
            if (type == "video" && video == null && T("albumart") != "yes") { video = T("codec"); w = I(T("demux-w")); h = I(T("demux-h")); fps = D(T("demux-fps")); }
            else if (type == "audio" && audio == null) { audio = T("codec"); ch = I(T("demux-channel-count")); }
        }
        var dur = D(get("duration"));
        double? mbps = dur is > 0 && size > 0 ? size * 8.0 / dur / 1_000_000 : null;
        return new FileSpec(size, video, w, h, fps, audio, ch, mbps);
    }
}
