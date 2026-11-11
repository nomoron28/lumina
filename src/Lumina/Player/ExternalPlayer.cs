using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Lumina.Player;

/// <summary>
/// "Play with": hand a video to another installed player. mpv and VLC report where playback stopped (mpv over its IPC pipe,
/// VLC over its local web interface with a one-time password); the others cannot, so the caller asks the user instead.
/// </summary>
public static class ExternalPlayer
{
    public enum Report { None, Mpv, Vlc }

    public sealed record Kind(string Id, string Name, string[] Exes, string[] Dirs, Report Report);

    public static readonly IReadOnlyList<Kind> Known = new Kind[]
    {
        new("vlc", "VLC", new[] { "vlc.exe" }, new[] { @"VideoLAN\VLC" }, Report.Vlc),
        new("mpc-hc", "MPC-HC", new[] { "mpc-hc64.exe", "mpc-hc.exe" }, new[] { "MPC-HC", @"K-Lite Codec Pack\MPC-HC64" }, Report.None),
        new("mpc-be", "MPC-BE", new[] { "mpc-be64.exe", "mpc-be.exe" }, new[] { "MPC-BE x64", "MPC-BE" }, Report.None),
        new("potplayer", "PotPlayer", new[] { "PotPlayerMini64.exe", "PotPlayerMini.exe" }, new[] { @"DAUM\PotPlayer", "PotPlayer" }, Report.None),
        new("mpv", "mpv", new[] { "mpv.exe" }, new[] { "mpv" }, Report.Mpv),
        new("mpvnet", "mpv.net", new[] { "mpvnet.exe" }, new[] { "mpv.net" }, Report.Mpv),
    };

    public static Kind? Get(string id) => Known.FirstOrDefault(k => k.Id == id);

