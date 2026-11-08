using System.IO;
using Lumina.Library;
using Lumina.Metadata;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class ConfigTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-cfg-" + Guid.NewGuid().ToString("N"));
    string CfgPath => Path.Combine(_dir, "config.json");

    public ConfigTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", CfgPath);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", null);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void SaveSetsKeepsUnknownKeysAndRemovesEmpty()
    {
        File.WriteAllText(CfgPath, "{\"tmdbKey\":\"old\",\"somethingElse\":\"keep me\",\"mpvOptions\":\"x=1\"}");
        Config.Save(new Dictionary<string, string> { ["tmdbKey"] = "new", ["openSubtitlesKey"] = "os", ["mpvOptions"] = "" });

        Assert.Equal("new", Config.TmdbKey);
        Assert.Equal("os", Config.OpenSubtitlesKey);
        Assert.Equal("", Config.MpvOptions); // empty value removed the key
        Assert.Contains("keep me", File.ReadAllText(CfgPath)); // unknown key survived
    }

    [Fact]
    public void SaveCreatesFileAndMultilineOptionsRoundTrip()
    {
        var opts = "hwdec=auto-safe" + Environment.NewLine + "# a comment" + Environment.NewLine + "deband=yes";
        Config.Save(new Dictionary<string, string> { ["mpvOptions"] = opts });
        Assert.Equal(opts, Config.MpvOptions);
        Assert.False(File.Exists(CfgPath + ".tmp"));
    }

    [Fact]
    public void CorruptFileIsBackedUpNotLost()
    {
        File.WriteAllText(CfgPath, "{ this is not json");
        Config.Save(new Dictionary<string, string> { ["tmdbKey"] = "k" });
        Assert.Equal("k", Config.TmdbKey);
        Assert.Equal("{ this is not json", File.ReadAllText(CfgPath + ".bak"));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData(" EN , es ,en", "en,es")]
    [InlineData("pt-BR,zh-cn", "pt-br,zh-cn")]
    [InlineData("english", null)]
    [InlineData("en,,", "en")]
    [InlineData("e", null)]
    [InlineData("en;es", null)]
    [InlineData("", null)]
    public void NormalizesLanguages(string input, string? expected) =>
        Assert.Equal(expected, Config.NormalizeLanguages(input));
}

[Collection("db")]
public class FolderRemovalTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-rm-" + Guid.NewGuid().ToString("N"));

    public FolderRemovalTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Db.Init();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    string Folder(string name, params string[] files)
    {
        var p = Path.Combine(_dir, name);
        Directory.CreateDirectory(p);
        foreach (var f in files) File.WriteAllText(Path.Combine(p, f), "x");
        return p;
    }

    [Fact]
    public void RemovingARootDropsItsItemsAndWatchStateOnly()
    {
        var a = Folder("A", "Alpha.2020.mkv");
        var b = Folder("B", "Beta.2021.mkv");
        Db.AddRoot(a);
        Db.AddRoot(b);
        Scanner.ScanAll();
        foreach (var i in Db.List()) Db.SaveWatch(i.Id, 10, 100, false);

        Assert.Equal(1, Db.RemoveRoot(a));

        Assert.Equal(new[] { b }, Db.Roots());
        var left = Db.List().Single();
        Assert.Equal("Beta", left.Title);
        Assert.Equal(10, left.Position); // B keeps its progress
        Assert.Equal(0, Scanner.ScanAll()); // and a rescan does not resurrect A
        Assert.Single(Db.List());
    }

    [Fact]
    public void FilesUnderAnotherRemainingFolderStay()
    {
        var outer = Folder("Outer", "Top.2019.mkv");
        var inner = Path.Combine(outer, "Inner");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "Deep.2018.mkv"), "x");
        Db.AddRoot(outer);
        Db.AddRoot(inner);
        Scanner.ScanAll();
        Assert.Equal(2, Db.List().Count);

        Assert.Equal(0, Db.RemoveRoot(inner)); // Deep is still covered by Outer
        Assert.Equal(2, Db.List().Count);

        Assert.Equal(2, Db.RemoveRoot(outer));
        Assert.Empty(Db.List());
    }

    [Fact]
    public void SiblingFoldersWithSharedNamePrefixDoNotEatEachOther()
    {
        var movies = Folder("Movies", "One.2020.mkv");
        var movies2 = Folder("Movies2", "Two.2021.mkv");
        Db.AddRoot(movies);
        Db.AddRoot(movies2);
        Scanner.ScanAll();
        Scanner.ScanAll(); // the old prefix match made each scan delete the other folder's rows
        Assert.Equal(2, Db.List().Count);

        Db.RemoveRoot(movies);
        var left = Db.List().Single();
        Assert.Equal("Two", left.Title);
    }

    [Fact]
    public void RemovingUnknownRootIsHarmless()
    {
        Assert.Equal(0, Db.RemoveRoot(Path.Combine(_dir, "nope")));
    }
}

public class MpvBadOptionTests
{
    [Fact]
    public void BadExtraOptionsAreSkippedAndReportedNotFatal()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"))) return;
        var extra = string.Join("\n", "vo=null", "ao=null", "# comment", "not-a-real-option=1", "no equals sign", "volume=notanumber");
        using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, extra);
        Assert.Equal(new[] { "not-a-real-option", "no equals sign", "volume" }, m.BadOptions);
        Assert.Equal("null", m.Get("options/vo"));                  // user option beats the shipped mpv.conf
        Assert.Equal("d3d11va,d3d11va-copy", m.Get("options/hwdec"));       // shipped default still applies
        Assert.Contains("lumina-subs", m.Get("input-bindings") ?? ""); // input.conf is loaded
    }
}
