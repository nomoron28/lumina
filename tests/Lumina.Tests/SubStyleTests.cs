using System.IO;
using Lumina.Player;
using Lumina.Subtitles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

public class SubStyleValueTests
{
    [Fact]
    public void ValuesAreHeldToWhatThePlayerAcceptsAndRounded()
    {
        Assert.Equal(new SubStyle(600, 3, 100), SubStyle.Clamped(9999, 99, 500));
        Assert.Equal(new SubStyle(-600, 0.3, 0), SubStyle.Clamped(-9999, 0, -5));
        Assert.Equal(new SubStyle(0.3, 1.1, 95), SubStyle.Clamped(0.30000000004, 1.0999999, 94.6));
        Assert.Equal(new SubStyle(0, 1, 100), SubStyle.Clamped(double.NaN, double.PositiveInfinity, double.NaN));
    }

    [Theory]
    [InlineData(0, 1, 100, 100, true)]
    [InlineData(0, 1, 90, 90, true)]            // the person's own default height is the normal look
    [InlineData(0.1, 1, 100, 100, false)]
    [InlineData(0, 1.1, 100, 100, false)]
    [InlineData(0, 1, 95, 100, false)]
    [InlineData(0.0001, 1, 100, 100, true)]     // too small to matter
    public void ANormalLookIsNotWorthRemembering(double delay, double scale, double pos, double defaultPos, bool isDefault) =>
        Assert.Equal(isDefault, new SubStyle(delay, scale, pos).IsDefault(defaultPos));

    [Fact]
    public void JsonRoundTripsAndAnythingElseIsNull()
    {
        var s = new SubStyle(-1.5, 1.2, 85);
        Assert.Equal(s, SubStyle.FromJson(s.ToJson()));
        foreach (var junk in new[] { null, "", "   ", "not json", "{}", "[]", "{\"delay\":1}", "{\"delay\":\"x\",\"scale\":1,\"pos\":1}", "null" })
            Assert.Null(SubStyle.FromJson(junk));
        Assert.Equal(new SubStyle(600, 3, 100), SubStyle.FromJson("{\"delay\":1e9,\"scale\":50,\"pos\":999}"));      // a hand-edited value is brought into range
    }

    [Theory]
    [InlineData("0.100", "1.200", "90", true)]
    [InlineData("0.1", "x", "90", false)]
    [InlineData("", "1", "90", false)]
    [InlineData(null, null, null, false)]
    public void MessagesFromThePlayerAreParsedOrIgnored(string? d, string? s, string? p, bool ok) => Assert.Equal(ok, SubStyle.Parse(d, s, p) != null);

