using System.IO;
using System.Windows;

namespace Lumina.Ui;

/// <summary>
/// The app's typeface (Garet) is compiled into the program as a resource. The player's on-screen controller is drawn by
/// libmpv, which can only read fonts from a folder, so the two files are written once to a folder of the app's own.
/// </summary>
public static class AppFonts
{
    static readonly string[] Files = { "Garet-Book.ttf", "Garet-Heavy.ttf" };

    public static string Folder
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("LUMINA_FONTS");
            return string.IsNullOrEmpty(root) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumina", "fonts") : root;
        }
    }

    /// <summary>Writes the font files if they are missing or a different size. Returns the folder, or null when it could not be written.</summary>
    public static string? EnsureOnDisk()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var name in Files)
            {
                System.Windows.Resources.StreamResourceInfo? info;
                try { info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Fonts/{name}", UriKind.Absolute)); }
                catch (IOException) { return null; }      // a build without the typeface (the public source has none): the program falls back to Segoe UI
                if (info == null) return null;
                using var src = info.Stream;
                var dest = Path.Combine(Folder, name);
                if (File.Exists(dest) && new FileInfo(dest).Length == src.Length) continue;
                using var o = File.Create(dest);
                src.CopyTo(o);
            }
            return Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            Lumina.Diagnostics.Log.Error("Could not write the font files for the player", e);
            return null;
        }
    }
}
