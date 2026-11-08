using System.Reflection;

namespace Lumina.Windows;

/// <summary>What the About box and bug reports say about this build.</summary>
public static class AppInfo
{
    /// <summary>Three-part version, from the assembly: 0.1.0.</summary>
    public static Version Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string VersionText => Version.ToString(3);

    /// <summary>Compare two version strings such as "0.2.0" and "v0.10.1". Null when either is not a version.</summary>
    public static int? Compare(string? a, string? b)
    {
        if (!TryParse(a, out var x) || !TryParse(b, out var y)) return null;
        return x.CompareTo(y);
    }

    public static bool TryParse(string? text, out Version v)
    {
        v = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimStart('v', 'V');
        var core = text.Split('-', '+')[0];                   // 1.2.3-beta and 1.2.3+build both mean 1.2.3
        if (!core.Contains('.')) core += ".0";                  // a bare "2" is 2.0
        if (!Version.TryParse(core, out var p) || p.Major < 0 || p.Minor < 0) return false;
        v = new Version(p.Major, p.Minor, Math.Max(0, p.Build));
        return true;
    }

    /// <summary>Plain lines for the About dialog. The player part is filled by the caller because it needs a running mpv.</summary>
    public static List<(string Label, string Value)> Describe(string? player = null) => new()
    {
        ("Lumina", VersionText),
        ("Mode", AppPaths.IsPortable ? "Portable (data next to the program)" : "Installed (data in your user profile)"),
        ("Data folder", AppPaths.Data),
        ("Player", player ?? "mpv"),
        (".NET", Environment.Version.ToString()),
        ("Windows", Environment.OSVersion.Version.ToString()),
    };
}
