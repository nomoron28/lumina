using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ControllerLuaTests
{
    static string? Wait(Mpv m, string prop, string want)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (v == want) break; Thread.Sleep(40); }
        return v;
    }

    [Fact]
    public void TheControllerStartsWithTheStandardButtons()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Assert.Equal("cc+audio+speed+queue+chap+snap+gear", Wait(m, "controls", "cc+audio+speed+queue+chap+snap+gear"));
    }

    [Fact]
    public void ASavedLayoutReachesTheControllerAtStart()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv", "script-opts=lumina-osc-controls=speed+cc+gear");
        Assert.Equal("speed+cc+gear", Wait(m, "controls", "speed+cc+gear"));
    }

    [Fact]
    public void TheControllerSurvivesBadLayoutsAndAlwaysKeepsTheGear()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-controls", "speed+cc");
        Assert.Equal("speed+cc+gear", Wait(m, "controls", "speed+cc+gear"));
        m.Command("script-message", "lumina-controls", "nonsense");
        Assert.Equal("cc+audio+speed+queue+chap+snap+gear", Wait(m, "controls", "cc+audio+speed+queue+chap+snap+gear"));
        m.Command("script-message", "lumina-controls", "cc+cc+snap+gear+x");
        Assert.Equal("cc+snap+gear", Wait(m, "controls", "cc+snap+gear"));
    }

    [Fact]
    public void HideUiTogglesAndClosesOpenPanels()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Assert.Equal("off", Wait(m, "hide_ui", "off"));
        m.Command("script-message", "lumina-open-menu", "speed");
        Assert.Equal("speed", Wait(m, "menu", "speed"));
        m.Command("script-message", "lumina-do", "hide-ui");
        Assert.Equal("on", Wait(m, "hide_ui", "on"));
        Assert.Equal("", Wait(m, "menu", ""));                 // nothing stays open behind a hidden controller
        m.Command("script-message", "lumina-do", "hide-ui");
        Assert.Equal("off", Wait(m, "hide_ui", "off"));
    }

    [Fact]
    public void TheHideKeyIsBoundByDefaultAndHasNoClash()
    {
        var a = Shortcuts.Find("hideui")!;
        Assert.Equal("script-message lumina-do hide-ui", a.Command);
        Assert.Empty(Shortcuts.Conflicts(new Dictionary<string, string>()));
        var shipped = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "input.conf"));
        Assert.Contains("h script-message lumina-do hide-ui", shipped);   // the default key has to exist in the file mpv reads
    }
}