    [Fact]
    public void NumbersAreReadTheSameWhateverTheComputersLanguage()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(new SubStyle(0.1, 1.2, 90), SubStyle.Parse("0.100", "1.200", "90"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}

[Collection("db")]
public class SubStyleStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-substyle-" + Guid.NewGuid().ToString("N"));

    public SubStyleStoreTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Lumina.Library.Db.Init();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    [Fact]
    public void AVideosLookIsRememberedAndAnotherVideoIsNotAffected()
    {
        SubStyleStore.Save(@"C:\v\a.mkv", new SubStyle(0.5, 1.3, 90), 100);
        Assert.Equal(new SubStyle(0.5, 1.3, 90), SubStyleStore.Load(@"C:\v\a.mkv"));
        Assert.Null(SubStyleStore.Load(@"C:\v\b.mkv"));
    }

    [Fact]
    public void GoingBackToTheNormalLookForgetsTheEntry()
    {
        SubStyleStore.Save(@"C:\v\a.mkv", new SubStyle(0.5, 1, 100), 100);
        Assert.NotNull(SubStyleStore.Load(@"C:\v\a.mkv"));
        SubStyleStore.Save(@"C:\v\a.mkv", new SubStyle(0, 1, 100), 100);
        Assert.Null(SubStyleStore.Load(@"C:\v\a.mkv"));
    }

    [Fact]
    public void TheSameFileSpelledDifferentlyIsTheSameEntry()
    {
        SubStyleStore.Save(@"C:\Videos\Show\ep1.mkv", new SubStyle(2, 1, 100), 100);
        Assert.Equal(2, SubStyleStore.Load(@"c:/videos/SHOW/ep1.mkv")!.Delay);
        Assert.Equal(2, SubStyleStore.Load(@"C:\Videos\Show\..\Show\ep1.mkv")!.Delay);
    }

    [Fact]
    public void AMoreRecentSaveReplacesTheOldOne()
    {
        SubStyleStore.Save(@"C:\v\a.mkv", new SubStyle(1, 1, 100), 100);
        SubStyleStore.Save(@"C:\v\a.mkv", new SubStyle(-1, 1.5, 80), 100);
        Assert.Equal(new SubStyle(-1, 1.5, 80), SubStyleStore.Load(@"C:\v\a.mkv"));
    }

    [Fact]
    public void ADamagedEntryReadsAsNothingNotACrash()
    {
        SubStyleStore.Save(@"C:\v\a.mkv", new SubStyle(1, 1, 100), 100);
        using var c = new SqliteConnection("Data Source=" + Path.Combine(_dir, "t.db"));
        c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE kv SET value='{{broken' WHERE key LIKE 'substyle:%'"; cmd.ExecuteNonQuery();
        Assert.Null(SubStyleStore.Load(@"C:\v\a.mkv"));
    }
}

public class SubStyleControllerTests
{
    static string? Wait(Mpv m, string prop, Func<string?, bool> ok)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (ok(v)) break; Thread.Sleep(40); }
        return v;
    }

    [Fact]
    public void EachChangeChangesThePropertyAndTellsTheAppTheNewLook()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        m.Command("script-message", "lumina-do", "sub-size", "0.1");
        m.Command("script-message", "lumina-do", "sub-pos", "-5");
        m.Command("script-message", "lumina-do", "sub-delay", "0.2");
        var published = Wait(m, "substyle", v => v != null && v.StartsWith("0.200|"));
        Assert.Equal("0.200|1.100|95", published);                                        // the height started at 100 and moved up by 5
        Assert.Equal(1.1, double.Parse(m.Get("sub-scale")!, System.Globalization.CultureInfo.InvariantCulture), 3);
        string[][] msgs; lock (got) msgs = got.Where(a => a.Length > 0 && a[0] == "lumina-substyle").ToArray();
        Assert.Equal(3, msgs.Length);
        Assert.Equal(new[] { "lumina-substyle", "0.200", "1.100" }, msgs[2].Take(3).ToArray());
    }

    [Fact]
    public void TextSizeAndPositionStayInRange()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        for (var i = 0; i < 40; i++) m.Command("script-message", "lumina-do", "sub-size", "0.5");
        for (var i = 0; i < 40; i++) m.Command("script-message", "lumina-do", "sub-pos", "-50");
        Thread.Sleep(500);
        Assert.Equal(3.0, double.Parse(m.Get("sub-scale")!, System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(0, double.Parse(m.Get("sub-pos")!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AStoredLookIsAppliedWithoutEchoingItBackAsAChange()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        m.Command("script-message", "lumina-substyle-apply", "1.5", "1.4", "80");
        Assert.Equal("1.500|1.400|80", Wait(m, "substyle", v => v == "1.500|1.400|80"));
        Assert.Equal(1.5, double.Parse(m.Get("sub-delay")!, System.Globalization.CultureInfo.InvariantCulture), 3);
        Thread.Sleep(300);
        lock (got) Assert.DoesNotContain(got, a => a.Length > 0 && a[0] == "lumina-substyle");
    }

    [Fact]
    public void AGarbledApplyMessageChangesNothing()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-substyle-apply", "x", "y", "z");
        var published = Wait(m, "substyle", v => !string.IsNullOrEmpty(v)) ?? "";
        Assert.StartsWith("0.000|1.000|", published);                                      // nothing was changed
    }

    [Fact]
    public void ResetEverythingTellsTheAppTheLookIsNormalAgain()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-do", "sub-delay", "1");
        Assert.NotNull(Wait(m, "substyle", v => v != null && v.StartsWith("1.000|")));
        m.Command("script-message", "lumina-do", "reset-all");
        Assert.NotNull(Wait(m, "substyle", v => v != null && v.StartsWith("0.000|")));
    }

    [Fact]
    public void TheSubtitleMenuOffersAMatchPickerAndTheLookControls()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-open-menu", "cc");
        var labels = Wait(m, "menu_items", v => !string.IsNullOrEmpty(v)) ?? "";
        foreach (var expected in new[] { "Search OpenSubtitles", "Choose from all matches...", "Larger text", "Smaller text", "Move up", "Move down", "Show later (+0.1 s)", "Show earlier (-0.1 s)" })
            Assert.Contains(expected, labels.Split('|'));
    }

    [Fact]
    public void ThePickerRequestReachesTheApp()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        var got = new List<string[]>();
        m.ClientMessage += a => { lock (got) got.Add(a); };
        m.Command("script-message", "lumina-open-menu", "cc");
        Wait(m, "menu_items", v => !string.IsNullOrEmpty(v));
        // choosing the entry runs this same message
        m.Command("script-message", "lumina-subs-pick");
        Thread.Sleep(300);
        lock (got) Assert.Contains(got, a => a.Length > 0 && a[0] == "lumina-subs-pick");
    }
}
