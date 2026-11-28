using System.IO;
using System.Text.Json;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>Box sets: the groups TMDB puts films in ("The Terminator Collection"), shown next to the user's own collections.</summary>
public class BoxSetParsingTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void AFilmNamesItsBoxSet()
    {
        var d = Tmdb.ParseDetail(J("""{"belongs_to_collection":{"id":528,"name":"The Terminator Collection","poster_path":"/p.jpg","backdrop_path":"/b.jpg"}}"""), tv: false);
        Assert.Equal(new BoxSet(528, "The Terminator Collection", "/b.jpg"), d.Box);
    }

    [Theory]
    [InlineData("""{"belongs_to_collection":null}""")]
    [InlineData("""{"belongs_to_collection":{"name":"No id"}}""")]
    [InlineData("""{"belongs_to_collection":{"id":5}}""")]
    [InlineData("""{}""")]
    public void NoBoxSetWhenTmdbHasNone(string json) => Assert.Null(Tmdb.ParseDetail(J(json), tv: false).Box);

    [Fact]
    public void ShowsNeverHaveABoxSet() =>
        Assert.Null(Tmdb.ParseDetail(J("""{"belongs_to_collection":{"id":1,"name":"X"}}"""), tv: true).Box);
}

[Collection("db")]
public class BoxSetTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-box-" + Guid.NewGuid().ToString("N"));
    readonly string _lib;

    public BoxSetTests()
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

    static List<LibraryEntry> Entries() => LibraryEntry.Build(Db.List());

    /// <summary>A scanned, TMDB-matched film that belongs to box set <paramref name="box"/> (0 for none).</summary>
    void Film(string title, int year, long tmdb, long box)
    {
        File.WriteAllText(Path.Combine(_lib, $"{title} ({year}).mkv"), "x");
        Scanner.ScanAll();
        Db.SetMatch("movie", title, year, new Meta("movie", tmdb, title, "", null, null, 7, year));
        Db.SetDetail("movie", tmdb, Detail.Empty with { Box = box == 0 ? null : new BoxSet(box, "The Terminator Collection", "/b.jpg") });
    }

    [Fact]
    public void TwoOwnedFilmsMakeABoxSetTileInReleaseOrder()
    {
        Film("Terminator 2", 1991, 280, 528);
        Film("The Terminator", 1984, 218, 528);

        var box = Assert.Single(Db.BoxSets());
        Assert.Equal((-528L, "The Terminator Collection", true), (box.Id, box.Name, box.BoxSet));
        Assert.Equal(new[] { "The Terminator", "Terminator 2" }, Collections.Members(Entries(), box).Select(e => e.Title));

        var tile = Assert.Single(Collections.Tiles(Entries(), Db.BoxSets()));
        Assert.Equal(("collection", "The Terminator Collection", 2, -528L), (tile.Kind, tile.Name, tile.Count, tile.CollectionId));
    }

    [Fact]
    public void OneOwnedFilmDoesNotMakeABoxSetTile()
    {
        Film("The Terminator", 1984, 218, 528);
        Assert.Empty(Collections.Tiles(Entries(), Db.BoxSets()));
    }

    [Fact]
    public void HidingABoxSetRemovesItUntilItIsBroughtBack()
    {
        Film("Terminator 2", 1991, 280, 528);
        Film("The Terminator", 1984, 218, 528);

        Db.HideBoxSet(-528);
        Assert.Empty(Db.BoxSets());
        Assert.Single(Db.BoxSets(includeHidden: true));

        // TMDB sending the details again must not bring a hidden box set back
        Db.SetDetail("movie", 218, Detail.Empty with { Box = new BoxSet(528, "The Terminator Collection (renamed)", null) });
        Assert.Empty(Db.BoxSets());

        Assert.Equal(1, Db.UnhideBoxSets());
        var back = Assert.Single(Db.BoxSets());
        Assert.Equal("The Terminator Collection (renamed)", back.Name);
        Assert.Equal(0, Db.UnhideBoxSets());
    }

    [Fact]
    public void AFilmThatLeavesItsBoxSetIsNoLongerInIt()
    {
        Film("Terminator 2", 1991, 280, 528);
        Film("The Terminator", 1984, 218, 528);

        Db.SetDetail("movie", 280, Detail.Empty);   // TMDB no longer lists it in the set
        Assert.Equal(new[] { "movie:tmdb:218" }, Db.BoxSets().Single().Refs);
    }

    [Fact]
    public void BoxSetsAndUserCollectionsStayApart()
    {
        Film("Terminator 2", 1991, 280, 528);
        Film("The Terminator", 1984, 218, 528);
        var mine = Db.CreateCollection("Terminator");

        Assert.Empty(Db.Collections().Single().Refs);
        Assert.All(Db.BoxSets(), b => Assert.True(b.Id < 0 && b.Id != mine));
        // the user's empty collection shows no tile; the box set does
        Assert.Equal(-528L, Assert.Single(Collections.Tiles(Entries(), Db.Collections().Concat(Db.BoxSets()))).CollectionId);
    }
}
