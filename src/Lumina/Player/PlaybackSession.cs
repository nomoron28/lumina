using System.Globalization;
using System.Windows.Threading;
using Lumina.Library;
using Lumina.Sync;

namespace Lumina.Player;

/// <summary>Polls mpv once a second while a library item plays: resume state to the db, scrobbles to Trakt.</summary>
public sealed class PlaybackSession
{
    /// <summary>Same cut-off Trakt uses to turn a scrobble stop into a watched mark.</summary>
    public const double WatchedFraction = 0.8;

    readonly Mpv _mpv;
    readonly MediaItem _item;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    double _pos, _dur;
    int _ticks;
    bool _started, _paused;

    public PlaybackSession(Mpv mpv, MediaItem item)
    {
        _mpv = mpv;
        _item = item;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    static double? Num(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    double Pct => _dur > 0 ? _pos / _dur * 100 : 0;

    /// <summary>
    /// What to tell Trakt when playback pauses. Reports say Trakt dropped its pause call, but a stop below 80%
    /// is stored as the resume point either way, so send that. At 80% or more a stop means watched, so wait for the real end.
    /// </summary>
    public static string? ActionOnPause(double pct) => pct < WatchedFraction * 100 ? "stop" : null;

    static bool Remember => Lumina.Settings.AppSettings.Bool("privacy.rememberPosition");

    void Scrobble(string action) { if (Lumina.Settings.AppSettings.Bool("privacy.scrobble")) _ = Trakt.ScrobbleAsync(action, _item, Pct); }

    void Tick()
    {
        Poll();
        var paused = _mpv.Get("pause") == "yes";
        if (_dur > 0)
        {
            if (!_started && !paused) { _started = true; _paused = false; Scrobble("start"); }
            else if (_started && paused && !_paused)
            {
                _paused = true;
                if (ActionOnPause(Pct) is { } a) Scrobble(a);
            }
            else if (_started && !paused && _paused) { _paused = false; Scrobble("start"); }
        }
        if (++_ticks % 15 == 0) Save();
    }

    void Poll()
    {
        if (Num(_mpv.Get("time-pos")) is { } p) _pos = p;
        if (Num(_mpv.Get("duration")) is { } d) _dur = d;
    }

    void Save()
    {
        if (_dur <= 0 || !Remember) return;
        Db.SaveWatch(_item.Id, _pos, _dur, _pos / _dur >= WatchedFraction);
    }

    /// <summary>Call before the mpv handle is gone; uses the last polled values if mpv already quit.</summary>
    public void Stop()
    {
        _timer.Stop();
        try { Poll(); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { } // mpv already shut down: keep last values
        Save();
        if (_started) Scrobble("stop");
    }
}
