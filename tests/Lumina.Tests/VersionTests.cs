using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>Several files of one film (a 1080p and a 4K copy) are one entry with a Version picker.</summary>
public class VersionLabelTests
{
    static MediaItem F(string name, long size = 2L * 1024 * 1024 * 1024) => new() { Path = @"D:\Films\" + name, Size = size, Kind = "movie" };

    [Theory]
    [InlineData("Film (2020) 2160p HDR HEVC.mkv", "4K · HDR · HEVC")]
    [InlineData("Film.2020.1080p.BluRay.x264.mkv", "1080p · BluRay · H.264")]
    [InlineData("Film (2020) [WEB-DL 720p].mp4", "720p · WEB")]
    [InlineData("Film (2020) Remux 4K DV.mkv", "4K · Remux · Dolby Vision")]
    [InlineData("Film (2020) director's cut.mkv", "")]
    [InlineData("Film (2020) dvd.mkv", "")]   // "dv" inside another word is not Dolby Vision
    public void ReadsThePictureFromTheFileName(string name, string expected) => Assert.Equal(expected, Versions.Qualities(name));

    [Fact]
    public void LabelsHaveTheSizeAndFallBackToTheFileName()
    {
        Assert.Equal("1080p · 2.0 GB", Versions.Label(F("Film 1080p.mkv")));
        Assert.Equal("Film extended.mkv · 2.0 GB", Versions.Label(F("Film extended.mkv")));
    }
}

[Collection("db")]
public class VersionTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-ver-" + Guid.NewGuid().ToString("N"));
    readonly string _lib;

    public VersionTests()
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

    void File_(string name, int bytes) => File.WriteAllText(Path.Combine(_lib, name), new string('x', bytes));
    static List<LibraryEntry> Entries() => LibraryEntry.Build(Db.List());
    static void Match(string title, int year, long tmdb) => Db.SetMatch("movie", title, year, new Meta("movie", tmdb, title, "", null, null, 7, year));

    /// <summary>Two copies of one film, matched: a small 1080p and a big 4K.</summary>
    void TwoCopies()
    {
        File_("Dune (2021) 1080p.mkv", 100);
        File_("Dune (2021) 2160p.mkv", 900);
        Scanner.ScanAll();
        Match("Dune", 2021, 438631);
    }

    [Fact]
    public void FilesMatchedToTheSameFilmAreOneEntryBiggestFirst()
    {
        TwoCopies();
        var film = Assert.Single(Entries());
        Assert.True(film.HasVersions);
        Assert.Equal("movie:" + film.Items.Min(i => i.Id), film.Key);   // the lowest file id: a copy added later never changes it
        Assert.Equal(new[] { "Dune (2021) 2160p.mkv", "Dune (2021) 1080p.mkv" }, film.Items.Select(i => Path.GetFileName(i.Path)));
        Assert.Equal("Dune (2021) 2160p.mkv", Path.GetFileName(film.Next.Path));   // Play opens the biggest copy until one is watched
    }

    [Fact]
    public void PlayOpensTheVersionWatchedLast()
    {
        TwoCopies();
        var small = Entries().Single().Items.Single(i => i.Path.Contains("1080p"));
        Db.SetWatch(small.Id, 600, watched: false, playedAt: 1_700_000_000);

        var film = Entries().Single();
        Assert.Contains("1080p", film.Next.Path);
        Assert.NotNull(film.ContinueItem);
        Assert.Contains("1080p", film.ContinueItem!.Path);   // Continue watching resumes the copy that was started
    }

    [Fact]
    public void UnmatchedFilesWithTheSameNameStayApart()
    {
        File_("Dune (2021) 1080p.mkv", 100);
        File_("Dune (2021) 2160p.mkv", 900);
        Scanner.ScanAll();
        Assert.Equal(2, Entries().Count);
        Assert.All(Entries(), e => Assert.False(e.HasVersions));
    }

