using System.Runtime.InteropServices;
using Lumina.Library;
using Microsoft.Win32;

namespace Lumina.Windows;

/// <summary>
/// Registers Lumina as a video app for the current user only (HKCU, no admin rights): it appears in "Open with" and in
/// Settings, Default apps. Windows does not let an app pick itself as the default silently, so the user finishes that step.
/// Everything lives under two injectable roots so tests use a throwaway branch of the registry.
/// </summary>
public sealed class FileAssociations
{
    public const string ProgId = "Lumina.Video";
    public const string AppKey = "Lumina";

    readonly string _classes;   // normally Software\Classes
    readonly string _software;  // normally Software
    readonly string _exe;

    public FileAssociations(string exePath, string classesRoot = @"Software\Classes", string softwareRoot = "Software")
    {
        _exe = exePath; _classes = classesRoot; _software = softwareRoot;
    }

    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>This app's registration. LUMINA_REG_BRANCH moves it to a private registry branch for tests.</summary>
    public static FileAssociations ForThisApp() =>
        Environment.GetEnvironmentVariable("LUMINA_REG_BRANCH") is { Length: > 0 } b
            ? new FileAssociations(Environment.ProcessPath!, b + @"\Classes", b + @"\Software")
            : new FileAssociations(Environment.ProcessPath!);

    public static IReadOnlyList<string> Extensions { get; } = new[] { ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".mts", ".webm", ".wmv", ".flv", ".iso" };

    string Command => $"\"{_exe}\" -- \"%1\"";

    static RegistryKey Create(string path) => Registry.CurrentUser.CreateSubKey(path, writable: true);

    public void Register(bool notifyShell = false)
    {
        using (var k = Create($@"{_classes}\{ProgId}")) { k.SetValue("", "Lumina video"); k.SetValue("FriendlyTypeName", "Lumina video"); }
        using (var k = Create($@"{_classes}\{ProgId}\DefaultIcon")) k.SetValue("", $"\"{_exe}\",0");
        using (var k = Create($@"{_classes}\{ProgId}\shell\open\command")) k.SetValue("", Command);

        // "Open with" lists apps registered here even before the user has chosen one.
        var exeName = System.IO.Path.GetFileName(_exe);
        using (var k = Create($@"{_classes}\Applications\{exeName}")) k.SetValue("FriendlyAppName", "Lumina");
        using (var k = Create($@"{_classes}\Applications\{exeName}\shell\open\command")) k.SetValue("", Command);
        using (var k = Create($@"{_classes}\Applications\{exeName}\SupportedTypes"))
            foreach (var e in Extensions) k.SetValue(e, "");

        foreach (var e in Extensions)
        {
            using var k = Create($@"{_classes}\{e}\OpenWithProgids");
            k.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // The entry Settings, Default apps reads.
        using (var k = Create($@"{_software}\{AppKey}\Capabilities"))
        {
            k.SetValue("ApplicationName", "Lumina");
            k.SetValue("ApplicationDescription", "Plays almost any video file, with your library, subtitles and Trakt.");
        }
        using (var k = Create($@"{_software}\{AppKey}\Capabilities\FileAssociations"))
            foreach (var e in Extensions) k.SetValue(e, ProgId);
        using (var k = Create($@"{_software}\RegisteredApplications")) k.SetValue(AppKey, $@"{_software}\{AppKey}\Capabilities");

        if (notifyShell) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED
    }

    /// <summary>Removes only what Register added. Other apps' entries on the same extensions are left alone.</summary>
    public void Unregister(bool notifyShell = false)
    {
        var exeName = System.IO.Path.GetFileName(_exe);
        foreach (var e in Extensions)
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{_classes}\{e}\OpenWithProgids", writable: true);
            k?.DeleteValue(ProgId, throwOnMissingValue: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree($@"{_classes}\{ProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{_classes}\Applications\{exeName}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{_software}\{AppKey}", throwOnMissingSubKey: false);
        using (var k = Registry.CurrentUser.OpenSubKey($@"{_software}\RegisteredApplications", writable: true))
            k?.DeleteValue(AppKey, throwOnMissingValue: false);
        if (notifyShell) SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public bool IsRegistered()
    {
        using var open = Registry.CurrentUser.OpenSubKey($@"{_classes}\{ProgId}\shell\open\command");
        if (open?.GetValue("") is not string cmd || !cmd.Contains(_exe, StringComparison.OrdinalIgnoreCase)) return false;
        using var cap = Registry.CurrentUser.OpenSubKey($@"{_software}\{AppKey}\Capabilities\FileAssociations");
        return cap != null;
    }

    /// <summary>Opens Windows Settings at the page where the user can make Lumina the default.</summary>
    public static void OpenDefaultAppsSettings() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Lumina") { UseShellExecute = true });
}
