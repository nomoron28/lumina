namespace Lumina.Player;

/// <summary>
/// "Stop after a while" or "stop when this ends". It lives in the app rather than in the player, because the player is restarted for
/// every file in a queue and the timer has to carry on across them. The clock is passed in, so every case is testable.
/// </summary>
public sealed class SleepTimer
{
    public enum Mode { Off, Timer, EndOfFile }

    public Mode Current { get; private set; } = Mode.Off;
    DateTime _ends;
    int _step;

    /// <summary>The steps one press of the button goes through, in order: off, then minutes, then the end of the file.</summary>
    public static readonly IReadOnlyList<(Mode Mode, int Minutes)> Steps = new[]
    {
        (Mode.Off, 0), (Mode.Timer, 15), (Mode.Timer, 30), (Mode.Timer, 45), (Mode.Timer, 60), (Mode.Timer, 90), (Mode.EndOfFile, 0),
    };

    public void Off() { Current = Mode.Off; _step = 0; }

    public void StartMinutes(double minutes, DateTime now)
    {
        if (minutes <= 0 || double.IsNaN(minutes) || double.IsInfinity(minutes)) { Off(); return; }
        Current = Mode.Timer;
        _ends = now.AddMinutes(Math.Min(minutes, 24 * 60));
        _step = -1;                       // a custom length: the next press goes back to the start of the list
    }

    public void StartAtEnd() { Current = Mode.EndOfFile; _step = Steps.Count - 1; }

    /// <summary>Next choice in the list. After the last one it goes back to off.</summary>
    public void Cycle(DateTime now)
    {
        _step = (_step + 1) % Steps.Count;
        var (mode, minutes) = Steps[_step];
        if (mode == Mode.Timer) { Current = Mode.Timer; _ends = now.AddMinutes(minutes); }
        else Current = mode;
    }

    /// <summary>True once the time is up. It stays true until <see cref="Off"/>, so the caller turns it off when it acts.</summary>
    public bool TimeIsUp(DateTime now) => Current == Mode.Timer && now >= _ends;

    public bool StopsAtEnd => Current == Mode.EndOfFile;

    public TimeSpan Left(DateTime now) => Current == Mode.Timer ? (_ends > now ? _ends - now : TimeSpan.Zero) : TimeSpan.Zero;

    /// <summary>What the button shows: Off, 25 min left, Under 1 min left, End of file.</summary>
    public string Label(DateTime now)
    {
        switch (Current)
        {
            case Mode.Timer:
                var m = (int)Math.Ceiling(Left(now).TotalMinutes);
                return m <= 1 ? "Under 1 min left" : $"{m} min left";
            case Mode.EndOfFile: return "End of file";
            default: return "Off";
        }
    }
}
