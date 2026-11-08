using System.IO;
using Lumina.Windows;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class AppPathsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-prog-" + Guid.NewGuid().ToString("N"));
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_PROGRAM_DIR");

    public AppPathsTests() { Directory.CreateDirectory(_dir); Environment.SetEnvironmentVariable("LUMINA_PROGRAM_DIR", _dir); }

    public void Dispose() { Environment.SetEnvironmentVariable("LUMINA_PROGRAM_DIR", _old); Directory.Delete(_dir, true); }

    [Fact]
    public void WithoutTheMarkerDataLivesInTheUserProfile()
    {
        Assert.False(AppPaths.IsPortable);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumina"), AppPaths.Data);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumina"), AppPaths.Cache);
    }

    [Fact]
    public void WithTheMarkerEverythingMovesBesideTheProgram()
    {
        File.WriteAllText(Path.Combine(_dir, "portable.txt"), "");
        Assert.True(AppPaths.IsPortable);
        Assert.Equal(Path.Combine(_dir, "data"), AppPaths.Data);
        Assert.Equal(Path.Combine(_dir, "data", "cache"), AppPaths.Cache);
    }

    [Fact]
    public void ADifferentProgramFolderIsJudgedOnItsOwn()
    {
        var other = Path.Combine(Path.GetTempPath(), "lumina-prog2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            File.WriteAllText(Path.Combine(other, "portable.txt"), "");
            Assert.False(AppPaths.IsPortable);                                         // _dir has no marker
            Environment.SetEnvironmentVariable("LUMINA_PROGRAM_DIR", other);
            Assert.True(AppPaths.IsPortable);                                          // the cache is keyed by folder
        }
        finally { Environment.SetEnvironmentVariable("LUMINA_PROGRAM_DIR", _dir); Directory.Delete(other, true); }
    }

    [Fact]
    public void EveryDataLocationFollowsTheResolverUnlessATestOverridesIt()
    {
        File.WriteAllText(Path.Combine(_dir, "portable.txt"), "");
        var saved = new[] { "LUMINA_DB", "LUMINA_CONFIG", "LUMINA_LOG", "LUMINA_ART", "LUMINA_SUBS" }.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var k in saved.Keys) Environment.SetEnvironmentVariable(k, null);
            Assert.StartsWith(_dir, Lumina.Diagnostics.Log.Dir);
            Assert.StartsWith(_dir, Lumina.Library.Db.ArtDir);
            Assert.StartsWith(_dir, Lumina.Subtitles.OpenSubtitles.SubsDir);
        }
        finally { foreach (var (k, v) in saved) Environment.SetEnvironmentVariable(k, v); }
    }
}

public class AppInfoTests
{
    [Theory]
    [InlineData("0.1.0", "0.2.0", -1)]
    [InlineData("0.2.0", "0.1.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("0.9.0", "0.10.0", -1)]            // numbers, not text
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3-beta.1", "1.2.3", 0)]       // pre-release tags are ignored
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("2", "1.9.9", 1)]
    public void VersionsCompareByNumber(string a, string b, int expected) => Assert.Equal(expected, Math.Sign(AppInfo.Compare(a, b)!.Value));

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("banana", "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData("1.x.0", "1.0.0")]
    [InlineData("-1.0.0", "1.0.0")]
    public void NonVersionsCompareAsNull(string? a, string? b) => Assert.Null(AppInfo.Compare(a, b));

    [Fact]
    public void TheRunningVersionIsAThreePartNumberFromTheProject()
    {
        Assert.Equal(3, AppInfo.VersionText.Split('.').Length);
        Assert.True(AppInfo.TryParse(AppInfo.VersionText, out _));
        Assert.NotEqual("0.0.0", AppInfo.VersionText);
    }

    [Fact]
    public void AboutLinesNameTheModeAndKeepNoSecrets()
    {
        var lines = AppInfo.Describe("mpv 0.40");
        Assert.Contains(lines, l => l.Label == "Lumina" && l.Value == AppInfo.VersionText);
        Assert.Contains(lines, l => l.Label == "Player" && l.Value == "mpv 0.40");
        Assert.Contains(lines, l => l.Label == "Mode");
        Assert.DoesNotContain(lines, l => l.Value.Contains("key", StringComparison.OrdinalIgnoreCase));
    }
}