    /// <summary>Where a player is installed: its App Paths entry, the usual program folders, then PATH (Scoop, winget portable).</summary>
    public static string? Locate(Kind k, Func<string, bool>? exists = null, Func<string, string?>? appPath = null)
    {
        exists ??= File.Exists;
        appPath ??= RegistryAppPath;
        foreach (var exe in k.Exes)
            if (appPath(exe) is { } p && exists(p)) return p;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        }.Where(r => r.Length > 0);
        foreach (var root in roots)
            foreach (var dir in k.Dirs)
                foreach (var exe in k.Exes)
                    if (Path.Combine(root, dir, exe) is var p && exists(p)) return p;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var exe in k.Exes)
            {
                string p;
                try { p = Path.Combine(dir, exe); } catch (ArgumentException) { continue; }   // a malformed PATH entry
                if (exists(p)) return p;
            }
        return null;
    }

    static string? RegistryAppPath(string exe)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
            if (key?.GetValue(null) is string p && p.Trim('"') is { Length: > 0 } clean) return clean;
        }
        return null;
    }

    /// <summary>The installed players, for the "Play with" menu.</summary>
    public static List<(Kind Kind, string Path)> Installed() =>
        Known.Select(k => (k, Locate(k))).Where(x => x.Item2 != null).Select(x => (x.k, x.Item2!)).ToList();

    static string Q(string s) => "\"" + s + "\"";
    static string Sec(double s) => s.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The command line for each player, in its own syntax: start position, subtitle file, and how it reports back.</summary>
    /// <param name="channel">mpv: the pipe name. VLC: "port:password" for its web interface.</param>
    public static string Arguments(string id, string file, double start, string? sub, string? channel = null, string? template = null)
    {
        start = Math.Max(0, start);
        switch (id)
        {
            case "vlc":
            {
                var a = new List<string> { "--no-one-instance" };
                if (start > 0) a.Add("--start-time=" + Sec(start));
                if (sub != null) a.Add("--sub-file=" + Q(sub));
                if (channel?.Split(':') is [var port, var pw])
                    a.AddRange(new[] { "--extraintf=http", "--http-host=127.0.0.1", "--http-port=" + port, "--http-password=" + pw });
                a.Add(Q(file));
                return string.Join(" ", a);
            }
            case "mpv":
            case "mpvnet":
            {
                var a = new List<string>();
                if (start > 0) a.Add("--start=" + Sec(start));
                if (sub != null) a.Add("--sub-file=" + Q(sub));
                if (channel != null) a.Add(@"--input-ipc-server=\\.\pipe\" + channel);
                a.Add(Q(file));
                return string.Join(" ", a);
            }
            case "mpc-hc":
            case "mpc-be":
                return Q(file) + (start > 0 ? " /start " + ((long)(start * 1000)).ToString(CultureInfo.InvariantCulture) : "") + (sub != null ? " /sub " + Q(sub) : "");
            case "potplayer":
                return Q(file) + (start > 0 ? " /seek=" + TimeSpan.FromSeconds(Math.Floor(start)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "") + (sub != null ? " /sub=" + Q(sub) : "");
            default:   // another program: the user's template
                return (string.IsNullOrWhiteSpace(template) ? "\"{file}\"" : template)
                    .Replace("{file}", file).Replace("{startms}", ((long)(start * 1000)).ToString(CultureInfo.InvariantCulture))
                    .Replace("{start}", Sec(start)).Replace("{sub}", sub ?? "");
        }
    }

    /// <summary>VLC's /requests/status.json: the position and length in seconds, when it has them.</summary>
    public static (double? Pos, double? Dur) ParseVlcStatus(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json);
            double? N(string n) => d.RootElement.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
            return (N("time"), N("length") is > 0 and var l ? l : null);
        }
        catch (JsonException) { return (null, null); }
    }

    /// <summary>One mpv IPC reply line: {"data":12.5,"error":"success"}.</summary>
    public static double? ParseMpvReply(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var d = JsonDocument.Parse(line);
            return d.RootElement.TryGetProperty("data", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        }
        catch (JsonException) { return null; }
    }

    public sealed class NotFoundException(string message) : Exception(message);

    /// <summary>
    /// Start the player and watch it. Exited fires once, on a worker thread, with the last position and length it reported
    /// (both null for players that cannot report).
    /// </summary>
    public sealed class Session
    {
        public string Name { get; }
        public event Action<double?, double?>? Exited;
        readonly Process _proc;
        double? _pos, _dur;

        Session(string name, Process proc) { Name = name; _proc = proc; }

        public static Session Start(string id, string file, double start, string? sub, string? pathOverride, string? template)
        {
            var kind = Get(id);
            var name = kind?.Name ?? (pathOverride is { Length: > 0 } ? Path.GetFileNameWithoutExtension(pathOverride) : "the other player");
            var exe = pathOverride is { Length: > 0 } o ? o.Trim('"') : kind != null ? Locate(kind) : null;
            if (exe == null || !File.Exists(exe))
                throw new NotFoundException(kind == null
                    ? "Choose the other player's program in Settings, Playback, Player"
                    : $"{kind.Name} was not found. Install it, or choose its program in Settings, Playback, Player");

            string? channel = kind?.Report switch
            {
                Report.Mpv => "lumina-ext-" + Guid.NewGuid().ToString("N")[..8],
                Report.Vlc => FreePort() + ":" + Guid.NewGuid().ToString("N"),
                _ => null,
            };
            var psi = new ProcessStartInfo(exe, Arguments(kind?.Id ?? "custom", file, start, sub, channel, template)) { UseShellExecute = false };
            var proc = Process.Start(psi) ?? throw new NotFoundException($"{name} did not start");
            var s = new Session(name, proc);
            _ = Task.Run(() => s.Watch(kind?.Report ?? Report.None, channel));
            return s;
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
        }

        async Task Watch(Report report, string? channel)
        {
            using var http = report == Report.Vlc ? new HttpClient { Timeout = TimeSpan.FromSeconds(2) } : null;
            if (http != null && channel!.Split(':') is [_, var pw])
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + pw)));
            while (!_proc.HasExited)
            {
                try
                {
                    if (report == Report.Mpv) await PollMpv(channel!);
                    else if (report == Report.Vlc) PollVlc(await http!.GetStringAsync($"http://127.0.0.1:{channel!.Split(':')[0]}/requests/status.json"));
                }
                catch (Exception e) when (e is IOException or TimeoutException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException) { }   // not ready yet, or closing
                await Task.Delay(2000);
            }
            Exited?.Invoke(_pos, _dur);
        }

        void PollVlc(string json)
        {
            var (p, d) = ParseVlcStatus(json);
            if (p is { } pos && (d ?? _dur) is > 0) { _pos = pos; _dur = d ?? _dur; }
        }

        async Task PollMpv(string pipe)
        {
            using var c = new NamedPipeClientStream(".", pipe, PipeDirection.InOut);
            await c.ConnectAsync(500);
            using var w = new StreamWriter(c, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            using var r = new StreamReader(c, Encoding.UTF8, leaveOpen: true);
            async Task<double?> Get(string prop, int id)
            {
                await w.WriteLineAsync($$"""{"command":["get_property","{{prop}}"],"request_id":{{id}}}""");
                // mpv may interleave event lines; take the reply that carries our request id
                for (var i = 0; i < 20; i++)
                {
                    var line = await r.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(1));
                    if (line == null) return null;
                    if (line.Contains($"\"request_id\":{id}")) return ParseMpvReply(line);
                }
                return null;
            }
            var pos = await Get("time-pos", 1);
            var dur = await Get("duration", 2);
            if (pos is { } p && dur is > 0) { _pos = p; _dur = dur; }
        }
    }
}
