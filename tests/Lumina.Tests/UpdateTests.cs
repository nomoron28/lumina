using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lumina.Updates;
using Xunit;

namespace Lumina.Tests;

/// <summary>Serves a feed and an installer from memory so the update code runs against a real HTTP server.</summary>
sealed class FakeUpdates : IDisposable
{
    HttpListener _l;
    public string Base { get; }
    public byte[] Installer { get; set; } = Encoding.ASCII.GetBytes(new string('A', 5000));
    public string? FeedOverride { get; set; }
    public int FeedStatus { get; set; } = 200;
    public int FeedDelayMs { get; set; }
    public string Version { get; set; } = "9.9.9";
    public string? ShaOverride { get; set; }
    public long? SizeOverride { get; set; }
    public List<string> Requests { get; } = new();

    public string Sha => ShaOverride ?? Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant();

    public FakeUpdates()
    {
        var (listener, url) = TestNet.StartListener("127.0.0.1");
        _l = listener;
        Base = url;
        _ = Task.Run(async () =>
        {
            while (_l.IsListening)
            {
                HttpListenerContext c;
                try { c = await _l.GetContextAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    lock (Requests) Requests.Add(c.Request.Url!.AbsolutePath);
                    try
                    {
                        if (c.Request.Url!.AbsolutePath == "/latest.json")
                        {
                            if (FeedDelayMs > 0) await Task.Delay(FeedDelayMs);
                            c.Response.StatusCode = FeedStatus;
                            var json = FeedOverride ?? $"{{\"version\":\"{Version}\",\"file\":\"Lumina-{Version}-setup.exe\",\"sha256\":\"{Sha}\",\"size\":{SizeOverride ?? Installer.Length},\"notes\":\"Faster startup\"}}";
                            await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
                        }
                        else if (c.Request.Url.AbsolutePath.EndsWith("-setup.exe")) { c.Response.ContentLength64 = Installer.Length; await c.Response.OutputStream.WriteAsync(Installer); }
                        else c.Response.StatusCode = 404;
                        c.Response.Close();
                    }
                    catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
                });
            }
        });
    }

    public Uri Feed => new(Base + "latest.json");
    public void Dispose() => _l.Close();
}

/// <summary>Records progress in the order it is reported. Progress&lt;T&gt; would post to the thread pool and may reorder.</summary>
sealed class CollectingProgress : IProgress<double>
{
    public List<double> Values { get; } = new();
    public void Report(double v) { lock (Values) Values.Add(v); }
}

public class UpdateFeedTests
{
    static Uri Feed(string host = "https://updates.example.com/") => new(host + "latest.json");
    static string Sha(string c = "a") => new string(c[0], 64);

    [Fact]
    public void AValidFeedParsesWithItsInstallerResolvedAgainstTheFeedAddress()
    {
        var i = UpdateCheck.Parse($"{{\"version\":\"v1.2.3\",\"file\":\"Lumina-1.2.3-setup.exe\",\"sha256\":\"{Sha("B")}\",\"size\":1234,\"notes\":\"Hi\"}}", Feed(), out _)!;
        Assert.Equal("1.2.3", i.Version);
        Assert.Equal("https://updates.example.com/Lumina-1.2.3-setup.exe", i.Download.ToString());
        Assert.Equal(Sha("b"), i.Sha256);                       // normalised to lower case
        Assert.Equal(1234, i.Size);
        Assert.Equal("Hi", i.Notes);
    }

    [Theory]
    [InlineData("not json", "could not be read")]
    [InlineData("[]", "expected shape")]
    [InlineData("{}", "valid version")]
    [InlineData("{\"version\":\"x.y\"}", "valid version")]
    [InlineData("{\"version\":\"1.0.0\"}", "where the installer is")]
    [InlineData("{\"version\":\"1.0.0\",\"file\":\"a.exe\"}", "checksum")]
    [InlineData("{\"version\":\"1.0.0\",\"file\":\"a.exe\",\"sha256\":\"abc\"}", "checksum")]
    [InlineData("{\"version\":\"1.0.0\",\"file\":\"a.exe\",\"sha256\":\"zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz\"}", "checksum")]
    public void UnusableFeedsAreRefusedWithAReason(string json, string reasonPart)
    {
        Assert.Null(UpdateCheck.Parse(json, Feed(), out var reason));
        Assert.Contains(reasonPart, reason);
    }

    [Fact]
    public void AnInstallerOnAnotherServerOrOverPlainHttpIsRefused()
    {
        var other = $"{{\"version\":\"1.0.0\",\"file\":\"https://evil.example.net/a.exe\",\"sha256\":\"{Sha()}\"}}";
        Assert.Null(UpdateCheck.Parse(other, Feed(), out var r1));
        Assert.Contains("different server", r1);
        var plain = $"{{\"version\":\"1.0.0\",\"file\":\"http://updates.example.com/a.exe\",\"sha256\":\"{Sha()}\"}}";
        Assert.Null(UpdateCheck.Parse(plain, Feed(), out var r2));
        Assert.Contains("https", r2);
    }

    [Theory]
    [InlineData("https://example.com/x", true)]
    [InlineData("http://example.com/x", false)]
    [InlineData("http://127.0.0.1:8080/x", true)]      // local testing only
    [InlineData("http://localhost/x", true)]
    [InlineData("ftp://example.com/x", false)]
    [InlineData("file:///C:/x", false)]
    public void OnlyHttpsOrLocalAddressesAreAcceptable(string url, bool ok) => Assert.Equal(ok, UpdateCheck.IsAcceptable(new Uri(url)));
}

