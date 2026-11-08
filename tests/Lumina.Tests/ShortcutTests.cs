using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ShortcutTests
{
    static readonly Dictionary<string, string> None = new();

    [Fact]
    public void DefaultsAreUniqueSoNoKeyStartsOutDoingTwoThings()
    {
        Assert.Empty(Shortcuts.Conflicts(None));
        Assert.Equal(Shortcuts.Actions.Count, Shortcuts.Actions.Select(a => a.Id).Distinct().Count());
        Assert.All(Shortcuts.Actions.SelectMany(a => a.DefaultKeys), k => Assert.True(Shortcuts.IsValidKey(k), k));
    }

    [Fact]
    public void EveryDefaultMatchesWhatTheShippedInputConfAndMpvActuallyBind()
    {
        // input.conf binds b, f, F11, ESC, N, P: they must agree with the table or Settings would show the wrong key.
        var conf = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "input.conf")).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        foreach (var line in conf.Where(l => !l.StartsWith("NEXT") && !l.StartsWith("PREV")))
        {
            var key = line.Split(' ')[0];
            var action = Shortcuts.Actions.FirstOrDefault(a => a.DefaultKeys.Contains(key) && line.Contains(a.Command));
            Assert.NotNull(action);
        }
    }

    [Theory]
    [InlineData("k", true)]
    [InlineData("K", true)]
    [InlineData("SPACE", true)]
    [InlineData("space", true)]
    [InlineData("Ctrl+s", true)]
    [InlineData("Ctrl+Shift+LEFT", true)]
    [InlineData("F5", true)]
    [InlineData("F24", true)]
    [InlineData("PGDWN", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    [InlineData("two words", false)]
    [InlineData("F25", false)]
    [InlineData("Fx", false)]
    [InlineData("NOSUCHKEY", false)]
    [InlineData("Ctrl+", false)]
    public void KeyNamesAreValidatedBeforeTheyReachMpv(string? key, bool ok) => Assert.Equal(ok, Shortcuts.IsValidKey(key));

    [Fact]
    public void OverridesParseLeniently()
    {
        var o = Shortcuts.ParseOverrides("{\"playpause\":\"k\",\"nonsense\":\"x\",\"mute\":\"bad key\",\"quit\":5}");
        Assert.Equal(new[] { "playpause" }, o.Keys);
        Assert.Empty(Shortcuts.ParseOverrides("garbage"));
        Assert.Empty(Shortcuts.ParseOverrides("[1,2]"));
        Assert.Empty(Shortcuts.ParseOverrides(null));
        Assert.Equal("", Shortcuts.SerializeOverrides(None));
        Assert.Equal(o, Shortcuts.ParseOverrides(Shortcuts.SerializeOverrides(o)));
    }

    [Fact]
    public void KeyUsersAreFoundForDefaultsAndOverrides()
    {
        Assert.Equal("mute", Assert.Single(Shortcuts.UsersOf("m", None)).Id);
        Assert.Equal("fullscreen", Assert.Single(Shortcuts.UsersOf("F11", None)).Id);          // a second default key counts
        Assert.Equal("fullscreen", Assert.Single(Shortcuts.UsersOf("f11", None)).Id);          // names ignore case
        Assert.Empty(Shortcuts.UsersOf("M", None));                                            // single letters do not
        var o = new Dictionary<string, string> { ["quit"] = "m" };
        Assert.Equal(new[] { "mute", "quit" }, Shortcuts.UsersOf("m", o).Select(a => a.Id).OrderBy(x => x));
        Assert.Equal("mute", Assert.Single(Shortcuts.UsersOf("m", o, except: "quit")).Id);
    }

    [Fact]
    public void AssigningAFreeKeyJustMovesTheAction()
    {
        var o = Shortcuts.Assign(None, "playpause", "k", out var displaced);
        Assert.Null(displaced);
        Assert.Equal("k", o["playpause"]);
        Assert.Empty(Shortcuts.Conflicts(o));
    }

    [Fact]
    public void AssigningATakenKeySwapsSoNobodyIsLeftWithoutAKey()
    {
        var o = Shortcuts.Assign(None, "playpause", "m", out var displaced);                  // m belongs to mute
        Assert.Equal("mute", displaced!.Id);
        Assert.Equal("m", o["playpause"]);
        Assert.Equal("SPACE", o["mute"]);                                                       // mute got play/pause's old key
        Assert.Empty(Shortcuts.Conflicts(o));
        Assert.All(Shortcuts.Actions, a => Assert.NotEmpty(Shortcuts.KeysFor(a, o)));
    }

    [Fact]
    public void ReassigningADefaultKeyDropsTheOverrideSoResetStaysClean()
    {
        var o = Shortcuts.Assign(None, "playpause", "k", out _);
        o = Shortcuts.Assign(o, "playpause", "SPACE", out _);
        Assert.DoesNotContain("playpause", o.Keys);
        Assert.Empty(o);
    }

    [Fact]
    public void ASwapThatRestoresADefaultIsStoredAsNoOverrideAtAll()
    {
        var o = Shortcuts.Assign(None, "playpause", "m", out _);                               // swap play/pause and mute
        o = Shortcuts.Assign(o, "playpause", "SPACE", out var d);                              // swap back
        Assert.Equal("mute", d!.Id);
        Assert.Empty(o);
    }

    [Fact]
    public void ResetAfterASwapRestoresBothActionsWithoutAClash()
    {
        var o = Shortcuts.Assign(None, "playpause", "m", out _);                               // playpause=m, mute=SPACE
        var r = Shortcuts.Reset(o, "playpause");
        Assert.Empty(Shortcuts.Conflicts(r));
        Assert.Empty(r);                                                                        // the swap was undone, so no overrides remain
    }

    [Fact]
    public void ResetGivesTheTakerTheKeyBeingVacated()
    {
        var o = Shortcuts.Assign(None, "playpause", "k", out _);                               // playpause=k
        o = Shortcuts.Assign(o, "mute", "k", out var d);                                        // mute takes k, playpause gets m
        Assert.Equal("playpause", d!.Id);
        var r = Shortcuts.Reset(o, "mute");                                                     // mute back to m, so playpause must leave m
        Assert.Empty(Shortcuts.Conflicts(r));
        Assert.Equal("k", r["playpause"]);
        Assert.DoesNotContain("mute", r.Keys);
    }

    [Fact]
    public void ResettingAnUntouchedActionChangesNothing() => Assert.Empty(Shortcuts.Reset(None, "mute"));

    [Fact]
    public void ResettingAnActionWithTwoDefaultKeysRestoresBoth()
    {
        var o = Shortcuts.Assign(None, "fullscreen", "g", out _);
        var r = Shortcuts.Reset(o, "fullscreen");
        Assert.Empty(r);
        Assert.Equal(new[] { "f", "F11" }, Shortcuts.KeysFor(Shortcuts.Find("fullscreen")!, r));
    }

    [Fact]
    public void CtrlCombinationsStayReadableInTheStoredText() =>
        Assert.Contains("Ctrl+s", Shortcuts.SerializeOverrides(new Dictionary<string, string> { ["snapshot"] = "Ctrl+s" }));

    [Fact]
    public void HandEditedClashesAreDetected()
    {
        var o = new Dictionary<string, string> { ["quit"] = "m" };                              // mute still has m
        var c = Assert.Single(Shortcuts.Conflicts(o));
        Assert.Contains("Mute", c);
        Assert.Contains("Close the video", c);
    }

    [Fact]
    public void GeneratedLinesUnbindFreedDefaultsAndBindTheNewKeys()
    {
        var g = Shortcuts.Generate(new Dictionary<string, string> { ["playpause"] = "k", ["skipfwd"] = "l" });
        Assert.Contains("SPACE ignore", g);
        Assert.Contains("k cycle pause", g);
        Assert.Contains("RIGHT ignore", g);
        Assert.Contains("l repeatable script-message lumina-skip 1", g);                        // holding it keeps skipping
        Assert.DoesNotContain("LEFT", g);                                                       // untouched actions are not written
        Assert.StartsWith("#", g);
    }

    [Fact]
    public void ASwapDoesNotUnbindAKeyAnotherActionJustTook()
    {
        var o = Shortcuts.Assign(None, "playpause", "m", out _);
        var g = Shortcuts.Generate(o);
        Assert.DoesNotContain("m ignore", g);                                                   // m is now play/pause
        Assert.DoesNotContain("SPACE ignore", g);                                               // SPACE is now mute
        Assert.Contains("m cycle pause", g);
        Assert.Contains("SPACE cycle mute", g);
    }

    [Fact]
    public void NoOverridesMeansAnEmptyFileBody() => Assert.DoesNotContain(" ", Shortcuts.Generate(None).Split('\n').Skip(1).FirstOrDefault() ?? "");

    [Theory]
    [InlineData("SPACE", "Space")]
    [InlineData("LEFT", "Left")]
    [InlineData("m", "m")]
    [InlineData("SHARP", "#")]
    [InlineData("PGDWN", "Page Down")]
    [InlineData("F11", "F11")]
    [InlineData("Ctrl+s", "Ctrl+s")]
    [InlineData("Ctrl+Shift+LEFT", "Ctrl+Shift+Left")]
    public void KeysReadNaturallyOnTheChip(string key, string shown) => Assert.Equal(shown, Shortcuts.Display(key));
}

public class ShortcutHeadlessTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-keys-" + Guid.NewGuid().ToString("N"));
    public ShortcutHeadlessTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    Mpv Open(Dictionary<string, string> overrides)
    {
        var conf = Path.Combine(_dir, "input.generated.conf");
        File.WriteAllText(conf, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "input.conf")) + "\n" + Shortcuts.Generate(overrides));
        return Fixtures.Open("multi.mkv", "input-conf=" + conf);
    }

    [Fact]
    public void ARebindMovesPlayPauseToTheNewKeyAndFreesTheOldOne()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open(new Dictionary<string, string> { ["playpause"] = "k" });       // starts paused
        m.Command("keypress", "SPACE"); Thread.Sleep(300);
        Assert.Equal("yes", m.Get("pause"));                                                // the old key does nothing now
        m.Command("keypress", "k"); Thread.Sleep(300);
        Assert.Equal("no", m.Get("pause"));
        m.Command("keypress", "k"); Thread.Sleep(300);
        Assert.Equal("yes", m.Get("pause"));
    }

    [Fact]
    public void ASwapMakesBothKeysDoTheOtherThing()
    {
        if (!Fixtures.HasMpv) return;
        var o = Shortcuts.Assign(new Dictionary<string, string>(), "playpause", "m", out _);
        using var m = Open(o);
        m.Command("keypress", "m"); Thread.Sleep(300);
        Assert.Equal("no", m.Get("pause"));                                                 // m plays now
        m.Command("keypress", "SPACE"); Thread.Sleep(300);
        Assert.Equal("yes", m.Get("mute"));                                                 // space mutes now
    }

    [Fact]
    public void WithoutOverridesTheShippedKeysStillWork()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open(new Dictionary<string, string>());
        m.Command("keypress", "SPACE"); Thread.Sleep(300);
        Assert.Equal("no", m.Get("pause"));
    }

    [Fact]
    public void SkipKeysGoThroughTheControllerSoTheSkipSettingStillApplies()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open(new Dictionary<string, string> { ["skipfwd"] = "l" });
        m.Set("pause", "yes");
        var before = double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture);
        m.Command("keypress", "l"); Thread.Sleep(500);
        var after = double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(after - before, 9, 11);                                              // opt.skip is 10 by default
        m.Command("keypress", "RIGHT"); Thread.Sleep(400);                                  // the freed key no longer skips
        Assert.InRange(double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture) - after, -0.5, 0.5);
    }

    [Fact]
    public void ARepeatablePrefixSurvivesLiveRebinding()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open(new Dictionary<string, string>());
        m.Set("pause", "yes");
        m.Command("keybind", "l", "repeatable script-message lumina-skip 1");
        var before = double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture);
        m.Command("keypress", "l"); Thread.Sleep(500);
        Assert.InRange(double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture) - before, 9, 11);
    }

    [Fact]
    public void KeysAppliedLiveWithKeybindWork()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Open(new Dictionary<string, string>());
        m.Command("keybind", "g", "cycle pause");
        m.Command("keypress", "g"); Thread.Sleep(300);
        Assert.Equal("no", m.Get("pause"));
    }
}
