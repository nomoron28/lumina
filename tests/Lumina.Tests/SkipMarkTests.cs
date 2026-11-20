using System.IO;
using Lumina.Library;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

/// <summary>Skip intro, recap and credits (0.3): where they are and what the controller does with them.</summary>
public class SkipMarkTests
{
    [Theory]
    [InlineData("Opening", "intro")]
    [InlineData("Intro", "intro")]
    [InlineData("OP", "intro")]
    [InlineData("Opening Credits", "intro")]
    [InlineData("Previously on", "recap")]
    [InlineData("Recap", "recap")]
    [InlineData("Ending", "credits")]
    [InlineData("End Credits", "credits")]
    [InlineData("ED", "credits")]
    [InlineData("Credits", "credits")]
    [InlineData("Chapter 1", null)]
    [InlineData("The Opening of the Box", null)]   // a story chapter that happens to start with the word
    [InlineData("Epilogue", null)]
    public void NamesChapters(string title, string? kind) => Assert.Equal(kind, SkipMarks.KindOfChapter(title));

    [Fact]
    public void AChapterRunsToTheNextOrToTheEnd()
    {
        var chapters = new List<(string, double)> { ("Recap", 0), ("Opening", 60), ("Part A", 150), ("Ending", 1300) };
        Assert.Equal(new[] { new Segment("recap", 0, 60), new Segment("intro", 60, 150), new Segment("credits", 1300, 1420) },
            SkipMarks.FromChapters(chapters, 1420));
    }

    [Fact]
    public void TheFilesOwnMarkBeatsTheSeasonsWhichBeatsTheChapters()
    {
        var chapters = new[] { new Segment("intro", 0, 90), new Segment("credits", 1300, 1420) };
        var season = new[] { new SkipMark("intro", 30, 120), new SkipMark("recap", 0, 25) };
        var own = new[] { new SkipMark("intro", 40, 125) };
        var got = SkipMarks.Combine(own, season, chapters, 1420);
        Assert.Equal(new[] { new Segment("recap", 0, 25), new Segment("intro", 40, 125), new Segment("credits", 1300, 1420) }, got);
    }

    [Fact]
    public void MarksNeedBothEdgesExceptCreditsWhichRunToTheEnd()
    {
        Assert.Null(SkipMarks.FromMark(new SkipMark("intro", 30, null), 1400));
        Assert.Null(SkipMarks.FromMark(new SkipMark("intro", 30, 20), 1400));
        Assert.Null(SkipMarks.FromMark(new SkipMark("intro", 1500, 1600), 1400));   // a season mark past the end of a shorter episode
        Assert.Equal(new Segment("credits", 1300, 1400), SkipMarks.FromMark(new SkipMark("credits", 1300, null), 1400));
        Assert.Equal(new Segment("intro", 30, 1400), SkipMarks.FromMark(new SkipMark("intro", 30, 2000), 1400));
    }

    [Fact]
    public void UpNextComesAtTheCreditsOrHalfAMinuteBeforeTheEnd()
    {
        Assert.Equal(1300, SkipMarks.UpNextAt(new[] { new Segment("intro", 0, 90), new Segment("credits", 1300, 1420) }, 1420));
        Assert.Equal(1390, SkipMarks.UpNextAt(new[] { new Segment("intro", 0, 90) }, 1420));
        Assert.Equal(1390, SkipMarks.UpNextAt(new[] { new Segment("credits", 200, 260) }, 1420));   // credits in the first half: not this file's
        Assert.Null(SkipMarks.UpNextAt(Array.Empty<Segment>(), 90));                                 // a short clip gets no card
    }

    [Fact]
    public void SeasonScopePrefersTheTmdbId()
    {
        var ep = new MediaItem { Kind = "show", Season = 2, TmdbId = 1396, FileTitle = "Breaking Bad" };
        Assert.Equal("season:tmdb:1396:2", SkipMarks.SeasonScope(ep));
        Assert.Equal("season:title:breaking bad:2", SkipMarks.SeasonScope(new MediaItem { Kind = "show", Season = 2, FileTitle = "Breaking Bad" }));
        Assert.Null(SkipMarks.SeasonScope(new MediaItem { Kind = "movie" }));
    }
}

