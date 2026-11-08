using System.Globalization;
using Lumina.Metadata;

namespace Lumina.Settings;

/// <summary>Typed access to the catalog's values, stored flat in config.json. A value equal to its default is not stored.</summary>
public static class AppSettings
{
    public static string Get(string key) =>
        Config.Raw(key) is { Length: > 0 } v ? v : Catalog.Find(key)?.Default ?? "";

    public static bool Bool(string key) => Get(key) == "true";

    public static double Num(string key) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : double.TryParse(Catalog.Find(key)?.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;

    /// <summary>Clamp numbers to the slider range and snap to its step, so a hand-edited file cannot feed mpv nonsense.</summary>
    public static string Normalize(SettingDef d, string value)
    {
        value = value.Trim();
        if (d.Kind == Kind.Slider)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return d.Default;
            n = Math.Clamp(Math.Round(n / d.Step) * d.Step, d.Min, d.Max);
            return Catalog.Num(n);
        }
        if (d.Choices is { } cs && cs.All(c => c.Value != value)) return d.Default;
        if (d.Kind == Kind.Color) return value.TrimStart('#').Length == 6 && value.TrimStart('#').All(Uri.IsHexDigit) ? value.TrimStart('#').ToUpperInvariant() : d.Default;
        return value;
    }

    public static void Set(string key, string value) => Set(new Dictionary<string, string> { [key] = value });

    public static void Set(IReadOnlyDictionary<string, string> values)
    {
        var save = new Dictionary<string, string>();
        foreach (var (k, v) in values)
        {
            var d = Catalog.Find(k);
            var n = d is null ? v : Normalize(d, v);
            save[k] = d is not null && n == d.Default ? "" : n;
        }
        Config.Save(save);
    }

    /// <summary>mpv option lines for everything the user changed, plus the composite ones (audio filters, volume, OSC options).</summary>
    public static List<string> MpvLines()
    {
        var lines = new List<string>();
        foreach (var d in Catalog.All)
        {
            if (d.Mpv is null || Config.Raw(d.Key) is not { Length: > 0 } v || v == d.Default) continue;
            lines.AddRange(d.Mpv(Normalize(d, v)));
        }

        // Custom keys: the shipped input.conf plus the user's changes, written beside config.json and handed to mpv.
        var keys = Lumina.Player.Shortcuts.ParseOverrides(Config.Raw("shortcuts"));
        if (keys.Count > 0)
        {
            if (Lumina.Player.Shortcuts.Conflicts(keys).Count == 0)
            {
                try
                {
                    var shipped = System.IO.Path.Combine(AppContext.BaseDirectory, "input.conf");
                    var path = System.IO.Path.Combine(Config.Folder, "input.generated.conf");
                    System.IO.Directory.CreateDirectory(Config.Folder);
                    System.IO.File.WriteAllText(path, (System.IO.File.Exists(shipped) ? System.IO.File.ReadAllText(shipped) : "") + "\n" + Lumina.Player.Shortcuts.Generate(keys));
                    lines.Add("input-conf=" + path);
                }
                catch (System.IO.IOException e) { Lumina.Diagnostics.Log.Error("Could not write the custom key file", e); }
            }
            else Lumina.Diagnostics.Log.Info("Custom keys ignored: " + string.Join("; ", Lumina.Player.Shortcuts.Conflicts(keys)));
        }

        // Snapshots go to Pictures\Lumina unless the user picked a folder.
        if (Config.Raw("snapshots.folder").Length == 0)
        {
            var pics = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Lumina");
            try { System.IO.Directory.CreateDirectory(pics); lines.Add($"screenshot-directory={pics}"); } catch (System.IO.IOException) { }
        }

        if (Lumina.Player.AudioFx.Compose(Lumina.Player.AudioFxSettings.FromSettings()) is { Length: > 0 } af) lines.Add("af=" + af);

        if (Get("audio.startVolume") == "fixed") lines.Add($"volume={Catalog.Num(Num("audio.fixedVolume"))}");
        else if (Config.Raw("audio.lastVolume") is { Length: > 0 } last && double.TryParse(last, NumberStyles.Float, CultureInfo.InvariantCulture, out var lv))
            lines.Add($"volume={Catalog.Num(Math.Clamp(lv, 0, Num("audio.volumeMax")))}");

        var osc = new List<string>();
        if (Get("interface.hideAfter") != Catalog.Find("interface.hideAfter")!.Default)
            osc.Add($"lumina-osc-hide_ms={(int)(Num("interface.hideAfter") * 1000)}");
        if (Get("interface.autoHide") != "always") osc.Add($"lumina-osc-autohide={Get("interface.autoHide")}");
        if (Get("controls.skip") != "10") osc.Add($"lumina-osc-skip={Get("controls.skip")}");
        foreach (var (key, name) in new[] { ("controls.click", "click"), ("controls.doubleClick", "dclick"), ("controls.wheel", "wheel") })
            if (Get(key) != Catalog.Find(key)!.Default) osc.Add($"lumina-osc-{name}={Get(key)}");
        if (!Lumina.Player.ControllerLayout.IsDefault(Config.Raw("interface.controls")))
            osc.Add("lumina-osc-controls=" + Lumina.Player.ControllerLayout.Format(Lumina.Player.ControllerLayout.Parse(Config.Raw("interface.controls"))));
        if (!Bool("interface.seekThumbs")) osc.Add("lumina-osc-thumbs=no");
        if (Get("interface.controllerStyle") != "classic") osc.Add("lumina-osc-style=" + Get("interface.controllerStyle"));
        if (!string.Equals(Get("general.accent"), Catalog.Find("general.accent")!.Default, StringComparison.OrdinalIgnoreCase)) osc.Add("lumina-osc-accent=" + Get("general.accent"));
        if (osc.Count > 0) lines.Add($"script-opts={string.Join(",", osc)}");
        return lines;
    }

    /// <summary>What goes to <c>new Mpv(...)</c>: generated lines first, hand-written Advanced options last so they win.</summary>
    public static string MpvExtra()
    {
        var all = MpvLines();
        // the player's on-screen controller is drawn with the app's typeface, which libmpv reads from this folder
        if (System.IO.File.Exists(System.IO.Path.Combine(Lumina.Ui.AppFonts.Folder, "Garet-Book.ttf"))) { all.Add("osd-fonts-dir=" + Lumina.Ui.AppFonts.Folder); all.Add("sub-fonts-dir=" + Lumina.Ui.AppFonts.Folder); }
        if (Config.MpvOptions.Trim().Length > 0) all.Add(Config.MpvOptions);
        return string.Join("\n", all);
    }
}
