using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>Issue #16: specials filed as a show of their own belong in their show's seasons.</summary>
[Collection("db")]
public class MoveIntoShowTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-move-" + Guid.NewGuid().ToString("N"));
    readonly string _lib;

    public MoveIntoShowTests()
    {
        _lib = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(_lib);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Environment.SetEnvironmentVariable("LUMINA_ART", Path.Combine(_dir, "art"));
        Db.Init();
        Db.AddRoot(_lib);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    string Touch(params string[] parts)
    {
        var p = Path.Combine(new[] { _lib }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    static List<(string Title, int? Season, int? Episode)> Shows() =>
        Db.List().Where(i => i.Kind == "show").OrderBy(i => i.Season).ThenBy(i => i.Episode).Select(i => (i.Title, i.Season, i.Episode)).ToList();

    [Fact]
    public void SpecialsAndOvasNamedAfterAShowJoinItsSpecials()
    {
        Touch("One Punch Man", "Season 1", "One Punch Man S01E01.mkv");
        Touch("One Punch Man Specials", "One Punch Man Specials E01.mkv");
        Touch("One Punch Man Specials", "One Punch Man Specials E02.mkv");
        Touch("Mob Psycho 100 OVA E01.mkv");                 // no show of that name here: left alone
        Scanner.ScanAll();

        var s = Shows();
        Assert.Contains(("One Punch Man", (int?)0, (int?)1), s);
        Assert.Contains(("One Punch Man", (int?)0, (int?)2), s);
        Assert.Contains(("Mob Psycho 100 OVA", (int?)1, (int?)1), s);
        Assert.DoesNotContain(s, x => x.Title == "One Punch Man Specials");
    }

    [Fact]
    public void MovedEpisodesStayMovedUntilUndone()
    {
        Touch("Show A", "Show A S01E01.mkv");
        Touch("Show A", "Show A S01E02.mkv");
        var odd1 = Touch("Odd Bits", "Odd Bits 01.mkv");
        var odd2 = Touch("Odd Bits", "Odd Bits 02.mkv");
        Touch("Odd Bits", "Odd Bits 03.mkv");
        Scanner.ScanAll();
        Assert.Contains(Shows(), x => x.Title == "Odd Bits");

        Db.SetShowOverride(new[] { odd1, odd2 }, "Show A", null, 0);
        Scanner.ScanAll();
        var s = Shows();
        Assert.Contains(("Show A", (int?)0, (int?)1), s);
        Assert.Contains(("Show A", (int?)0, (int?)2), s);
        Assert.Single(s, x => x.Title == "Odd Bits");        // the third file was not moved

        Scanner.ScanAll();                                    // and a plain rescan keeps the move
        Assert.Equal(2, Shows().Count(x => x.Title == "Show A" && x.Season == 0));

        Db.SetShowOverride(new[] { odd1, odd2 }, null, null, 0);
        Scanner.ScanAll();
        Assert.Equal(3, Shows().Count(x => x.Title == "Odd Bits"));
        Assert.Empty(Db.ShowOverrides());
    }

    [Fact]
    public void MovedEpisodesTakeTheShowsMatchWithoutAskingAgain()
    {
        Touch("Show A", "Show A S01E01.mkv");
        var odd = Touch("Odd Bits", "Odd Bits E01.mkv");
        Scanner.ScanAll();
        Db.SetMatch("show", "Show A", null, new Meta("show", 42, "Show A", "", null, null, 7.0, 2010));

        Db.SetShowOverride(new[] { odd }, "Show A", null, 0);
        Scanner.ScanAll();
        Assert.Equal(42, Db.List().Single(i => i.Path == odd).TmdbId);
        Assert.Single(LibraryEntry.Build(Db.List()));
    }
}
