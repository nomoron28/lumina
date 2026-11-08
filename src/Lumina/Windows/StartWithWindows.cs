using Microsoft.Win32;

namespace Lumina.Windows;

/// <summary>
/// Start Lumina when the user signs in: one value under the current user's Run key (no admin rights), removable, and quiet:
/// it starts with --background, so it waits in the tray (or minimised) instead of taking over the screen.
/// LUMINA_REG_BRANCH moves it to a private registry branch for tests.
/// </summary>
public static class StartWithWindows
{
    public const string ValueName = "Lumina";

    static string RunKey => Environment.GetEnvironmentVariable("LUMINA_REG_BRANCH") is { Length: > 0 } b
        ? b + @"\Run"
        : @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string CommandFor(string exePath) => $"\"{exePath}\" --background";

    public static bool IsOn()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string s && s.Length > 0;
    }

    /// <summary>The command stored, or null when off.</summary>
    public static string? Stored()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) as string;
    }

    /// <summary>Turns it on or off. Turning it on again rewrites the command, so a moved install keeps working.</summary>
    public static void Set(bool on, string? exePath = null)
    {
        exePath ??= Environment.ProcessPath ?? throw new InvalidOperationException("The program path is unknown.");
        if (on)
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            k.SetValue(ValueName, CommandFor(exePath));
        }
        else
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
