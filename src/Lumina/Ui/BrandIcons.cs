using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace Lumina.Ui;

/// <summary>The aperture icons that are not drawn by WPF: the system tray glyph, chosen to match the taskbar.</summary>
public static class BrandIcons
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    const int SmCxSmIcon = 49;

    static System.Drawing.Icon? _tray;     // kept so the handle stays valid for the life of the tray entry

    /// <summary>True when the Windows taskbar is light (the glyph must then be dark).</summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    /// <summary>The tray icon handle: flat aperture, white on a dark taskbar and near-black on a light one. Falls back to a stock icon if the resource is missing.</summary>
    public static IntPtr Tray()
    {
        try
        {
            var name = TaskbarIsLight() ? "lumina-tray-light.ico" : "lumina-tray-dark.ico";
            var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Brand/{name}", UriKind.Absolute));
            if (info != null)
            {
                var px = Math.Max(16, GetSystemMetrics(SmCxSmIcon));
                using var s = info.Stream;
                _tray = new System.Drawing.Icon(s, new System.Drawing.Size(px, px));
                return _tray.Handle;
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { }
        return System.Drawing.SystemIcons.Application.Handle;
    }
}
