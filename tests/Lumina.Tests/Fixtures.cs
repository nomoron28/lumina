using System.Diagnostics;
using System.IO;
using Lumina.Player;

namespace Lumina.Tests;

/// <summary>Finds (and if needed generates) the media in scratch\fixtures, and opens files in a headless libmpv.</summary>
public static class Fixtures
{
    static readonly string Repo = FindRepo();

    static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Lumina.sln"))) return d.FullName;
        throw new InvalidOperationException("Lumina.sln not found above " + AppContext.BaseDirectory);
    }

    public static string Root => Path.Combine(Repo, "scratch", "fixtures");

    /// <summary>Full path of a fixture, generating the set on first use.</summary>
    public static string Path_(string name)
    {
        var p = Path.Combine(Root, name);
        if (!File.Exists(p)) Generate();
        if (!File.Exists(p)) throw new FileNotFoundException("Fixture missing even after scripts\\make-fixtures.ps1: " + name);
        return p;
    }

    static void Generate()
    {
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(Repo, "scripts", "make-fixtures.ps1")}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit(120_000);
    }

    public static bool HasMpv => File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"));

    /// <summary>Headless mpv (no video or audio output) with the file loaded and paused. Caller disposes.</summary>
    public static Mpv Open(string fixture, string extra = "", double timeoutSec = 15)
    {
        var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null\npause=yes\n" + extra);
        using var loaded = new ManualResetEventSlim();
        m.FileLoaded += () => loaded.Set();
        m.Load(Path_(fixture));
        if (!loaded.Wait(TimeSpan.FromSeconds(timeoutSec))) { m.Dispose(); throw new TimeoutException("mpv did not load " + fixture); }
        return m;
    }

    /// <summary>Decoder-derived properties appear a moment after file-loaded; poll until one is set.</summary>
    public static string? Eventually(Mpv m, string property, double timeoutSec = 5)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < until)
        {
            if (m.Get(property) is { Length: > 0 } v) return v;
            Thread.Sleep(50);
        }
        return null;
    }
}
