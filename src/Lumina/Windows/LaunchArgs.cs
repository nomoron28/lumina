using System.IO;
using Lumina.Player;

namespace Lumina.Windows;

/// <summary>What the command line (or a second launch forwarding to this one) asks for.</summary>
public sealed record LaunchArgs(
    IReadOnlyList<string> Paths,
    bool Fullscreen = false,
    double? StartSeconds = null,
    IReadOnlyList<string>? SubFiles = null,
    bool Paused = false,
    IReadOnlyList<string>? Problems = null,
    string? Action = null,
    bool Background = false)
{
    public static readonly LaunchArgs None = new(Array.Empty<string>());

    public bool HasMedia => Paths.Count > 0;

    /// <summary>Housekeeping the installer asks for. These run without opening a window.</summary>
    public static readonly string[] MaintenanceActions = { "register-types", "unregister-types", "forget-logins" };

    /// <summary>The same request as a command line, with every path absolute, for forwarding to the running instance.</summary>
    public string[] ToArgs()
    {
        var l = new List<string>();
        if (Fullscreen) l.Add("--fullscreen");
        if (Paused) l.Add("--paused");
        if (StartSeconds is { } s) l.Add("--start-time=" + s.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var f in SubFiles ?? Array.Empty<string>()) l.Add("--sub-file=" + f);
        if (Paths.Count > 0) { l.Add("--"); l.AddRange(Paths); }   // after "--" a path may start with a dash
        return l.ToArray();
    }

    /// <summary>
    /// Understands: files, folders and URLs; <c>--fullscreen</c> (or <c>-f</c>); <c>--paused</c>;
    /// <c>--start-time=1:30</c> or <c>--start-time 1:30</c>; <c>--sub-file=a.srt</c> (repeatable).
    /// A bare <c>--</c> ends options. Relative paths are resolved against <paramref name="cwd"/>, because a forwarded launch
    /// runs in a different folder. Unknown or malformed options never throw: they are listed in <see cref="Problems"/>.
    /// </summary>
    public static LaunchArgs Parse(IEnumerable<string> args, string? cwd = null)
    {
        var paths = new List<string>(); var subs = new List<string>(); var problems = new List<string>();
        bool fs = false, paused = false, background = false;
        string? action = null;
        double? start = null;
        var list = args.ToList();
        var optionsEnded = false;

        string Resolve(string p)
        {
            if (p.Contains("://") || cwd == null || Path.IsPathRooted(p)) return p;
            try { return Path.GetFullPath(p, cwd); } catch (ArgumentException) { return p; }
        }

        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (optionsEnded || !a.StartsWith('-') || a == "-") { if (a.Length > 0) paths.Add(Resolve(a)); continue; }
            if (a == "--") { optionsEnded = true; continue; }

            var eq = a.IndexOf('=');
            var name = (eq > 0 ? a[..eq] : a).ToLowerInvariant();
            string? value = eq > 0 ? a[(eq + 1)..] : null;
            string? Next() => value ?? (i + 1 < list.Count && !list[i + 1].StartsWith("--") ? list[++i] : null);

            switch (name)
            {
                case "--fullscreen": case "-f": fs = true; break;
                case "--paused": case "--pause": paused = true; break;
                case "--background": background = true; break;     // start without taking over the screen: in the tray, or minimised
                case "--register-types": case "--unregister-types": case "--forget-logins": action = name[2..]; break;
                case "--start-time":
                    if (Timecode.Parse(Next()) is { } t) start = t; else problems.Add(a + " needs a time like 90 or 1:30");
                    break;
                case "--sub-file":
                    if (Next() is { Length: > 0 } s) subs.Add(Resolve(s)); else problems.Add(a + " needs a file");
                    break;
                default: problems.Add("Unknown option " + a); break;
            }
        }
        return new LaunchArgs(paths, fs, start, subs, paused, problems, action, background);
    }
}