    [Fact]
    public void AFilmIsWatchedOnceAnyVersionIsAndMarkingCoversEveryVersion()
    {
        TwoCopies();
        var film = Entries().Single();
        Assert.False(film.AllWatched);
        Assert.True(film.AnyUnwatched);

        Db.MarkWatched(new[] { film.Items[1].Id }, true);
        film = Entries().Single();
        Assert.True(film.AllWatched);
        Assert.False(film.AnyUnwatched);
        Assert.Empty(LibraryEntry.Unwatched(Entries()));

        Db.MarkWatched(film.Items.Select(i => i.Id), false);   // what "Mark as unwatched" does for the entry
        film = Entries().Single();
        Assert.False(film.AllWatched);
        Assert.Single(LibraryEntry.Unwatched(Entries()));
    }

    [Fact]
    public void ACollectionStillFindsTheFilmWhateverVersionItWasSavedBy()
    {
        File_("Dune (2021) 1080p.mkv", 100);
        File_("Dune (2021) 2160p.mkv", 900);
        Scanner.ScanAll();
        var id = Db.CreateCollection("Dune");
        Db.AddToCollection(id, "movie:path:" + Entries().Single(e => e.Items[0].Path.Contains("1080p")).Items[0].Path);   // saved before TMDB matched, by one file's path
        Match("Dune", 2021, 438631);

        var members = Collections.Members(Entries(), Db.Collections().Single());
        Assert.Equal(2, Assert.Single(members).Items.Count);
    }

    [Fact]
    public void SortBySizeCountsAllVersions()
    {
        TwoCopies();
        Assert.Equal(1000, Entries().Single().Size);
    }

    [Fact]
    public void TheKeyStaysTheSameWhenASecondCopyArrives()
    {
        File_("Dune (2021) 2160p.mkv", 900);
        Scanner.ScanAll();
        Match("Dune", 2021, 438631);
        var before = Entries().Single().Key;

        File_("Dune (2021) 1080p.mkv", 100);   // found by a later scan: a higher file id
        Scanner.ScanAll();
        Match("Dune", 2021, 438631);
        Assert.Equal(before, Entries().Single().Key);
        Assert.True(Entries().Single().HasVersions);
    }

    [Fact]
    public void AHalfWatchedVersionStaysInContinueWatchingEvenWhenAnotherWasOpenedLater()
    {
        TwoCopies();
        var film = Entries().Single();
        Db.SetWatch(film.Items[0].Id, 600, watched: false, playedAt: 1_700_000_000);   // the 4K copy, paused
        Db.SetWatch(film.Items[1].Id, 2, watched: false, playedAt: 1_700_000_500);     // the 1080p copy opened later, left after two seconds

        film = Entries().Single();
        Assert.Contains("2160p", film.ContinueItem!.Path);   // not the copy that is merely newest
        Assert.True(film.InProgress);
        Assert.Contains("2160p", film.Next.Path);
        Assert.Single(LibraryEntry.ContinueWatching(Entries()));
    }

    [Fact]
    public void FinishingOneVersionWhileAnotherIsHalfWatchedMakesTheFilmWatched()
    {
        TwoCopies();
        var film = Entries().Single();
        Db.SetWatch(film.Items[0].Id, 600, watched: false, playedAt: 1_700_000_000);
        Db.SetWatch(film.Items[1].Id, 0, watched: true, playedAt: 1_700_000_500);

        film = Entries().Single();
        Assert.True(film.AllWatched);
        Assert.False(film.InProgress);
        Assert.Null(film.ContinueItem);
        Assert.Empty(LibraryEntry.ContinueWatching(Entries()));
    }

    [Fact]
    public void TheFilmsCountAndTraktSeeOneFilm()
    {
        TwoCopies();
        File_("Alien (1979).mkv", 50);
        Scanner.ScanAll();
        Assert.Equal(2, LibraryStats.From(Db.List()).Films);   // Dune once, Alien once

        var rows = Db.List().Where(i => i.Title == "Dune").Select(i => new LocalRow(i.Id, "movie", 438631, null, null, 0, true, 1_700_000_000 + i.Id, null)).ToList();
        Assert.Equal(2, rows.Count);
        var body = System.Text.Json.JsonSerializer.SerializeToElement(Lumina.Sync.Trakt.HistoryBody(rows));
        Assert.Equal(1, body.GetProperty("movies").GetArrayLength());
    }
}
