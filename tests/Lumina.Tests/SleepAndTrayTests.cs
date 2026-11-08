using System.IO;
using Lumina.Player;
using Lumina.Settings;
using Lumina.Windows;
using Microsoft.Win32;
using Xunit;

namespace Lumina.Tests;

public class SleepTimerTests
{
    static readonly DateTime T0 = new(2026, 10, 17, 2, 0, 0);

    [Fact]
    public void ItStartsOffAndNeverFiresWhileOff()
    {
        var t = new SleepTimer();
        Assert.Equal(("Off", false, false), (t.Label(T0), t.TimeIsUp(T0.AddDays(5)), t.StopsAtEnd));
    }

    [Fact]
    public void AMinutesTimerCountsDownFiresOnTimeAndReadsInMinutes()
    {
        var t = new SleepTimer();
        t.StartMinutes(30, T0);
        Assert.Equal("30 min left", t.Label(T0));
        Assert.Equal("15 min left", t.Label(T0.AddMinutes(15)));
        Assert.Equal("2 min left", t.Label(T0.AddMinutes(28.5)));
        Assert.Equal("Under 1 min left", t.Label(T0.AddMinutes(29.5)));
        Assert.False(t.TimeIsUp(T0.AddMinutes(29.99)));
        Assert.True(t.TimeIsUp(T0.AddMinutes(30)));
        Assert.True(t.TimeIsUp(T0.AddMinutes(300)));                     // late is still up: a sleeping PC wakes to a fired timer
        Assert.Equal(TimeSpan.Zero, t.Left(T0.AddHours(2)));
        t.Off();
        Assert.False(t.TimeIsUp(T0.AddDays(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NonsenseLengthsTurnItOffAndHugeOnesAreCappedAtADay(double minutes)
    {
        var t = new SleepTimer();
        t.StartMinutes(30, T0);
        t.StartMinutes(minutes, T0);
        Assert.Equal(SleepTimer.Mode.Off, t.Current);
        t.StartMinutes(99999, T0);
        Assert.True(t.Left(T0) <= TimeSpan.FromHours(24));
    }

    [Fact]
    public void ThePressOfTheButtonWalksOffFifteenThirtyFortyFiveSixtyNinetyEndOfFileThenOff()
    {
        var t = new SleepTimer();
        var seen = new List<string>();
        for (var i = 0; i < 8; i++) { t.Cycle(T0); seen.Add(t.Label(T0)); }
        Assert.Equal(new[] { "15 min left", "30 min left", "45 min left", "60 min left", "90 min left", "End of file", "Off", "15 min left" }, seen);
    }

    [Fact]
    public void EndOfFileDoesNotFireOnTimeButAsksToStopAtTheEnd()
    {
        var t = new SleepTimer();
        t.StartAtEnd();
        Assert.True(t.StopsAtEnd);
        Assert.False(t.TimeIsUp(T0.AddDays(1)));
        Assert.Equal("End of file", t.Label(T0));
    }

    [Fact]
    public void ACustomLengthMakesTheNextPressStartTheListAgain()
    {
        var t = new SleepTimer();
        t.StartMinutes(7, T0);
        t.Cycle(T0);
        Assert.Equal("Off", t.Label(T0));
        t.Cycle(T0);
        Assert.Equal("15 min left", t.Label(T0));
    }

    [Fact]
    public void StartingEndOfFileThenCyclingContinuesFromThere()
    {
        var t = new SleepTimer();
        t.StartAtEnd();
        t.Cycle(T0);
        Assert.Equal("Off", t.Label(T0));
    }
}

public class PowerActionTests : IDisposable
{
    readonly string _log = Path.Combine(Path.GetTempPath(), "lumina-power-" + Guid.NewGuid().ToString("N") + ".log");
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_POWER_LOG");
    public PowerActionTests() => Environment.SetEnvironmentVariable("LUMINA_POWER_LOG", _log);
    public void Dispose() { Environment.SetEnvironmentVariable("LUMINA_POWER_LOG", _old); File.Delete(_log); }

    [Theory]
    [InlineData("pause")]
    [InlineData("stop")]
    [InlineData("nonsense")]
    [InlineData("")]
    public void KindsThatStayInsideLuminaRunNothing(string kind)
    {
        Assert.False(PowerAction.Run(kind));
        Assert.False(File.Exists(_log));
    }

    [Fact]
    public void SleepIsSuspendNotHibernateAndShutdownGivesAFullMinute()
    {
        Assert.True(PowerAction.Run("sleep"));
        Assert.True(PowerAction.Run("shutdown"));
        var lines = File.ReadAllLines(_log);
        Assert.Equal("sleep|rundll32.exe|powrprof.dll,SetSuspendState 0,1,0", lines[0]);
        Assert.StartsWith("shutdown|shutdown.exe|/s /t 60 ", lines[1]);
        Assert.DoesNotContain("/f", lines[1].Split('|')[2].Split(' '));                  // never forced: programs with unsaved work can object
        Assert.Contains("shutdown /a", lines[1]);                                        // and the way to cancel is on the screen
    }

    [Fact]
    public void EveryKindTheSettingOffersIsKnown()
    {
        var offered = Catalog.Find("playback.afterSleep")!.Choices!.Select(c => c.Value).ToArray();
        Assert.Equal(PowerAction.Kinds, offered);
        Assert.Equal("pause", Catalog.Find("playback.afterSleep")!.Default);               // the safe choice unless the user picks otherwise
    }
}

[Collection("db")]    // the registry branch is chosen with an environment variable
public class StartWithWindowsTests : IDisposable
{
    readonly string _branch = @"Software\LuminaTest\" + Guid.NewGuid().ToString("N");
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_REG_BRANCH");
    public StartWithWindowsTests() => Environment.SetEnvironmentVariable("LUMINA_REG_BRANCH", _branch);
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_REG_BRANCH", _old);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\LuminaTest\" + _branch.Split('\\')[^1], throwOnMissingSubKey: false);
    }

    [Fact]
    public void OnWritesAQuotedBackgroundCommandOffRemovesItAndBothAreRepeatable()
    {
        Assert.False(StartWithWindows.IsOn());
        StartWithWindows.Set(true, @"C:\Program Files\Lumina\Lumina.exe");
        Assert.True(StartWithWindows.IsOn());
        Assert.Equal("\"C:\\Program Files\\Lumina\\Lumina.exe\" --background", StartWithWindows.Stored());
        StartWithWindows.Set(true, @"C:\Program Files\Lumina\Lumina.exe");           // twice is the same
        StartWithWindows.Set(true, @"D:\Moved\Lumina.exe");                          // a moved install rewrites the path
        Assert.Equal("\"D:\\Moved\\Lumina.exe\" --background", StartWithWindows.Stored());
        StartWithWindows.Set(false);
        Assert.False(StartWithWindows.IsOn());
        Assert.Null(StartWithWindows.Stored());
        StartWithWindows.Set(false);                                                   // off when already off is harmless
    }

    [Fact]
    public void ItNeverTouchesTheRealRunKeyWhileTheTestBranchIsSet()
    {
        using var real = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var before = real?.GetValue("Lumina");
        StartWithWindows.Set(true, @"C:\x\Lumina.exe");
        using var after = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        Assert.Equal(before, after?.GetValue("Lumina"));
        StartWithWindows.Set(false);
    }

    [Fact]
    public void OnlyTheBackgroundFlagIsAddedAndTheSettingsAreOffByDefault()
    {
        Assert.Equal("\"C:\\a b\\Lumina.exe\" --background", StartWithWindows.CommandFor(@"C:\a b\Lumina.exe"));
        Assert.Equal("false", Catalog.Find("general.startWithWindows")!.Default);
        Assert.Equal("false", Catalog.Find("general.tray")!.Default);
    }
}

public class BackgroundLaunchTests
{
    [Fact]
    public void BackgroundIsRecognisedAnywhereAmongTheOptions()
    {
        Assert.True(LaunchArgs.Parse(new[] { "--background" }).Background);
        var a = LaunchArgs.Parse(new[] { "--paused", "--background", "movie.mkv" });
        Assert.True(a.Background);
        Assert.True(a.Paused);
        Assert.Single(a.Paths);
        Assert.False(LaunchArgs.Parse(new[] { "movie.mkv" }).Background);
        Assert.Empty(LaunchArgs.Parse(new[] { "--background" }).Problems!);
    }

    [Fact]
    public void AForwardedLaunchNeverCarriesBackgroundSoTheRunningWindowIsNotHidden()
    {
        var args = LaunchArgs.Parse(new[] { "--background", "movie.mkv" }).ToArgs();
        Assert.DoesNotContain("--background", args);
    }
}

/// <summary>What the player asks the app for and shows back.</summary>
public class SleepControllerTests
{
    static bool WaitFor(Func<bool> ok) { var until = DateTime.UtcNow.AddSeconds(5); while (DateTime.UtcNow < until) { if (ok()) return true; Thread.Sleep(30); } return false; }

    [Fact]
    public void TheButtonAsksTheAppToCycleAndATypedLengthAsksForThatLength()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        m.Command("script-message", "lumina-do", "sleep-cycle");
        m.Command("script-message", "lumina-do", "sleep", "45");
        m.Command("script-message", "lumina-do", "sleep", "eof");
        Assert.True(WaitFor(() => { lock (got) return got.Count(a => a.Length > 0 && a[0] == "lumina-sleep") == 3; }));
        string[][] snap; lock (got) snap = got.Where(a => a.Length > 0 && a[0] == "lumina-sleep").ToArray();
        Assert.Equal(new[] { "cycle", "45", "eof" }, snap.Select(a => a[1]).ToArray());
    }

    [Fact]
    public void TheStateTheAppSendsIsShownAndPublished()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-sleep-state", "25 min left");
        Assert.True(WaitFor(() => m.Get("user-data/lumina/sleep_label")?.Trim('"') == "25 min left"));
    }
}
