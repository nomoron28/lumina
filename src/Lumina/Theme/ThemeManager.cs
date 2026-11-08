using System.IO;
using System.Windows;
using System.Windows.Media;
using Lumina.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace Lumina.Themes;

/// <summary>
/// Dark, light, or follow Windows. Every colour in the app comes from brushes that stay mutable, so switching recolours them
/// in place with no restart. The palettes are generated from tokens.json (Palette.g.cs).
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    /// <summary>"dark", "light" or "system" resolved to light or not. Anything unknown means dark, the shipped look.</summary>
    public static bool Resolve(string? mode, bool systemIsLight) => mode == "light" || (mode == "system" && systemIsLight);

    public static bool SystemIsLight()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int v && v == 1; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException) { return false; }
    }

    public static string Pascal(string key) => string.Concat(key.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    static Color Hex(string h) => (Color)ColorConverter.ConvertFromString("#" + h);

    // Controls.xaml's own brushes that mirror a palette entry (WPF-UI reads these names)
    static readonly (string Resource, string Key)[] Mirrors =
    {
        ("ApplicationBackgroundBrush", "bg"), ("TextFillColorPrimaryBrush", "text"), ("TextFillColorSecondaryBrush", "text-muted"),
        ("ControlFillColorDefaultBrush", "surface-raised"), ("ControlStrokeColorDefaultBrush", "border"),
    };

    /// <summary>Apply the saved theme and accent. Call at startup and whenever either setting changes.</summary>
    public static void Apply() => Apply(AppSettings.Get("general.theme"));

    public static void Apply(string? mode)
    {
        var app = Application.Current;
        if (app == null) return;
        IsLight = Resolve(mode, SystemIsLight());
        var palette = IsLight ? Palette.Light : Palette.Dark;
        var r = app.Resources;

        // New brush objects replace the old ones under the same keys. Styles hold DynamicResource references, so they pick them up.
        // (Changing a brush in place is not possible: WPF freezes any brush a style setter uses.)
        foreach (var (key, hex) in palette)
        {
            var c = Hex(hex);
            r["Lumina.Color." + Pascal(key)] = c;
            r["Lumina.Brush." + Pascal(key)] = new SolidColorBrush(c);
        }
        foreach (var (res, key) in Mirrors)
        {
            var c = Hex(palette[key]);
            if (res == "ApplicationBackgroundBrush") r["ApplicationBackgroundColor"] = c;
            r[res] = new SolidColorBrush(c);
        }

        // colours with transparency that fade into the page, and the scrollbar thumb, follow the page colour
        var bg = Hex(palette["bg"]);
        r["Lumina.Color.BgA90"] = Color.FromArgb(0xE6, bg.R, bg.G, bg.B);
        r["Lumina.Color.BgA60"] = Color.FromArgb(0x99, bg.R, bg.G, bg.B);
        r["Lumina.Color.BgA0"] = Color.FromArgb(0x00, bg.R, bg.G, bg.B);
        r["Lumina.Brush.ScrollThumb"] = new SolidColorBrush(IsLight ? Color.FromArgb(0x66, 0x14, 0x16, 0x1A) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

        // the sidebar is frosted glass when the setting is on: 85% of its colour, so text on it holds 4.5:1 even over a white desktop
        if (AppSettings.Bool("interface.translucency"))
        {
            var sb = Hex(palette["sidebar"]);
            r["Lumina.Brush.Sidebar"] = new SolidColorBrush(Color.FromArgb(0xD8, sb.R, sb.G, sb.B));
        }

        Accent.Apply(AppSettings.Get("general.accent"));
        try { ApplicationThemeManager.Apply(IsLight ? ApplicationTheme.Light : ApplicationTheme.Dark, updateAccent: false); }
        catch (InvalidOperationException) { }   // WPF-UI needs a window to repaint; before one exists the colours above are enough
        try { ApplicationAccentColorManager.Apply((Color)app.FindResource("Lumina.Color.Accent"), IsLight ? ApplicationTheme.Light : ApplicationTheme.Dark); }
        catch (InvalidOperationException) { }
    }

    static bool _watching;

    /// <summary>When the setting is "system", follow the Windows light/dark switch while Lumina is running.</summary>
    public static void WatchSystem()
    {
        if (_watching) return;
        _watching = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General || AppSettings.Get("general.theme") != "system") return;
            Application.Current?.Dispatcher.BeginInvoke(() => Apply("system"));
        };
    }
}
