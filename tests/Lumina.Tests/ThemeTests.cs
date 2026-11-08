using System.Windows.Media;
using Lumina.Settings;
using Lumina.Themes;
using Xunit;

namespace Lumina.Tests;

public class ThemeTests
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString("#" + hex);
    static double Ratio(string a, string b) => Accent.Contrast(C(a), C(b));

    [Fact]
    public void TheFillAccentIsLivelierThanTheTextAccentButStillSeenAgainstEveryLightSurface()
    {
        foreach (var hex in Catalog.AccentSwatches)
        {
            var fill = Accent.Fill(hex, light: true);
            var text = Accent.For(hex, light: true).Accent;
            foreach (var surface in new[] { "bg", "surface", "surface-raised" })
                Assert.True(Accent.Contrast(fill, C(Palette.Light[surface])) >= 3.0, $"{hex} fill on {surface}");     // drawn things need 3:1
            Assert.True(Accent.Luminance(fill) >= Accent.Luminance(text) - 0.0001, $"{hex}: the fill is never darker than the text colour");
        }
    }

    [Fact]
    public void OnDarkTheFillIsExactlyTheChosenSwatchAndTheOrangeStaysOrangeOnLight()
    {
        foreach (var hex in Catalog.AccentSwatches) Assert.Equal(C(hex), Accent.Fill(hex, light: false));
        var orange = Accent.Fill("F28C1E", light: true);
        Assert.True(orange.R > 0xB0 && orange.R > orange.G && orange.G > orange.B, "still an orange, not brown");
        Assert.Equal(C(Palette.Light["accent-fill"]), Accent.Fill(Palette.Dark["accent"], light: true));               // the shipped default uses the palette's own value
    }

    [Theory]
    [InlineData("dark", false, false)]
    [InlineData("dark", true, false)]
    [InlineData("light", false, true)]
    [InlineData("light", true, true)]
    [InlineData("system", false, false)]
    [InlineData("system", true, true)]
    [InlineData("nonsense", true, false)]      // anything unknown keeps the shipped dark look
    [InlineData(null, true, false)]
    public void ModesResolveToLightOrDark(string? mode, bool systemLight, bool expected) => Assert.Equal(expected, ThemeManager.Resolve(mode, systemLight));

    [Theory]
    [InlineData("bg", "Bg")]
    [InlineData("surface-raised", "SurfaceRaised")]
    [InlineData("on-accent", "OnAccent")]
    [InlineData("text-muted", "TextMuted")]
    public void PaletteKeysBecomeResourceNames(string key, string name) => Assert.Equal(name, ThemeManager.Pascal(key));

    [Fact]
    public void BothPalettesDefineEveryTokenAndTheLightOneIsReallyLight()
    {
        Assert.Equal(Palette.Dark.Keys.OrderBy(k => k), Palette.Light.Keys.OrderBy(k => k));
        Assert.True(Accent.Luminance(C(Palette.Light["bg"])) > 0.85);
        Assert.True(Accent.Luminance(C(Palette.Dark["bg"])) < 0.02);
        Assert.True(Accent.Luminance(C(Palette.Light["text"])) < 0.05);       // dark text on light, light text on dark
        Assert.True(Accent.Luminance(C(Palette.Dark["text"])) > 0.7);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void EveryTextAndControlPairMeetsWcagAA(string name)
    {
        var p = name == "Dark" ? Palette.Dark : Palette.Light;
        var text = new[] { ("text", "bg"), ("text", "sidebar"), ("text", "surface"), ("text", "surface-raised"), ("text", "surface-hover"),
            ("text-muted", "bg"), ("text-muted", "sidebar"), ("text-muted", "surface"), ("text-muted", "surface-raised"),
            ("text-faint", "bg"), ("text-faint", "surface"), ("on-accent", "accent"), ("on-primary", "primary"),
            ("accent", "bg"), ("accent", "surface"), ("accent", "surface-raised"), ("danger", "surface") };
        foreach (var (fg, bg) in text) Assert.True(Ratio(p[fg], p[bg]) >= 4.5, $"{name}: {fg} on {bg} is {Ratio(p[fg], p[bg]):0.00}");
        foreach (var bg in new[] { "bg", "surface" }) Assert.True(Ratio(p["border-input"], p[bg]) >= 3, $"{name}: input border on {bg}");
    }

    [Fact]
    public void EveryAccentSwatchReadsOnBothThemes()
    {
        foreach (var hex in Catalog.AccentSwatches)
            foreach (var light in new[] { false, true })
            {
                var (a, dim, on) = Accent.For(hex, light);
                var pal = light ? Palette.Light : Palette.Dark;
                foreach (var s in new[] { "bg", "surface", "surface-raised" })
                    Assert.True(Accent.Contrast(a, C(pal[s])) >= 4.5, $"{(light ? "light" : "dark")} accent {hex} on {s}: {Accent.Contrast(a, C(pal[s])):0.00}");
                Assert.True(Accent.Contrast(on, a) >= 4.5, $"text on accent {hex} ({(light ? "light" : "dark")}): {Accent.Contrast(on, a):0.00}");
                Assert.NotEqual(a, dim);
            }
    }

    [Fact]
    public void TheDefaultAccentUsesThePalettesOwnColoursExactly()
    {
        var (a, dim, on) = Accent.For(Palette.Dark["accent"], light: false);
        Assert.Equal(C(Palette.Dark["accent"]), a);
        Assert.Equal(C(Palette.Dark["accent-dim"]), dim);
        Assert.Equal(C(Palette.Dark["on-accent"]), on);
        var (la, ldim, lon) = Accent.For(Palette.Dark["accent"], light: true);
        Assert.Equal(C(Palette.Light["accent"]), la);                          // the same swatch maps to the tuned light colour
        Assert.Equal(C(Palette.Light["accent-dim"]), ldim);
        Assert.Equal(C(Palette.Light["on-accent"]), lon);
    }

    [Fact]
    public void AnyColourAtAllIsMadeReadableOnLight()
    {
        var rng = new Random(11);
        for (var i = 0; i < 300; i++)
        {
            var hex = $"{rng.Next(0x1000000):X6}";
            var (a, _, on) = Accent.For(hex, light: true);
            foreach (var s in new[] { "bg", "surface", "surface-raised" })
                Assert.True(Accent.Contrast(a, C(Palette.Light[s])) >= 4.5, $"{hex} on {s}");
            Assert.True(Accent.Contrast(on, a) >= 4.5, $"text on {hex}: {Accent.Contrast(on, a):0.00}");
        }
    }

    [Fact]
    public void ContrastMathMatchesKnownValues()
    {
        Assert.Equal(21, Accent.Contrast(Colors.Black, Colors.White), 3);
        Assert.Equal(1, Accent.Contrast(Colors.White, Colors.White), 3);
        Assert.Equal(4.54, Accent.Contrast(C("767676"), Colors.White), 2);     // the standard "just passes AA" grey
    }
}
