using System.IO;
using Lumina.Library;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

public class PinTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("12 4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("１２３４", false)]      // full-width digits are not ASCII digits
    public void OnlyFourToEightAsciiDigitsMakeAPin(string? pin, bool ok) => Assert.Equal(ok, Pin.IsValid(pin));

    [Fact]
    public void ARightPinVerifiesAWrongOneDoesNotAndTheDigitsAreNotInTheStoredText()
    {
        var stored = Pin.Create("4821", 2000);                      // fewer rounds only to keep the test quick
        Assert.True(Pin.Verify("4821", stored));
        Assert.False(Pin.Verify("4822", stored));
        Assert.False(Pin.Verify("482", stored));
        Assert.DoesNotContain("4821", stored);
    }

    [Fact]
    public void TheSameDigitsGiveADifferentStoredValueEachTimeBecauseOfTheSalt()
    {
        var a = Pin.Create("1111", 2000); var b = Pin.Create("1111", 2000);
        Assert.NotEqual(a, b);
        Assert.True(Pin.Verify("1111", a)); Assert.True(Pin.Verify("1111", b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"V\":1,\"Salt\":\"!!\",\"Hash\":\"??\",\"Iter\":5000}")]
    [InlineData("{\"V\":2,\"Salt\":\"AAAA\",\"Hash\":\"AAAA\",\"Iter\":5000}")]
    [InlineData("{\"V\":1,\"Salt\":\"AAAA\",\"Hash\":\"AAAA\",\"Iter\":1}")]               // a tampered, trivially cheap round count is refused
    [InlineData("{\"V\":1,\"Salt\":\"AAAA\",\"Hash\":\"AAAA\",\"Iter\":999999999}")]        // and so is one made to hang the app
    public void ADamagedOrMissingStoredValueNeverLetsAnyoneIn(string? stored)
    {
        Assert.False(Pin.Verify("1234", stored));
        Assert.False(Pin.Verify("0000", stored));
    }

    [Fact]
    public void CreatingWithABadPinIsRefused() => Assert.Throws<ArgumentException>(() => Pin.Create("12"));

    [Fact]
    public void TheShippedRoundCountIsAtLeastOneHundredThousand() => Assert.True(Pin.Iterations >= 100_000);
}

public class LockedPathTests
{
    [Theory]
    [InlineData(@"C:\Kids\Movie.mkv", @"C:\Kids", true)]
    [InlineData(@"C:\Kids\Sub\Deep\Movie.mkv", @"C:\Kids", true)]
    [InlineData(@"c:\kids\MOVIE.MKV", @"C:\KIDS", true)]                    // case
    [InlineData(@"C:\Kids\Movie.mkv", @"C:\Kids\", true)]                    // trailing slash on the folder
    [InlineData(@"C:/Kids/Movie.mkv", @"C:\Kids", true)]                     // forward slashes
    [InlineData(@"C:\Other\..\Kids\Movie.mkv", @"C:\Kids", true)]            // .. does not get out of the lock
    [InlineData(@"C:\Kids\..\Kids\.\Movie.mkv", @"C:\Kids", true)]
    [InlineData(@"C:\Kids", @"C:\Kids", true)]                               // the folder itself
    [InlineData(@"C:\Kids2\Movie.mkv", @"C:\Kids", false)]                   // a different folder that starts the same
    [InlineData(@"C:\KidsStuff\Movie.mkv", @"C:\Kids", false)]
    [InlineData(@"C:\Other\Movie.mkv", @"C:\Kids", false)]
    [InlineData(@"D:\Kids\Movie.mkv", @"C:\Kids", false)]                    // another drive
    [InlineData(@"C:\Kids\Movie.mkv", "", false)]                            // an empty lock locks nothing, not everything
    public void IsUnderSeesThroughSpellingTricks(string path, string root, bool expected) => Assert.Equal(expected, LockedPaths.IsUnder(path, root));

    [Fact]
    public void NetworkPathsAreMatchedToo()
    {
        Assert.True(LockedPaths.IsUnder(@"\\nas\media\Kids\a.mkv", @"\\NAS\Media\Kids"));
        Assert.False(LockedPaths.IsUnder(@"\\nas\media\Adults\a.mkv", @"\\nas\media\Kids"));
    }

    [Fact]
    public void ShortEightDotThreeNamesCannotHideAFileInsideALockedFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Lumina Locked Folder " + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "A Long Movie Name.mkv");
            File.WriteAllText(file, "x");
            var shortName = ShortPath(file);
            if (shortName == null || !shortName.Contains('~')) return;      // short names switched off on this volume: nothing to test
            Assert.True(LockedPaths.IsUnder(shortName, dir), $"{shortName} got past the lock on {dir}");
            Assert.True(LockedPaths.IsUnder(file, ShortPath(dir) ?? dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern uint GetShortPathName(string longPath, System.Text.StringBuilder shortPath, uint size);
    static string? ShortPath(string p) { var sb = new System.Text.StringBuilder(1024); return GetShortPathName(p, sb, 1024) > 0 ? sb.ToString() : null; }

    [Fact]
    public void IsLockedLooksAtEveryLockedRoot()
    {
        var roots = new[] { @"C:\Kids", @"D:\Private" };
        Assert.True(LockedPaths.IsLocked(@"D:\private\x.mkv", roots));
        Assert.True(LockedPaths.IsLocked(@"C:\Kids\x.mkv", roots));
        Assert.False(LockedPaths.IsLocked(@"E:\Movies\x.mkv", roots));
        Assert.False(LockedPaths.IsLocked(@"C:\Kids\x.mkv", Array.Empty<string>()));
    }

    [Fact]
    public void TheMatcherAgreesWithIsLockedForEveryCase()
    {
        var roots = new[] { @"C:\Kids", @"D:\Private\", @"\\nas\media\Kids" };
        var match = LockedPaths.Matcher(roots);
        foreach (var path in new[] { @"C:\Kids\a.mkv", @"c:/kids/b/c.mkv", @"C:\Kids2\a.mkv", @"D:\private\x.mkv", @"D:\Privateer\x.mkv", @"\\NAS\Media\kids\a.mkv",
                                     @"E:\Movies\a.mkv", @"C:\x\..\Kids\a.mkv", "" })
            Assert.Equal(LockedPaths.IsLocked(path, roots), match(path));
    }

    [Fact]
    public void AMatcherForNoFoldersLocksNothingAndBlankFoldersAreIgnored()
    {
        Assert.False(LockedPaths.Matcher(Array.Empty<string>())(@"C:\anything.mkv"));
        Assert.False(LockedPaths.Matcher(new[] { "", "   " })(@"C:\anything.mkv"));
    }

    [Theory]
    [InlineData("not a path \0 at all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("::::")]
    public void OddTextNeverThrowsAndNeverCountsAsInside(string weird)
    {
        Assert.False(LockedPaths.IsUnder(weird, @"C:\Kids"));
        Assert.False(LockedPaths.IsLocked(weird, new[] { @"C:\Kids" }));
    }
}

public class AttemptLimiterTests
{
    static readonly DateTime T = new(2026, 10, 17, 3, 0, 0);

    [Fact]
    public void FiveWrongPinsInARowLockThePromptForThirtySecondsThenItOpensAgain()
    {
        var l = new AttemptLimiter();
        for (var i = 0; i < 4; i++) { Assert.True(l.Allowed(T)); l.Failed(T); }
        Assert.True(l.Allowed(T));
        l.Failed(T);                                          // the fifth
        Assert.False(l.Allowed(T.AddSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(20), l.Wait(T.AddSeconds(10)));
        Assert.False(l.Allowed(T.AddSeconds(29.9)));
        Assert.True(l.Allowed(T.AddSeconds(30)));
    }

    [Fact]
    public void ARightPinResetsTheCount()
    {
        var l = new AttemptLimiter();
        for (var i = 0; i < 4; i++) l.Failed(T);
        l.Succeeded();
        for (var i = 0; i < 4; i++) l.Failed(T);
        Assert.True(l.Allowed(T));                            // only four since the reset
    }

    [Fact]
    public void KeepingOnTryingDuringTheLockoutDoesNotExtendIt()
    {
        var l = new AttemptLimiter(3, TimeSpan.FromSeconds(10));
        for (var i = 0; i < 3; i++) l.Failed(T);
        l.Failed(T.AddSeconds(5)); l.Failed(T.AddSeconds(6));
        Assert.True(l.Allowed(T.AddSeconds(10)));
    }

    [Fact]
    public void ALockoutIsFollowedByAFreshRunOfAttempts()
    {
        var l = new AttemptLimiter(2, TimeSpan.FromSeconds(10));
        l.Failed(T); l.Failed(T);
        var later = T.AddSeconds(11);
        Assert.True(l.Allowed(later));
        l.Failed(later);
        Assert.True(l.Allowed(later));                         // one of two
        l.Failed(later);
        Assert.False(l.Allowed(later));
    }
}

[Collection("db")]
public class LockStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-lock-" + Guid.NewGuid().ToString("N"));

    public LockStoreTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db"));
        Db.Init();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    [Fact]
    public void NoPinToStartWithThenOneIsSetCheckedAndChanged()
    {
        Assert.False(LockStore.HasPin);
        Assert.False(LockStore.CheckPin("1234"));
        LockStore.SetPin("1234");
        Assert.True(LockStore.HasPin);
        Assert.True(LockStore.CheckPin("1234"));
        Assert.False(LockStore.CheckPin("4321"));
        LockStore.SetPin("9876");
        Assert.False(LockStore.CheckPin("1234"));
        Assert.True(LockStore.CheckPin("9876"));
    }

    [Fact]
    public void ThePinIsNeverStoredAsDigitsAnywhereInTheDatabase()
    {
        LockStore.SetPin("246810");
        SqliteConnection.ClearAllPools();                                             // release the file so it can be read whole
        var bytes = File.ReadAllBytes(Path.Combine(_dir, "t.db"));
        Assert.NotEmpty(bytes);
        Assert.DoesNotContain("246810", System.Text.Encoding.Latin1.GetString(bytes));
        Assert.Contains("parental_pin", System.Text.Encoding.Latin1.GetString(bytes));   // and it really was written, so the check above means something
    }

    [Fact]
    public void FoldersLockAndUnlockAndSpellingOfTheSameFolderIsOneFolder()
    {
        LockStore.SetFolderLocked(@"C:\Kids", true);
        LockStore.SetFolderLocked(@"c:\KIDS\", true);                                 // the same folder again
        Assert.Single(LockStore.Folders);
        Assert.True(LockStore.IsFolderLocked(@"C:/kids"));
        LockStore.SetFolderLocked(@"D:\Private", true);
        LockStore.SetFolderLocked(@"C:\Kids", false);
        Assert.Equal(new[] { @"D:\Private" }, LockStore.Folders);
        Assert.False(LockStore.IsFolderLocked(@"C:\Kids"));
    }

    [Fact]
    public void RemovingThePinUnlocksEveryFolderBecauseNoLockCouldBeOpenedWithoutIt()
    {
        LockStore.SetPin("1234");
        LockStore.SetFolderLocked(@"C:\Kids", true);
        LockStore.ClearPin();
        Assert.False(LockStore.HasPin);
        Assert.Empty(LockStore.Folders);
    }

    [Fact]
    public void ADamagedFolderListReadsAsEmptyNotAsACrash()
    {
        Db.KvSet("parental_folders", "{{ not json");
        Assert.Empty(LockStore.Folders);
    }
}
