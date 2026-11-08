using System.IO;

namespace Lumina.Player;

/// <summary>
/// Saves a network stream to a file while it plays (mpv's stream-record). It only makes sense for a stream: a file on disk is already a file.
/// The checks around it are here so they can be tested: a place to put it, enough room, and a file name that is safe on Windows.
/// </summary>
public static class Recorder
{
    public const long MinFreeBytes = 500L * 1024 * 1024;

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Lumina recordings");

    public static string FolderFor(string? setting) => string.IsNullOrWhiteSpace(setting) ? DefaultFolder : setting.Trim();

    /// <summary>The title made safe as a file name: no characters Windows refuses, no dots or spaces at the end, not absurdly long, never empty.</summary>
    public static string SafeName(string? title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string((title ?? "").Select(c => bad.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        s = string.Join(" ", s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.Length > 80) s = s[..80].TrimEnd();
        s = s.TrimEnd('.', ' ');
        if (s.Length == 0) return "Recording";
        // device names such as CON or NUL cannot be file names at all
        var stem = s.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) s = "Recording " + s;
        return s;
    }

    public static string FileNameFor(string? title, DateTime now, string ext = "mkv") =>
        $"{SafeName(title)} {now:yyyy-MM-dd HH-mm-ss}.{ext.TrimStart('.')}";

    public static bool HasRoom(long freeBytes, long minBytes = MinFreeBytes) => freeBytes < 0 || freeBytes >= minBytes;   // unknown space (-1) is not a reason to refuse

    public static long FreeSpace(string folder)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return -1; }
    }

    /// <summary>A name in the folder that is not taken: "x.mkv", then "x (2).mkv", "x (3).mkv".</summary>
    public static string Unique(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)} ({n}){Path.GetExtension(fileName)}");
        return path;
    }

    /// <summary>
    /// Starts recording the playing stream. Returns the file being written, or an explanation in plain words.
    /// <paramref name="source"/> is the address being played; <paramref name="freeSpace"/> can be replaced in tests.
    /// </summary>
    public static (string? Path, string? Error) Start(Mpv m, string source, string folder, string? title, DateTime now, Func<string, long>? freeSpace = null)
    {
        if (!source.Contains("://")) return (null, "Recording is for network streams. This video is already a file on your disk.");
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return (null, "Lumina cannot use the recordings folder: " + e.Message); }
        if (!HasRoom((freeSpace ?? FreeSpace)(folder))) return (null, $"Not enough free space for a recording (Lumina wants at least {MinFreeBytes / (1024 * 1024)} MB free).");
        var path = Unique(folder, FileNameFor(title, now));
        try { m.Set("stream-record", path); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { return (null, "The player would not start recording this stream."); }
        return (path, null);
    }

    /// <summary>Stops and returns the size of the file so far (0 if nothing was written).</summary>
    public static long Stop(Mpv m, string path)
    {
        try { m.Set("stream-record", ""); } catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { }
        // mpv finishes writing and closes the file a moment after it is told to stop: wait until the size stops changing
        long last = -1;
        for (var i = 0; i < 15; i++)
        {
            long now = 0;
            try { now = File.Exists(path) ? new FileInfo(path).Length : 0; } catch (IOException) { }
            if (now > 0 && now == last) return now;
            last = now;
            Thread.Sleep(100);
        }
        return Math.Max(0, last);
    }
}
