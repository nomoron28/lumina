using Lumina.Network;
using Xunit;

namespace Lumina.Tests;

// Uses the real Windows Credential Manager under a private prefix and removes everything it created.
[Collection("db")]   // the Windows credential service is shared, so these run one at a time
public class CredentialTests : IDisposable
{
    readonly CredentialStore _store = new("LuminaTest." + Guid.NewGuid().ToString("N") + ":");
    readonly List<string> _made = new();

    void Save(string target, string user, string pass) { _store.Save(target, user, pass); _made.Add(target); }

    public void Dispose() { foreach (var t in _made) _store.Delete(t); }

    [Fact]
    public void ALoginRoundTripsExactly()
    {
        Save("https://nas.local:8443", "alice", "p@ss w0rd!");
        var l = Settle.Until(() => _store.Load("https://nas.local:8443"), x => x != null)!;
        Assert.Equal("alice", l.User);
        Assert.Equal("p@ss w0rd!", l.Password);
    }

    [Fact]
    public void UnicodeAndLongPasswordsSurvive()
    {
        var pass = "pässwörd-日本語-" + new string('x', 400);
        Save("ftp://host:21", "üser", pass);
        var l = Settle.Until(() => _store.Load("ftp://host:21"), x => x != null)!;
        Assert.Equal("üser", l.User);
        Assert.Equal(pass, l.Password);
    }

    [Fact]
    public void SavingAgainReplacesTheOldLogin()
    {
        Save("https://a:443", "u", "old");
        Save("https://a:443", "u", "new");
        Assert.Equal("new", Settle.Until(() => _store.Load("https://a:443")?.Password, p => p == "new"));
        Assert.Single(Settle.Until(() => _store.List(), l => l.Count == 1));
    }

    [Fact]
    public void MissingLoginsReadAsNullAndDeletingTwiceIsHarmless()
    {
        Assert.Null(_store.Load("https://nowhere:1"));
        Save("https://b:443", "u", "p");
        Assert.True(Settle.Until(() => _store.Delete("https://b:443"), d => d));          // repeating a delete is harmless
        Assert.Null(Settle.Until(() => _store.Load("https://b:443"), l => l == null));
        Assert.False(_store.Delete("https://b:443"));
    }

    [Fact]
    public void ListShowsOnlyThisStoresLoginsSortedWithoutThePrefix()
    {
        Save("https://zeta:443", "z", "1");
        Save("ftp://alpha:21", "a", "2");
        var other = new CredentialStore("LuminaTestOther." + Guid.NewGuid().ToString("N") + ":");
        other.Save("https://foreign:443", "f", "3");
        try
        {
            Assert.Equal(new[] { "ftp://alpha:21", "https://zeta:443" }, Settle.Until(() => _store.List(), l => l.Count == 2).Select(l => l.Target));
        }
        finally { other.Delete("https://foreign:443"); }
    }

    [Fact]
    public void AnEmptyStoreListsNothing() => Assert.Empty(_store.List());

    [Theory]
    [InlineData("https://NAS.Local/path/to/file.mkv", "https://nas.local:443")]
    [InlineData("http://host:8080/a?b=c", "http://host:8080")]
    [InlineData("ftp://user:secret@Files.Example.com/movie.mp4", "ftp://files.example.com:21")]
    [InlineData("http://host/x", "http://host:80")]
    public void TheKeyIsSchemeHostAndPortOnlyNeverTheUserInfoOrPath(string url, string key) =>
        Assert.Equal(key, CredentialStore.TargetFor(new Uri(url)));

    [Fact]
    public void BasicHeaderIsStandardBase64OfUserColonPassword() =>
        Assert.Equal("Basic YWxpY2U6cDpzczo=", CredentialStore.BasicHeader("alice", "p:ss:"));    // a colon in the password is fine
}

[Collection("db")]   // the Windows credential service is shared, so these run one at a time
public class ShareLoginTests
{
    [Theory]
    [InlineData(@"\\nas\media\movies", "nas")]
    [InlineData(@"\\nas", "nas")]
    [InlineData("\\\\nas.local/media", "nas.local")]
    [InlineData("smb://files/share", "files")]
    [InlineData("SMB://Files", "files")]
    [InlineData("https://nas/x", null)]
    [InlineData("ftp://nas/x", null)]
    [InlineData("nas", null)]
    [InlineData(@"\\", null)]
    [InlineData("", null)]
    public void RecognisesWindowsFileShareAddresses(string address, string? server) => Assert.Equal(server, CredentialStore.ShareServer(address));

    [Fact]
    public void ShareLoginsGoToWindowsUnderTheBareServerNameAndComeBackOut()
    {
        var server = "luminatest-" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            Assert.False(CredentialStore.HasWindowsShareLogin(server));
            CredentialStore.SaveForWindowsShare(server, "alice", "pw");
            Assert.True(Settle.Until(() => CredentialStore.HasWindowsShareLogin(server), h => h));     // the write can take a moment to be readable
        }
        finally { CredentialStore.DeleteForWindowsShare(server); }
        // Windows can report a just-deleted share login for a moment, so wait for it to settle instead of asserting at once.
        var until = DateTime.UtcNow.AddSeconds(3);
        while (CredentialStore.HasWindowsShareLogin(server) && DateTime.UtcNow < until) Thread.Sleep(50);
        Assert.False(CredentialStore.HasWindowsShareLogin(server));
    }

    [Fact]
    public void RemovingALoginThatIsNotThereIsHarmless() => Assert.False(CredentialStore.DeleteForWindowsShare("luminatest-nothing-" + Guid.NewGuid().ToString("N")[..8]));
}
