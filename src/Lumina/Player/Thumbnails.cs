using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lumina.Player;

/// <summary>
/// Small preview pictures for the seek bar: one every few seconds, rendered once in the background by a quiet libmpv
/// (no window, no sound), kept in a folder with a size cap. Everything here is pure or touches only that folder.
/// </summary>
public static class Thumbnails
{
    public const int Width = 240;
    public const long CacheLimitBytes = 300L * 1024 * 1024;

    /// <summary>Where the pictures live. LUMINA_THUMBS points tests at a throwaway folder.</summary>
    public static string Root => Environment.GetEnvironmentVariable("LUMINA_THUMBS") ?? Path.Combine(Lumina.Windows.AppPaths.Cache, "thumbs");

    /// <summary>A short, stable folder name for a file as it is now: changes when the file is replaced or edited.</summary>
    public static string FolderFor(string path)
    {
        var f = new FileInfo(path);
        var raw = $"{f.FullName.ToLowerInvariant()}|{f.Length}|{f.LastWriteTimeUtc.Ticks}";
        return Path.Combine(Root, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw)))[..16]);
    }

    /// <summary>How many pictures and how far apart (seconds) for a video this long: one every ~10 s, between 12 and 120 in all.</summary>
    public static (int Count, double Step) Plan(double durationSec)
    {
        if (durationSec <= 1) return (0, 0);
        var count = Math.Clamp((int)Math.Ceiling(durationSec / 10), 12, 120);
        return (count, durationSec / count);
    }

    /// <summary>The time a picture stands for: the middle of its slice, so scrubbing shows what is about to play.</summary>
    public static double TimeOf(int index, double step) => (index + 0.5) * step;

    public static string FramePath(string dir, int index) => Path.Combine(dir, $"{index:0000}.jpg");

    /// <summary>The picture for a time: the one for that slice, or the closest earlier one that exists (pictures arrive in order). Null if none yet.</summary>
    public static string? Nearest(string dir, int count, double step, double timeSec)
    {
        if (count <= 0 || step <= 0) return null;
        var i = Math.Clamp((int)(timeSec / step), 0, count - 1);
        for (; i >= 0; i--)
        {
            var p = FramePath(dir, i);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static string DoneMarker(string dir) => Path.Combine(dir, "done");

    /// <summary>True when every picture for this folder was made.</summary>
    public static bool IsComplete(string dir) => File.Exists(DoneMarker(dir));

    /// <summary>
    /// Renders the pictures. Returns true when all are there (including when they already were), false when cancelled or the file will not open.
    /// <paramref name="report"/> gets the number made so far. Runs on the calling thread: start it on a background thread.
    /// </summary>
    public static bool Generate(string path, double durationSec, CancellationToken cancel, Action<int>? report = null)
    {
        var (count, step) = Plan(durationSec);
        if (count == 0 || !File.Exists(path)) return false;
        var dir = FolderFor(path);
        if (IsComplete(dir)) { Touch(dir); return true; }
        Directory.CreateDirectory(dir);

        var work = Path.Combine(dir, "work");           // mpv writes its numbered file here; each is renamed to its slot
        Directory.CreateDirectory(work);
        var empty = Path.Combine(Path.GetTempPath(), "lumina-probe");
        Directory.CreateDirectory(empty);
        Mpv? m = null;
        try
        {
            m = new Mpv(IntPtr.Zero, empty, string.Join("\n",
                "vo=image", "vo-image-format=jpg", "vo-image-jpeg-quality=72", "vo-image-outdir=" + work,
                "idle=yes", "ao=null", "aid=no", "sid=no", "audio-display=no", "sub-auto=no", "hwdec=no", "hr-seek=no", "pause=no", "terminal=no",
                $"vf=scale={Width}:-2"));
            using var ended = new SemaphoreSlim(0);
            m.EndFile += (_, _) => ended.Release();
            var made = 0;
            for (var i = 0; i < count; i++)
            {
                if (cancel.IsCancellationRequested) return false;
                var target = FramePath(dir, i);
                if (File.Exists(target)) { made++; continue; }
                foreach (var old in Directory.GetFiles(work)) File.Delete(old);
                m.Command("loadfile", path, "replace", "-1", "start=" + TimeOf(i, step).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ",frames=1");
                // wait for this file to end (one frame, or the end of a short file), but never forever
                var waited = 0;
                while (!ended.Wait(250))
                {
                    if (cancel.IsCancellationRequested) return false;
                    if (++waited > 40) return false;   // ten seconds for one frame: this file will not give pictures
                }
                var first = Directory.GetFiles(work).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
                if (first != null) { File.Move(first, target, true); made++; }
                report?.Invoke(made);
            }
            if (made == 0) return false;
            File.WriteAllText(DoneMarker(dir), made.ToString());
            try { Directory.Delete(work, true); } catch (IOException) { }
            Trim(CacheLimitBytes);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or DllNotFoundException or ObjectDisposedException) { return false; }
        finally { m?.Dispose(); }
    }

    static void Touch(string dir)
    {
        try { Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow); } catch (IOException) { }
    }

    /// <summary>Keeps the cache under a size by removing the folders used longest ago. Returns folders removed.</summary>
    public static int Trim(long maxBytes)
    {
        if (!Directory.Exists(Root)) return 0;
        var dirs = new DirectoryInfo(Root).GetDirectories().Select(d => (Dir: d, Size: SizeOf(d), Used: d.LastWriteTimeUtc)).OrderBy(x => x.Used).ToList();
        var total = dirs.Sum(x => x.Size);
        var removed = 0;
        foreach (var x in dirs)
        {
            if (total <= maxBytes) break;
            try { x.Dir.Delete(true); total -= x.Size; removed++; } catch (IOException) { }
        }
        return removed;
    }

    static long SizeOf(DirectoryInfo d) => d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}
