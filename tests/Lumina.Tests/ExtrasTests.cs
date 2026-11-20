using System.IO;
using Lumina.Library;
using Xunit;

namespace Lumina.Tests;

public class ExtrasNameTests
{
    [Theory]
    [InlineData(@"C:\lib\Inception (2010)\Inception (2010)-trailer.mkv", "Trailer")]
    [InlineData(@"C:\lib\Inception (2010)\trailer.mp4", "Trailer")]
    [InlineData(@"C:\lib\Inception (2010)\Making of-featurette.mkv", "Featurette")]
    [InlineData(@"C:\lib\Inception (2010)\Extras\Dream levels.mkv", "Extra")]
    [InlineData(@"C:\lib\Inception (2010)\Behind The Scenes\Day one.mkv", "Behind the scenes")]
    [InlineData(@"C:\lib\Show\Deleted Scenes\Cut.mkv", "Deleted scene")]
    public void RecognisesExtras(string path, string kind) => Assert.Equal(kind, Extras.KindOf(path, @"C:\lib"));

    [Theory]
    [InlineData(@"C:\lib\Inception (2010)\Inception (2010).mkv")]
    [InlineData(@"C:\lib\The Big Short (2015).mkv")]
    [InlineData(@"C:\lib\Extra (2011).mkv")]                 // a film called Extra
    [InlineData(@"C:\lib\Show\Season 1\Show S01E01.mkv")]
    [InlineData(@"C:\lib\Show\Specials\Show S00E01.mkv")]    // specials are season 0, not extras
    [InlineData(@"C:\Shorts\Paperman.mkv")]                  // a library root called Shorts holds short films
    public void LeavesFilmsAndEpisodesAlone(string path)
    {
        var root = path.StartsWith(@"C:\Shorts") ? @"C:\Shorts" : @"C:\lib";
        Assert.Null(Extras.KindOf(path, root));
        Assert.False(Extras.Skip(path, root));
    }

    [Theory]
    [InlineData(@"C:\lib\Film\Film-sample.mkv")]
    [InlineData(@"C:\lib\Film\sample.mkv")]
    [InlineData(@"C:\lib\Film\Sample\film.sample.mkv")]
    public void SamplesAreSkipped(string path) => Assert.True(Extras.Skip(path, @"C:\lib"));
}

[Collection("db")]
public class ExtrasScanTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-extras-" + Guid.NewGuid().ToString("N"));

    public ExtrasScanTests()
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

    string Touch(params string[] parts)
    {
        var p = Path.Combine(new[] { _dir, "lib" }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void ExtrasStayOutOfTheLibraryAndAreListedUnderTheirTitle()
    {
        var film = Touch("Inception (2010)", "Inception (2010).mkv");
        Touch("Inception (2010)", "Inception (2010)-trailer.mkv");
        Touch("Inception (2010)", "Inception (2010)-sample.mkv");
        Touch("Inception (2010)", "Extras", "Dream levels.mkv");
        Touch("Inception (2010)", "Extras", "notes.txt");
        var loose = Touch("Alien (1979).mkv");                 // films side by side in the root
        Touch("Alien (1979)-trailer.mkv");
        Touch("Aliens (1986).mkv");
        Touch("Aliens (1986)-trailer.mkv");
        var ep = Touch("Show", "Season 1", "Show S01E01.mkv");
        Touch("Show", "Featurettes", "Cast chat.mkv");
        var root = Path.Combine(_dir, "lib");
        Db.AddRoot(root);
        Scanner.ScanAll();

        var titles = Db.List().Select(i => Path.GetFileName(i.Path)).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Alien (1979).mkv", "Aliens (1986).mkv", "Inception (2010).mkv", "Show S01E01.mkv" }, titles);

        var inception = Extras.For(new[] { film }, new[] { root });
        Assert.Equal(new[] { ("Trailer", "Trailer"), ("Extra", "Dream levels") }, inception.Select(e => (e.Kind, e.Name)));

        // in a shared folder only the extras named after the film belong to it
        var alien = Extras.For(new[] { loose }, new[] { root });
        Assert.Equal(new[] { "Alien (1979)-trailer.mkv" }, alien.Select(e => Path.GetFileName(e.Path)));

        // a show's extras folder sits beside its season folders
        var show = Extras.For(new[] { ep }, new[] { root });
        Assert.Equal(new[] { ("Featurette", "Cast chat") }, show.Select(e => (e.Kind, e.Name)));
    }

    [Fact]
    public void TrailersAlreadyInTheLibraryLeaveOnTheNextScan()
    {
        var lib = Path.Combine(_dir, "lib");
        Touch("Film (2001)", "Film (2001).mkv");
        var trailer = Touch("Film (2001)", "Film (2001)-trailer.mkv");
        Db.AddRoot(lib);
        // an older Lumina filed the trailer as a film of its own
        Db.SyncRoot(lib, new() { new Found(trailer, 1, 1, new Parsed("movie", "Film", 2001, null, null)) });
        Assert.Single(Db.List());

        Scanner.ScanAll();
        Assert.Equal(new[] { "Film (2001).mkv" }, Db.List().Select(i => Path.GetFileName(i.Path)));
    }
}
