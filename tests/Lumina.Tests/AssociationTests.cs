using Lumina.Library;
using Lumina.Windows;
using Microsoft.Win32;
using Xunit;

namespace Lumina.Tests;

// Uses a private branch of HKCU, never the real Software\Classes, and removes it afterwards.
public class AssociationTests : IDisposable
{
    readonly string _branch = @"Software\LuminaTest\" + Guid.NewGuid().ToString("N");
    const string Exe = @"C:\Program Files\Lumina\Lumina.exe";
    string Classes => _branch + @"\Classes";
    string Software => _branch + @"\Software";
    FileAssociations Make() => new(Exe, Classes, Software);

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_branch, throwOnMissingSubKey: false);

    static object? Value(string path, string name = "") { using var k = Registry.CurrentUser.OpenSubKey(path); return k?.GetValue(name); }

    [Fact]
    public void RegisterCreatesTheProgIdTheCommandAndTheOpenWithEntries()
    {
        var fa = Make();
        Assert.False(fa.IsRegistered());
        fa.Register();
        Assert.True(fa.IsRegistered());
        Assert.Equal("\"C:\\Program Files\\Lumina\\Lumina.exe\" -- \"%1\"", Value($@"{Classes}\Lumina.Video\shell\open\command"));
        Assert.Equal("\"C:\\Program Files\\Lumina\\Lumina.exe\",0", Value($@"{Classes}\Lumina.Video\DefaultIcon"));
        Assert.Equal("Lumina", Value($@"{Classes}\Applications\Lumina.exe", "FriendlyAppName"));
        foreach (var ext in FileAssociations.Extensions)
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\{ext}\OpenWithProgids");
            Assert.Contains("Lumina.Video", k!.GetValueNames());
            Assert.Equal("Lumina.Video", Value($@"{Software}\Lumina\Capabilities\FileAssociations", ext));
        }
        Assert.Equal(@$"{Software}\Lumina\Capabilities", Value($@"{Software}\RegisteredApplications", "Lumina"));
    }

    [Fact]
    public void RegisteringTwiceChangesNothing()
    {
        var fa = Make();
        fa.Register(); fa.Register();
        using var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\.mkv\OpenWithProgids");
        Assert.Single(k!.GetValueNames());
        Assert.True(fa.IsRegistered());
    }

    [Fact]
    public void UnregisterRemovesOnlyLuminasEntriesAndLeavesOtherAppsAlone()
    {
        using (var k = Registry.CurrentUser.CreateSubKey($@"{Classes}\.mkv\OpenWithProgids"))
            k.SetValue("OtherPlayer.mkv", Array.Empty<byte>(), RegistryValueKind.None);
        using (var k = Registry.CurrentUser.CreateSubKey($@"{Classes}\OtherPlayer.mkv\shell\open\command")) k.SetValue("", "other.exe");
        using (var k = Registry.CurrentUser.CreateSubKey($@"{Software}\RegisteredApplications")) k.SetValue("OtherPlayer", @"Software\Other\Caps");

        var fa = Make();
        fa.Register();
        fa.Unregister();

        Assert.False(fa.IsRegistered());
        using (var k = Registry.CurrentUser.OpenSubKey($@"{Classes}\.mkv\OpenWithProgids"))
            Assert.Equal(new[] { "OtherPlayer.mkv" }, k!.GetValueNames());
        Assert.Equal("other.exe", Value($@"{Classes}\OtherPlayer.mkv\shell\open\command"));
        Assert.Equal(@"Software\Other\Caps", Value($@"{Software}\RegisteredApplications", "OtherPlayer"));
        Assert.Null(Value($@"{Software}\RegisteredApplications", "Lumina"));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{Classes}\Lumina.Video"));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{Classes}\Applications\Lumina.exe"));
    }

    [Fact]
    public void UnregisterWhenNothingIsRegisteredIsHarmless()
    {
        var fa = Make();
        fa.Unregister();
        Assert.False(fa.IsRegistered());
    }

    [Fact]
    public void ARegistrationForAnotherInstallPathIsNotMistakenForThisOne()
    {
        new FileAssociations(@"D:\old\Lumina.exe", Classes, Software).Register();
        Assert.False(Make().IsRegistered());       // points at a different exe, so the Settings button should offer to fix it
        Make().Register();
        Assert.True(Make().IsRegistered());
    }

    [Fact]
    public void EveryScannedVideoExtensionIsRegistered()
    {
        // The scanner and the shell must agree on what a video is, or double-clicking works for some files only.
        foreach (var e in new[] { ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".mts", ".webm", ".wmv", ".flv", ".iso" })
        {
            Assert.Contains(e, FileAssociations.Extensions);
            Assert.True(PlaybackQueue.IsVideo("x" + e));
        }
        Assert.Equal(FileAssociations.Extensions.Count, FileAssociations.Extensions.Distinct().Count());
    }
}