public class UpdateFlowTests
{
    [Fact]
    public async Task ANewerVersionIsAvailable()
    {
        using var s = new FakeUpdates { Version = "9.9.9" };
        var r = await UpdateCheck.CheckAsync(s.Feed, "0.1.0");
        Assert.Equal(UpdateStatus.Available, r.Status);
        Assert.Equal("9.9.9", r.Info!.Version);
        Assert.Contains("9.9.9", r.Message);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0", "0.0.9")]       // a feed that is older than what you run is not an update
    public async Task SameOrOlderMeansUpToDate(string current, string feed)
    {
        using var s = new FakeUpdates { Version = feed };
        var r = await UpdateCheck.CheckAsync(s.Feed, current);
        Assert.Equal(UpdateStatus.UpToDate, r.Status);
    }

    [Fact]
    public async Task ServerProblemsAreReportedInPlainWords()
    {
        using (var s = new FakeUpdates { FeedStatus = 404 })
        {
            var r = await UpdateCheck.CheckAsync(s.Feed, "0.1.0");
            Assert.Equal(UpdateStatus.FeedUnreachable, r.Status);
            Assert.Contains("404", r.Message);
        }
        using (var s = new FakeUpdates { FeedOverride = "garbage" })
            Assert.Equal(UpdateStatus.FeedInvalid, (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Status);
        using (var s = new FakeUpdates { FeedDelayMs = 3000 })
        {
            var r = await UpdateCheck.CheckAsync(s.Feed, "0.1.0", timeout: TimeSpan.FromMilliseconds(300));
            Assert.Equal(UpdateStatus.FeedUnreachable, r.Status);
            Assert.Contains("too long", r.Message);
        }
        var none = await UpdateCheck.CheckAsync(new Uri("http://127.0.0.1:1/latest.json"), "0.1.0", timeout: TimeSpan.FromSeconds(3));
        Assert.Equal(UpdateStatus.FeedUnreachable, none.Status);
    }

    [Fact]
    public async Task AFeedOverPlainHttpToARealServerIsRefusedBeforeAnyRequest()
    {
        var r = await UpdateCheck.CheckAsync(new Uri("http://example.com/latest.json"), "0.1.0");
        Assert.Equal(UpdateStatus.FeedRefused, r.Status);
    }

    [Fact]
    public async Task ADownloadIsVerifiedAndReportsProgress()
    {
        using var s = new FakeUpdates { Installer = new byte[3_000_000] };
        new Random(1).NextBytes(s.Installer);
        var info = (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Info!;
        var folder = Path.Combine(Path.GetTempPath(), "lumina-upd-" + Guid.NewGuid().ToString("N"));
        var seen = new CollectingProgress();
        try
        {
            var path = await UpdateCheck.DownloadAsync(info, folder, seen);
            Assert.True(File.Exists(path));
            Assert.Equal(s.Installer, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(folder, "*.part"));
            Assert.NotEmpty(seen.Values);
            Assert.Equal(seen.Values.OrderBy(x => x), seen.Values);            // never goes backwards
            Assert.Equal(1.0, seen.Values[^1], 3);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task AWrongChecksumDeletesTheDownloadAndNothingIsKept()
    {
        using var s = new FakeUpdates { ShaOverride = new string('0', 64) };
        var info = (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Info!;
        var folder = Path.Combine(Path.GetTempPath(), "lumina-upd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var e = await Assert.ThrowsAsync<UpdateException>(() => UpdateCheck.DownloadAsync(info, folder));
            Assert.Contains("did not match its checksum", e.Message);
            Assert.Empty(Directory.GetFiles(folder));            // neither the file nor a .part is left behind
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ATamperedFileWithAValidLookingFeedIsStillCaught()
    {
        using var s = new FakeUpdates();
        var info = (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Info!;       // the feed promises the original bytes
        s.Installer = Encoding.ASCII.GetBytes(new string('B', 5000));           // the server then serves something else of the same size
        var folder = Path.Combine(Path.GetTempPath(), "lumina-upd-" + Guid.NewGuid().ToString("N"));
        try { await Assert.ThrowsAsync<UpdateException>(() => UpdateCheck.DownloadAsync(info, folder)); Assert.Empty(Directory.GetFiles(folder)); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ASizeThatDoesNotMatchTheFeedIsRefused()
    {
        using var s = new FakeUpdates { SizeOverride = 1234 };
        var info = (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Info!;
        var folder = Path.Combine(Path.GetTempPath(), "lumina-upd-" + Guid.NewGuid().ToString("N"));
        try { var e = await Assert.ThrowsAsync<UpdateException>(() => UpdateCheck.DownloadAsync(info, folder)); Assert.Contains("size", e.Message); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task AMissingInstallerIsAClearFailure()
    {
        using var s = new FakeUpdates();
        var info = (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Info! with { Download = new Uri(s.Base + "gone.bin") };
        var folder = Path.Combine(Path.GetTempPath(), "lumina-upd-" + Guid.NewGuid().ToString("N"));
        try { var e = await Assert.ThrowsAsync<UpdateException>(() => UpdateCheck.DownloadAsync(info, folder)); Assert.Contains("404", e.Message); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task CancellingMidDownloadLeavesNoPartialFile()
    {
        using var s = new FakeUpdates { Installer = new byte[20_000_000] };
        var info = (await UpdateCheck.CheckAsync(s.Feed, "0.1.0")).Info!;
        var folder = Path.Combine(Path.GetTempPath(), "lumina-upd-" + Guid.NewGuid().ToString("N"));
        using var cts = new CancellationTokenSource();
        try
        {
            var p = new Progress<double>(v => { if (v > 0.05) cts.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateCheck.DownloadAsync(info, folder, p, ct: cts.Token));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void InstallerArgumentsAreSilentAndRestartLumina() =>
        Assert.All(new[] { "/SILENT", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS" }, a => Assert.Contains(a, UpdateCheck.InstallerArguments));
}
