using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace Lumina.Tests;

public class PlayerMirrorWordsTests
{
    [Fact]
    public void LabelsSayWhatPressingWillDo()
    {
        Assert.Equal(("Play", "Pause"), (PlayerMirror.PlayLabel(true), PlayerMirror.PlayLabel(false)));
        Assert.Equal(("Unmute", "Mute"), (PlayerMirror.MuteLabel(true), PlayerMirror.MuteLabel(false)));
        Assert.Equal(("Leave fullscreen", "Fullscreen"), (PlayerMirror.FullscreenLabel(true), PlayerMirror.FullscreenLabel(false)));
    }

    [Theory]
    [InlineData(0, 0, "0:00")]
    [InlineData(65, 0, "1:05")]
    [InlineData(754, 3261, "12:34 of 54:21")]
    [InlineData(3725, 7200, "1:02:05 of 2:00:00")]
    [InlineData(-5, 100, "0:00 of 1:40")]
    public void PositionReadsAsClockTimeAndTheLengthWhenKnown(double pos, double dur, string text) => Assert.Equal(text, PlayerMirror.PositionText(pos, dur));

    [Theory]
    [InlineData("Subtitles", "no", null, null, "Subtitles: Off")]
    [InlineData("Subtitles", null, null, null, "Subtitles: Off")]
    [InlineData("Subtitles", "2", "eng", "Full", "Subtitles: ENG Full")]
    [InlineData("Audio", "1", "jpn", null, "Audio: JPN")]
    [InlineData("Audio", "3", null, null, "Audio: track 3")]
    [InlineData("Audio", "1", "", "  ", "Audio: track 1")]
    public void TrackNamesUseLanguageAndTitleOrFallBackToTheTrackNumber(string kind, string? id, string? lang, string? title, string text) =>
        Assert.Equal(text, PlayerMirror.TrackLabel(kind, id, lang, title));

    [Theory]
    [InlineData(1.0, 1.25)]
    [InlineData(0.5, 0.75)]
    [InlineData(2.0, 0.5)]            // the last step wraps to the first
    [InlineData(1.1, 1.25)]           // a speed set elsewhere goes to the next step above it
    [InlineData(3.0, 0.5)]
    [InlineData(0.1, 0.5)]
    public void SpeedGoesToTheNextStepAndWrapsAround(double now, double next) => Assert.Equal(next, PlayerMirror.NextSpeed(now));

    [Theory]
    [InlineData(1.0, "Speed: 1x")]
    [InlineData(1.25, "Speed: 1.25x")]
    [InlineData(0.75, "Speed: 0.75x")]
    public void SpeedLabelDropsNeedlessZeros(double speed, string text) => Assert.Equal(text, PlayerMirror.SpeedLabel(speed));
}

/// <summary>The real controls, on a UI thread of their own, with a fake player behind them.</summary>
public class PlayerMirrorControlsTests
{
    sealed class Fake
    {
        public Dictionary<string, string> Props = new() { ["pause"] = "yes", ["mute"] = "no", ["volume"] = "70", ["volume-max"] = "130", ["duration"] = "600", ["time-pos"] = "90", ["speed"] = "1", ["sid"] = "no", ["aid"] = "1" };
        public List<string> Commands = new();
        public bool Fullscreen;
        public List<string> Calls = new();
    }

    static void OnUi(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("on the UI thread: " + error, error);
    }

    static PlayerMirror Make(Fake f) => new(
        name => f.Props.GetValueOrDefault(name), args => f.Commands.Add(string.Join(" ", args)), () => f.Fullscreen, () => 10,
        () => f.Calls.Add("previous"), () => f.Calls.Add("next"), () => f.Calls.Add("fullscreen"), () => f.Calls.Add("close"));

    static T Find<T>(PlayerMirror m, string name) where T : FrameworkElement =>
        m.Children.OfType<T>().Single(c => AutomationProperties.GetName(c) == name);

    static IEnumerable<string> Names(PlayerMirror m) => m.Children.OfType<FrameworkElement>().Select(AutomationProperties.GetName);

