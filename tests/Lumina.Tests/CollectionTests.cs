using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>Issue #16: the user's own collections ("Terminator Collection").</summary>
[Collection("db")]
public class CollectionTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-col-" + Guid.NewGuid().ToString("N"));
    readonly string _lib;

    public CollectionTests()
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

    void Touch(string name) => File.WriteAllText(Path.Combine(_lib, name), "x");
    static List<LibraryEntry> Entries() => LibraryEntry.Build(Db.List());
    static LibraryEntry Get(string title) => Entries().Single(e => e.Title == title);

    [Fact]
    public void CollectsTitlesInReleaseOrderAndKeepsThemAfterTheyAreMatched()
    {
        Touch("Terminator 2 (1991).mkv");
        Touch("The Terminator (1984).mkv");
        Touch("Alien (1979).mkv");
        Touch("Show S01E01.mkv");
        Scanner.ScanAll();

        var id = Db.CreateCollection("  Terminator ");
        Assert.Equal(id, Db.CreateCollection("terminator"));   // same name, any case, is the same collection
        Db.AddToCollection(id, Collections.Ref(Get("Terminator 2")));
        Db.AddToCollection(id, Collections.Ref(Get("The Terminator")));
        Db.AddToCollection(id, Collections.Ref(Get("Show")));
        Db.AddToCollection(id, Collections.Ref(Get("Show")));   // twice is once

        var col = Db.Collections().Single();
        Assert.Equal("Terminator", col.Name);
        Assert.Equal(new[] { "The Terminator", "Terminator 2", "Show" }, Collections.Members(Entries(), col).Select(e => e.Title));

        // TMDB matches the film later: it was saved by its path and is still in the collection
        Db.SetMatch("movie", "Terminator 2", 1991, new Meta("movie", 280, "Terminator 2: Judgment Day", "", null, null, 8.1, 1991));
        Assert.Contains(Collections.Members(Entries(), Db.Collections().Single()), e => e.TmdbId == 280);

        var tile = Assert.Single(Collections.Tiles(Entries(), Db.Collections()));
        Assert.Equal(("collection", "Terminator", 3, id), (tile.Kind, tile.Name, tile.Count, tile.CollectionId));
    }

    [Fact]
    public void RemoveRenameAndDelete()
    {
        Touch("Alien (1979).mkv");
        Touch("Aliens (1986).mkv");
        Scanner.ScanAll();
        var id = Db.CreateCollection("Alien");
        foreach (var e in Entries()) Db.AddToCollection(id, Collections.Ref(e));

        Db.RemoveFromCollection(id, Collections.Refs(Get("Aliens")));
        Assert.Equal(new[] { "Alien" }, Collections.Members(Entries(), Db.Collections().Single()).Select(e => e.Title));

        Db.CreateCollection("Other");
        Assert.Throws<ArgumentException>(() => Db.RenameCollection(id, "other"));
        Db.RenameCollection(id, "Alien films");
        Assert.Contains(Db.Collections(), c => c.Name == "Alien films");

        Db.DeleteCollection(id);
        Assert.DoesNotContain(Db.Collections(), c => c.Id == id);
        Assert.Equal(2, Entries().Count);                       // the films stay in the library
    }

    [Fact]
    public void EmptyCollectionsGetNoTileOnHome()
    {
        Db.CreateCollection("Nothing yet");
        Assert.Empty(Collections.Tiles(Entries(), Db.Collections()));
    }
}
