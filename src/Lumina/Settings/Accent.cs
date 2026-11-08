using System.Windows;
using System.Windows.Media;
using Lumina.Themes;

namespace Lumina.Settings;

/// <summary>Recolours the accent tokens at runtime. The brushes are unfrozen in Tokens.xaml so every user of them updates.</summary>
public static class Accent
{
    public static double Luminance(Color c)
    {
        static double Ch(byte v) { var s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a); var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R * t + b.R * (1 - t)), (byte)(a.G * t + b.G * (1 - t)), (byte)(a.B * t + b.B * (1 - t)));

    /// <summary>
    /// The accent, its dim tint and the text colour that sits on it, for a chosen swatch.
    /// On a light page a pale accent is darkened until it reads as text (4.6:1) on the page and cards; on dark it is used as chosen.
    /// </summary>
    public static (Color Accent, Color Dim, Color On) For(string hex, bool light)
    {
        var chosen = (Color)ColorConverter.ConvertFromString("#" + hex);
        var pal = light ? Palette.Light : Palette.Dark;
        Color P(string k) => (Color)ColorConverter.ConvertFromString("#" + pal[k]);
        var bg = P("bg");
        if (!light)
        {
            // the shipped default uses the palette's own dim and text colours exactly
            if (string.Equals(hex, Palette.Dark["accent"], StringComparison.OrdinalIgnoreCase)) return (chosen, P("accent-dim"), P("on-accent"));
            var dim = Mix(chosen, bg, 0.26);
            return (chosen, dim, Contrast(Colors.Black, chosen) >= 4.5 ? P("on-accent") : Colors.White);
        }
        if (string.Equals(hex, Palette.Dark["accent"], StringComparison.OrdinalIgnoreCase)) return (P("accent"), P("accent-dim"), P("on-accent"));
        var c = chosen;
        var surfaces = new[] { P("bg"), P("surface"), P("surface-raised") };
        for (var i = 0; i < 30 && surfaces.Any(s => Contrast(c, s) < 4.6); i++) c = Mix(c, Colors.Black, 0.92);
        return (c, Mix(c, Colors.White, 0.18), Contrast(Colors.White, c) >= 4.5 ? Colors.White : Colors.Black);
    }

    /// <summary>
    /// The accent for things that are drawn, not read: corner markers, progress, selection rings. They need 3:1 against the page, not the 4.6:1 text needs,
    /// so a light page keeps a livelier colour than <see cref="For"/> allows. On dark the chosen colour is used as it is.
    /// </summary>
    public static Color Fill(string hex, bool light)
    {
        var chosen = (Color)ColorConverter.ConvertFromString("#" + hex);
        if (!light) return chosen;
        var pal = Palette.Light;
        Color P(string k) => (Color)ColorConverter.ConvertFromString("#" + pal[k]);
        if (string.Equals(hex, Palette.Dark["accent"], StringComparison.OrdinalIgnoreCase)) return P("accent-fill");
        var surfaces = new[] { P("bg"), P("surface"), P("surface-raised") };
        var c = chosen;
        for (var i = 0; i < 30 && surfaces.Any(s => Contrast(c, s) < 3.1); i++) c = Mix(c, Colors.Black, 0.96);
        return c;
    }

    public static void Apply(string hex)
    {
        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit) || Application.Current == null) return;
        var (accent, dim, on) = For(hex, ThemeManager.IsLight);
        Set("Accent", accent);
        Set("AccentDim", dim);
        Set("OnAccent", on);
        Set("AccentFill", Fill(hex, ThemeManager.IsLight));
    }

    static void Set(string name, Color c)
    {
        var r = Application.Current.Resources;
        r["Lumina.Color." + name] = c;
        r["Lumina.Brush." + name] = new SolidColorBrush(c);   // a fresh brush: the old one may be frozen by a style
    }
}
