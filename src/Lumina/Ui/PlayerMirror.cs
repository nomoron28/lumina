using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Lumina;

/// <summary>
/// The on-screen controller is drawn by the player, which a screen reader cannot see. This is its twin for assistive technology:
/// real, named buttons and sliders that do the same things and follow the same state, invisible and out of the mouse's way.
/// Narrator (or any UI Automation client) finds them, reads their names and can press them.
/// </summary>
public sealed class PlayerMirror : StackPanel
{
    // ---- the words (pure, tested) ----

    public static string PlayLabel(bool paused) => paused ? "Play" : "Pause";
    public static string MuteLabel(bool muted) => muted ? "Unmute" : "Mute";
    public static string FullscreenLabel(bool on) => on ? "Leave fullscreen" : "Fullscreen";

    public static string Clock(double sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>"12:34 of 54:21", or just the time when the length is unknown.</summary>
    public static string PositionText(double pos, double dur) => dur > 0 ? $"{Clock(pos)} of {Clock(dur)}" : Clock(pos);

    public static string TrackLabel(string kind, string? id, string? lang, string? title)
    {
        if (string.IsNullOrEmpty(id) || id == "no") return $"{kind}: Off";
        var name = string.Join(" ", new[] { lang?.ToUpperInvariant(), title }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return name.Length > 0 ? $"{kind}: {name}" : $"{kind}: track {id}";
    }

    public static readonly double[] Speeds = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };

    /// <summary>The speed after this one in the list; anything not in the list goes to the first step above it.</summary>
    public static double NextSpeed(double current)
    {
        foreach (var s in Speeds) if (s > current + 0.001) return s;
        return Speeds[0];
    }

    public static string SpeedLabel(double speed) => $"Speed: {speed.ToString("0.##", CultureInfo.InvariantCulture)}x";

    // ---- the controls ----

    readonly Func<string, string?> _get;
    readonly Action<string[]> _cmd;
    readonly Func<bool> _fullscreen;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly Button _play, _fs, _mute, _subs, _audio, _speed;
    readonly Slider _seek, _volume;
    bool _updating;

    public PlayerMirror(Func<string, string?> get, Action<string[]> command, Func<bool> isFullscreen, Func<double> skipSeconds,
        Action previous, Action next, Action toggleFullscreen, Action closeVideo)
    {
        _get = get; _cmd = command; _fullscreen = isFullscreen;
        Orientation = Orientation.Horizontal;
        AutomationProperties.SetName(this, "Player controls");
        Button B(string name, Action act)
        {
            var b = new Button { Content = name, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) };
            AutomationProperties.SetName(b, name);
            b.Click += (_, _) => { try { act(); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { } };
            Children.Add(b);
            return b;
        }
        Slider S(string name, Action<double> changed)
        {
            var s = new Slider { Width = 160, Margin = new Thickness(2), SmallChange = 1, LargeChange = 10 };
            AutomationProperties.SetName(s, name);
            s.ValueChanged += (_, e) => { if (!_updating) try { changed(e.NewValue); } catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { } };
            Children.Add(s);
            return s;
        }
        _play = B("Play", () => _cmd(new[] { "cycle", "pause" }));
        B("Skip back", () => _cmd(new[] { "seek", (-skipSeconds()).ToString(CultureInfo.InvariantCulture) }));
        B("Skip forward", () => _cmd(new[] { "seek", skipSeconds().ToString(CultureInfo.InvariantCulture) }));
        B("Previous item", previous);
        B("Next item", next);
        _seek = S("Seek", v => _cmd(new[] { "seek", v.ToString("0.###", CultureInfo.InvariantCulture), "absolute" }));
        _mute = B("Mute", () => _cmd(new[] { "cycle", "mute" }));
        _volume = S("Volume", v => _cmd(new[] { "set", "volume", Math.Round(v).ToString(CultureInfo.InvariantCulture) }));
        _subs = B("Subtitles: Off", () => _cmd(new[] { "cycle", "sub" }));
        _audio = B("Audio: track 1", () => _cmd(new[] { "cycle", "audio" }));
        _speed = B("Speed: 1x", () => { double.TryParse(_get("speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cur); _cmd(new[] { "set", "speed", NextSpeed(cur == 0 ? 1 : cur).ToString(CultureInfo.InvariantCulture) }); });
        _fs = B("Fullscreen", toggleFullscreen);
        B("Close video", closeVideo);
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start() { Visibility = Visibility.Visible; _timer.Start(); Refresh(); }

    public void Stop() { _timer.Stop(); Visibility = Visibility.Collapsed; }

    static double D(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    static void Rename(Button b, string name)
    {
        if (AutomationProperties.GetName(b) == name) return;
        AutomationProperties.SetName(b, name);
        b.Content = name;
    }

    void Refresh()
    {
        try
        {
            _updating = true;
            Rename(_play, PlayLabel(_get("pause") != "no"));
            Rename(_mute, MuteLabel(_get("mute") == "yes"));
            Rename(_fs, FullscreenLabel(_fullscreen()));
            Rename(_subs, TrackLabel("Subtitles", _get("sid"), _get("current-tracks/sub/lang"), _get("current-tracks/sub/title")));
            Rename(_audio, TrackLabel("Audio", _get("aid"), _get("current-tracks/audio/lang"), _get("current-tracks/audio/title")));
            Rename(_speed, SpeedLabel(D(_get("speed")) is var sp && sp > 0 ? sp : 1));

            var dur = D(_get("duration")); var pos = D(_get("time-pos"));
            if (dur > 0) { _seek.Maximum = dur; _seek.Value = Math.Clamp(pos, 0, dur); }
            AutomationProperties.SetHelpText(_seek, PositionText(pos, dur));
            var vmax = D(_get("volume-max")); _volume.Maximum = vmax > 0 ? vmax : 100;
            _volume.Value = Math.Clamp(D(_get("volume")), 0, _volume.Maximum);
            AutomationProperties.SetHelpText(_volume, $"{Math.Round(_volume.Value)} percent");
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
        finally { _updating = false; }
    }
}
