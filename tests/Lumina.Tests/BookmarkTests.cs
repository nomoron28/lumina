using System.IO;
using Lumina.Library;
using Lumina.Player;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class BookmarkDbTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-bm-" + Guid.NewGuid().ToString("N"));

    public BookmarkDbTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Db.Init();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    [Fact]
    public void BookmarksComeBackInVideoOrderWithNumberedLabels()
    {
        Db.ToggleBookmark("a.mkv", 300);
        Db.ToggleBookmark("a.mkv", 20);
        var (added, all) = Db.ToggleBookmark("a.mkv", 100);
        Assert.True(added);
        Assert.Equal(new[] { 20.0, 100.0, 300.0 }, all.Select(b => b.Position));
        Assert.Equal(new[] { "Bookmark 2", "Bookmark 3", "Bookmark 1" }, all.Select(b => b.Label));    // numbered in the order they were made
    }

    [Fact]
    public void PressingAgainAtTheSameSpotRemovesItButAFurtherSpotAddsAnother()
    {
        Db.ToggleBookmark("a.mkv", 100);
        var (added, all) = Db.ToggleBookmark("a.mkv", 101.5);          // within two seconds: that is the same place
        Assert.False(added);
        Assert.Empty(all);
        Db.ToggleBookmark("a.mkv", 100);
        var (added2, all2) = Db.ToggleBookmark("a.mkv", 103);          // three seconds on: a new one
        Assert.True(added2);
        Assert.Equal(2, all2.Count);
    }

    [Fact]
    public void RemovingPicksTheClosestWhenTwoAreNear()
    {
        Db.ToggleBookmark("a.mkv", 100);
        Db.ToggleBookmark("a.mkv", 103);
        var (added, all) = Db.ToggleBookmark("a.mkv", 102.2);          // nearer to 103 (within 2 s of both)
        Assert.False(added);
        Assert.Equal(new[] { 100.0 }, all.Select(b => b.Position));
    }

    [Fact]
    public void FilesDoNotShareBookmarksAndClearOnlyAffectsOne()
    {
        Db.ToggleBookmark("a.mkv", 10);
        Db.ToggleBookmark("b.mkv", 10);
        Db.ToggleBookmark("b.mkv", 50);
        Assert.Single(Db.Bookmarks("a.mkv"));
        Db.ClearBookmarks("b.mkv");
        Assert.Empty(Db.Bookmarks("b.mkv"));
        Assert.Single(Db.Bookmarks("a.mkv"));
        Db.ClearBookmarks("never-seen.mkv");                              // harmless
    }

    [Fact]
    public void ANegativeTimeIsTreatedAsTheStartAndPathsWithQuotesAreSafe()
    {
        var (_, all) = Db.ToggleBookmark(@"C:\Movies\It's a ""test"".mkv", -5);
        Assert.Equal(0, Assert.Single(all).Position);
        Assert.Single(Db.Bookmarks(@"C:\Movies\It's a ""test"".mkv"));
    }

    [Fact]
    public void TheSchemaIsAtLeastVersionFiveAndTheTableExists()
    {
        Assert.True(Db.SchemaVersion() >= 5);
        Assert.Empty(Db.Bookmarks("anything"));
    }
}

/// <summary>What the controller does with the list the host sends.</summary>
public class BookmarkControllerTests
{
    static string? Wait(Mpv m, string prop, string want)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        string? v = null;
        while (DateTime.UtcNow < until) { v = m.Get("user-data/lumina/" + prop)?.Trim('"'); if (v == want) break; Thread.Sleep(40); }
        return v;
    }

    [Fact]
    public void TheControllerCountsTheListItsGivenAndIgnoresJunkEntries()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-bookmarks", "[{\"t\":5.5,\"label\":\"A\"},{\"t\":\"x\"},{\"label\":\"no time\"},{\"t\":2,\"label\":\"B\"},7,null]");
        Assert.Equal("2", Wait(m, "bookmark_count", "2"));
        m.Command("script-message", "lumina-bookmarks", "not json");
        Assert.Equal("0", Wait(m, "bookmark_count", "0"));
        m.Command("script-message", "lumina-bookmarks", "[]");
        Assert.Equal("0", Wait(m, "bookmark_count", "0"));
    }

    [Fact]
    public void TheMenuListsThemInOrderWithTheTimeThenOffersToAddAndRemove()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-bookmarks", "[{\"t\":8,\"label\":\"Late\"},{\"t\":2,\"label\":\"Early\"}]");
        Assert.Equal("2", Wait(m, "bookmark_count", "2"));
        m.Command("script-message", "lumina-do", "bookmarks");
        Assert.Equal("bookmarks", Wait(m, "menu", "bookmarks"));
        Assert.Equal("Early|Late|Add a bookmark here|Remove all bookmarks", Wait(m, "menu_items", "Early|Late|Add a bookmark here|Remove all bookmarks"));
    }

    [Fact]
    public void WithNoneTheMenuOnlyOffersToAdd()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-do", "bookmarks");
        Assert.Equal("Add a bookmark here", Wait(m, "menu_items", "Add a bookmark here"));
    }

    [Fact]
    public void ChoosingAnItemSeeksToThatTime()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-bookmarks", "[{\"t\":6,\"label\":\"Here\"}]");
        Assert.Equal("1", Wait(m, "bookmark_count", "1"));
        m.Command("seek", "6", "absolute+exact");                          // the menu runs this same command for its item
        var until = DateTime.UtcNow.AddSeconds(5);
        double pos = 0;
        while (DateTime.UtcNow < until && Math.Abs(pos - 6) > 0.3) { double.TryParse(m.Get("time-pos"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pos); Thread.Sleep(50); }
        Assert.InRange(pos, 5.7, 6.3);
    }

    [Fact]
    public void TheKeyIsBoundByDefaultInTheFileMpvReadsAndClashesWithNothing()
    {
        var a = Shortcuts.Find("bookmark")!;
        Assert.Equal("script-message lumina-bookmark", a.Command);
        Assert.Empty(Shortcuts.Conflicts(new Dictionary<string, string>()));
        var shipped = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "input.conf"));
        Assert.Contains("y script-message lumina-bookmark", shipped);
    }
}