    static void Press(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void EveryControlHasANameAndTheSetIsComplete() => OnUi(() =>
    {
        var m = Make(new Fake());
        Assert.All(Names(m), n => Assert.False(string.IsNullOrWhiteSpace(n)));
        foreach (var expected in new[] { "Play", "Skip back", "Skip forward", "Previous item", "Next item", "Seek", "Mute", "Volume", "Subtitles: Off", "Audio: track 1", "Speed: 1x", "Fullscreen", "Close video" })
            Assert.Contains(expected, Names(m));
        Assert.Equal("Player controls", AutomationProperties.GetName(m));
    });

    [Fact]
    public void NamesFollowThePlayersState() => OnUi(() =>
    {
        var f = new Fake(); var m = Make(f);
        m.Start();
        Assert.Contains("Play", Names(m));                                      // paused: pressing it plays
        f.Props["pause"] = "no"; f.Props["mute"] = "yes"; f.Props["sid"] = "2"; f.Props["current-tracks/sub/lang"] = "eng"; f.Props["speed"] = "1.5"; f.Fullscreen = true;
        m.Stop(); m.Start();                                                    // Start refreshes at once
        var names = Names(m).ToList();
        Assert.Contains("Pause", names); Assert.Contains("Unmute", names); Assert.Contains("Subtitles: ENG", names); Assert.Contains("Speed: 1.5x", names); Assert.Contains("Leave fullscreen", names);
        Assert.DoesNotContain("Play", names);
        m.Stop();
    });

    [Fact]
    public void ButtonsSendTheRightCommands() => OnUi(() =>
    {
        var f = new Fake(); var m = Make(f);
        m.Start();
        Press(Find<Button>(m, "Play"));
        Press(Find<Button>(m, "Skip back"));
        Press(Find<Button>(m, "Skip forward"));
        Press(Find<Button>(m, "Mute"));
        Press(Find<Button>(m, "Subtitles: Off"));
        Press(Find<Button>(m, "Audio: track 1"));
        Press(Find<Button>(m, "Speed: 1x"));
        Assert.Equal(new[] { "cycle pause", "seek -10", "seek 10", "cycle mute", "cycle sub", "cycle audio", "set speed 1.25" }, f.Commands);
        Press(Find<Button>(m, "Previous item")); Press(Find<Button>(m, "Next item")); Press(Find<Button>(m, "Fullscreen")); Press(Find<Button>(m, "Close video"));
        Assert.Equal(new[] { "previous", "next", "fullscreen", "close" }, f.Calls);
        m.Stop();
    });

    [Fact]
    public void SlidersShowThePlayerStateWithoutSendingCommandsAndSendThemWhenAPersonMovesThem() => OnUi(() =>
    {
        var f = new Fake(); var m = Make(f);
        m.Start();
        var seek = Find<Slider>(m, "Seek"); var vol = Find<Slider>(m, "Volume");
        Assert.Equal((600, 90), (seek.Maximum, seek.Value));
        Assert.Equal((130, 70), (vol.Maximum, vol.Value));
        Assert.Equal("1:30 of 10:00", AutomationProperties.GetHelpText(seek));
        Assert.Equal("70 percent", AutomationProperties.GetHelpText(vol));
        Assert.Empty(f.Commands);                                               // reading the state is not an instruction

        seek.Value = 300;                                                       // what a screen reader's "set value" does
        vol.Value = 40;
        Assert.Equal(new[] { "seek 300 absolute", "set volume 40" }, f.Commands);
        m.Stop();
    });

    [Fact]
    public void ADeadPlayerNeverCrashesTheMirror() => OnUi(() =>
    {
        var m = new PlayerMirror(_ => throw new ObjectDisposedException("mpv"), _ => throw new ObjectDisposedException("mpv"), () => false, () => 10, () => { }, () => { }, () => { }, () => { });
        m.Start();
        Press(Find<Button>(m, "Play"));
        m.Stop();
    });

    [Fact]
    public void StartShowsAndStopHidesSoTheControlsExistOnlyWhileSomethingPlays() => OnUi(() =>
    {
        var m = Make(new Fake());
        m.Start(); Assert.Equal(Visibility.Visible, m.Visibility);
        m.Stop(); Assert.Equal(Visibility.Collapsed, m.Visibility);
    });
}