[Collection("db")]
public class SkipMarkDbTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-skip-" + Guid.NewGuid().ToString("N"));

    public SkipMarkDbTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
        Db.Init();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void EdgesAreSetOneAtATimeAndAStartAfterTheEndStartsOver()
    {
        Db.SetMark("file:a", "intro", "start", 30);
        Db.SetMark("file:a", "intro", "end", 95);
        Assert.Equal(new SkipMark("intro", 30, 95), Assert.Single(Db.Marks("file:a")));

        Db.SetMark("file:a", "intro", "start", 100);            // past the old end: the end is cleared
        Assert.Equal(new SkipMark("intro", 100, null), Assert.Single(Db.Marks("file:a")));
        Db.SetMark("file:a", "intro", "end", 50);               // before the start: the start is cleared
        Assert.Equal(new SkipMark("intro", null, 50), Assert.Single(Db.Marks("file:a")));

        Db.SetMark("file:a", "credits", "start", 1300);
        Assert.Equal(2, Db.Marks("file:a").Count);
        Assert.Empty(Db.Marks("file:b"));
        Assert.Throws<ArgumentException>(() => Db.SetMark("file:a", "outro", "start", 1));

        Db.ClearMarks(new[] { "file:a" });
        Assert.Empty(Db.Marks("file:a"));
    }
}

/// <summary>What the controller does with the segments the host sends.</summary>
public class SkipControllerTests
{
    static string? Wait(Mpv m, string prop, string want)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (v == want) break; Thread.Sleep(40); }
        return v;
    }

    [Fact]
    public void OffersToSkipWhileInsideASegmentAndSkipsToItsEnd()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");                // its first chapter is "Opening", 0 to 4 s
        m.Set("pause", "yes");
        m.Command("seek", "1", "absolute+exact");
        m.Command("script-message", "lumina-segments", "{\"auto\":false,\"segments\":[{\"kind\":\"intro\",\"s\":0,\"e\":4},{\"kind\":\"x\"},7]}");
        Assert.Equal("intro", Wait(m, "skip", "intro"));

        m.Command("script-message", "lumina-skip-segment");
        Assert.Equal("", Wait(m, "skip", ""));
        Assert.True(double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture) >= 3.9);
    }

    [Fact]
    public void UpNextCountsDownTakesOverFromSkipCreditsAndCanBeCancelled()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Set("pause", "yes");
        m.Command("seek", "1", "absolute+exact");
        m.Command("script-message", "lumina-segments", "{\"segments\":[{\"kind\":\"credits\",\"s\":0.5,\"e\":12}],\"upnext\":{\"title\":\"S01E02 · Next\",\"at\":0.5}}");
        Assert.Equal("10", Wait(m, "upnext", "10"));            // half a second in: 10 seconds to go, rounded up
        Assert.Equal("", Wait(m, "skip", ""));                  // the card replaces Skip credits
        m.Command("seek", "4", "absolute+exact");
        Assert.Equal("7", Wait(m, "upnext", "7"));              // it counts playback time, so a pause holds it

        m.Command("script-message", "lumina-upnext", "cancel");
        Assert.Equal("", Wait(m, "upnext", ""));
        Assert.Equal("credits", Wait(m, "skip", "credits"));    // Skip credits is back for whoever still wants it
    }

    [Fact]
    public void AutoSkipJumpsPastIntrosButNeverCredits()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Set("pause", "yes");
        m.Command("seek", "1", "absolute+exact");
        m.Command("script-message", "lumina-segments", "{\"auto\":true,\"segments\":[{\"kind\":\"intro\",\"s\":0,\"e\":4},{\"kind\":\"credits\",\"s\":8,\"e\":12}]}");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && double.Parse(m.Get("time-pos") ?? "0", System.Globalization.CultureInfo.InvariantCulture) < 3.9) Thread.Sleep(40);
        Assert.True(double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture) >= 3.9);

        m.Command("seek", "9", "absolute+exact");
        Assert.Equal("credits", Wait(m, "skip", "credits"));    // offered, not taken
        Thread.Sleep(300);
        Assert.True(double.Parse(m.Get("time-pos")!, System.Globalization.CultureInfo.InvariantCulture) < 10);
    }
}
