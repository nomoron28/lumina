using System.IO;

namespace Lumina.Windows;

/// <summary>
/// Where Lumina keeps its data. Normally under the user's AppData folders. If a file named <c>portable.txt</c> sits next to the
/// program, everything goes to a <c>data</c> folder beside it instead, so the whole app can live on a USB stick or in a zip.
/// The individual LUMINA_* overrides used by tests still win over both.
/// </summary>
public static class AppPaths
{
    /// <summary>Folder of the running program. Tests can point this elsewhere.</summary>
    public static string ProgramDir => Environment.GetEnvironmentVariable("LUMINA_PROGRAM_DIR") is { Length: > 0 } d ? d : AppContext.BaseDirectory;

    static (string Dir, bool Portable)? _memo;

    /// <summary>Looked up once per program folder: settings are read many times a second, the marker never changes while running.</summary>
    public static bool IsPortable
    {
        get
        {
            var dir = ProgramDir;
            if (_memo is { } m && m.Dir == dir) return m.Portable;
            var p = File.Exists(Path.Combine(dir, "portable.txt"));
            _memo = (dir, p);
            return p;
        }
    }

    static string Appdata(Environment.SpecialFolder f) => Path.Combine(Environment.GetFolderPath(f), "Lumina");

    /// <summary>Settings, library database and logs.</summary>
    public static string Data => IsPortable ? Path.Combine(ProgramDir, "data") : Appdata(Environment.SpecialFolder.ApplicationData);

    /// <summary>Artwork and downloaded subtitles: things that can be fetched again.</summary>
    public static string Cache => IsPortable ? Path.Combine(ProgramDir, "data", "cache") : Appdata(Environment.SpecialFolder.LocalApplicationData);
}
