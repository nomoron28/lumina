using System.Runtime.InteropServices;

namespace Lumina.Player;

/// <summary>Thin libmpv wrapper: only the client API calls Lumina needs.</summary>
public sealed class Mpv : IDisposable
{
    const string Dll = "libmpv-2.dll";

    [DllImport(Dll)] static extern IntPtr mpv_create();
    [DllImport(Dll)] static extern int mpv_initialize(IntPtr h);
    [DllImport(Dll)] static extern void mpv_terminate_destroy(IntPtr h);
    [DllImport(Dll)] static extern int mpv_set_option_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll)] static extern int mpv_set_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Dll)] static extern IntPtr mpv_get_property_string(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Dll)] static extern void mpv_free(IntPtr data);
    [DllImport(Dll)] static extern int mpv_command(IntPtr h, IntPtr[] args);
    [DllImport(Dll)] static extern int mpv_command_async(IntPtr h, ulong replyUserdata, IntPtr[] args);
    [DllImport(Dll)] static extern IntPtr mpv_wait_event(IntPtr h, double timeout);
    [DllImport(Dll)] static extern void mpv_wakeup(IntPtr h);

    const int EventShutdown = 1, EventEndFile = 7, EventFileLoaded = 8, EventClientMessage = 16;

    IntPtr _h;
    Thread? _pump;
    volatile bool _stop;

    /// <summary>Extra-option lines mpv refused (unknown name or bad value).</summary>
    public List<string> BadOptions { get; } = new();

    /// <summary>Raised on the pump thread when the player shuts down or a file ends.</summary>
    public event Action<string>? Ended;

    /// <summary>Raised on the pump thread once a file is open and its tracks are known.</summary>
    public event Action? FileLoaded;

    /// <summary>Raised on the pump thread for input.conf script-message commands; args[0] is the message name.</summary>
    public event Action<string[]>? ClientMessage;

    /// <summary>A file stopped playing. reason is mpv's end-file reason (0 reached the end, 2 stopped, 3 quit, 4 error); error is its error code when reason is 4.</summary>
    public event Action<int, int>? EndFile;

    /// <summary>What went wrong, in words a person can use. Based on mpv's error code from <see cref="EndFile"/>.</summary>
    public static string FailureText(int error) => error switch
    {
        -13 => "The file or stream could not be opened. It may be missing, in use, or the connection dropped.",
        -14 => "The sound output could not be started. Another program may be using the audio device.",
        -15 => "The video output could not be started.",
        -16 => "There was nothing in that file to play.",
        -17 => "Lumina does not recognise that file format.",
        -18 => "That kind of file is not supported.",
        -9 or -11 => "The player ran out of memory or hit a limit.",
        _ => "Playback stopped because of an error (" + error + ").",
    };

    public Mpv(IntPtr hostHwnd, string configDir, string extraOptions = "")
    {
        _h = mpv_create();
        if (_h == IntPtr.Zero) throw new InvalidOperationException("mpv_create failed (is libmpv-2.dll next to the exe?)");

        Opt("wid", hostHwnd.ToInt64().ToString());
        // mpv loads config files during initialize, which would override anything set before it.
        // So we read our mpv.conf ourselves and apply it first; Settings -> Advanced lines then win.
        Opt("config", "no");
        Opt("config-dir", configDir);
        var input = System.IO.Path.Combine(configDir, "input.conf");
        if (System.IO.File.Exists(input)) Opt("input-conf", input);
        var conf = System.IO.Path.Combine(configDir, "mpv.conf");
        if (System.IO.File.Exists(conf)) ApplyLines(System.IO.File.ReadAllText(conf));
        // Lumina's own on-screen controller (the stock one is switched off in mpv.conf).
        var osc = System.IO.Path.Combine(configDir, "scripts", "lumina-osc.lua");
        if (System.IO.File.Exists(osc)) Opt("scripts", osc);
        ApplyLines(extraOptions);

        Check(mpv_initialize(_h), "mpv_initialize");

        _pump = new Thread(Pump) { IsBackground = true, Name = "mpv-events" };
        _pump.Start();
    }

    /// <summary>One key=value per line, # starts a comment line. A bad line is skipped and reported, never fatal.</summary>
    void ApplyLines(string text)
    {
        using var sr = new System.IO.StringReader(text);
        while (sr.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var i = line.IndexOf('=');
            if (i <= 0 || mpv_set_option_string(_h, line[..i].Trim(), line[(i + 1)..].Trim()) < 0)
                BadOptions.Add(i > 0 ? line[..i].Trim() : line);
        }
    }

    void Opt(string k, string v) => Check(mpv_set_option_string(_h, k, v), "option " + k);

    static void Check(int rc, string what)
    {
        if (rc < 0) throw new InvalidOperationException(what + " failed: mpv error " + rc);
    }

    void Pump()
    {
        var h = _h;
        while (!_stop)
        {
            var ev = mpv_wait_event(h, 0.25);
            var id = Marshal.ReadInt32(ev); // mpv_event.event_id is the first field
            if (id == EventShutdown) { Ended?.Invoke("shutdown"); return; }
            if (id == EventEndFile)
            {
                var data = Marshal.ReadIntPtr(ev, 16);          // mpv_event_end_file { int reason; int error; ... }
                if (data != IntPtr.Zero) EndFile?.Invoke(Marshal.ReadInt32(data, 0), Marshal.ReadInt32(data, 4));
                Ended?.Invoke("end-file");
            }
            if (id == EventFileLoaded) FileLoaded?.Invoke();
            if (id == EventClientMessage) ClientMessage?.Invoke(ReadArgs(ev));
        }
    }

    // mpv_event.data (offset 16) -> mpv_event_client_message { int num_args; char **args } (args at offset 8 on x64)
    static string[] ReadArgs(IntPtr ev)
    {
        var data = Marshal.ReadIntPtr(ev, 16);
        if (data == IntPtr.Zero) return Array.Empty<string>();
        var n = Marshal.ReadInt32(data);
        var argv = Marshal.ReadIntPtr(data, 8);
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
        return a;
    }

    void Live() { if (_h == IntPtr.Zero) throw new ObjectDisposedException(nameof(Mpv)); }

    /// <summary>A short note on screen, drawn by the controller (the player's own text banner is off).</summary>
    public void ShowText(string text, int ms = 3000) => Command("script-message", "lumina-toast", text, ms.ToString());

    /// <summary>Load an external subtitle file and switch to it.</summary>
    public void SubAdd(string path) => Command("sub-add", path, "select");

    public bool HasSubtitleTrack()
    {
        if (!int.TryParse(Get("track-list/count"), out var n)) return false;
        for (int i = 0; i < n; i++) if (Get($"track-list/{i}/type") == "sub") return true;
        return false;
    }

    public void Command(params string[] args)
    {
        Live();
        var p = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) p[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            Check(mpv_command(_h, p), "mpv_command " + args[0]);
        }
        finally { foreach (var x in p) if (x != IntPtr.Zero) Marshal.FreeCoTaskMem(x); }
    }

    /// <summary>Queue a command without waiting for mpv to run it, in order with other async commands. For a stream of pointer events, which must never stall the window.</summary>
    public void CommandAsync(params string[] args)
    {
        Live();
        var p = new IntPtr[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++) p[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            Check(mpv_command_async(_h, 0, p), "mpv_command_async " + args[0]);   // mpv copies the strings
        }
        finally { foreach (var x in p) if (x != IntPtr.Zero) Marshal.FreeCoTaskMem(x); }
    }

    public void Load(string path, double startSec = 0)
    {
        if (startSec > 0) Command("loadfile", path, "replace", "-1", "start=" + startSec.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        else Command("loadfile", path, "replace");
    }

    public void Set(string name, string value) { Live(); Check(mpv_set_property_string(_h, name, value), "set " + name); }

    public string? Get(string name)
    {
        Live();
        var p = mpv_get_property_string(_h, name);
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(p); } finally { mpv_free(p); }
    }

    public void Dispose()
    {
        var h = _h;
        if (h == IntPtr.Zero) return;
        // The pump must be out of mpv_wait_event before the handle dies, or it reads freed memory.
        _stop = true;
        mpv_wakeup(h);
        if (_pump != null && Thread.CurrentThread != _pump) _pump.Join();
        _h = IntPtr.Zero;
        mpv_terminate_destroy(h);
    }
}
