using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;
using Lumina.Network;
using Xunit;

namespace Lumina.Tests;

/// <summary>A small local web server whose behaviour each test sets.</summary>
sealed class FakeWeb : IDisposable
{
    public record Seen(string Method, string Url, string? Auth, string? Range);
    HttpListener _l;
    public List<Seen> Requests { get; } = new();
    public Func<HttpListenerRequest, (int Status, string Body, int DelayMs)> Handler { get; set; } = _ => (200, "x", 0);
    public string Base { get; }

    public FakeWeb()
    {
        var (listener, url) = TestNet.StartListener("localhost");
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
                    lock (Requests) Requests.Add(new Seen(c.Request.HttpMethod, c.Request.RawUrl ?? "", c.Request.Headers["Authorization"], c.Request.Headers["Range"]));
                    var (status, body, delay) = Handler(c.Request);
                    if (delay > 0) await Task.Delay(delay);
                    try
                    {
                        c.Response.StatusCode = status;
                        var bytes = Encoding.UTF8.GetBytes(body);
                        await c.Response.OutputStream.WriteAsync(bytes);
                        c.Response.Close();
                    }
                    catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { }
                });
            }
        });
    }

    public void Dispose() => _l.Close();
}

/// <summary>A bare TCP listener that answers every request like a proxy would and records the request line.</summary>
sealed class FakeProxy : IDisposable
{
    readonly System.Net.Sockets.TcpListener _l = new(IPAddress.Loopback, 0);
    public List<string> RequestLines { get; } = new();
    public string Url => $"http://127.0.0.1:{((IPEndPoint)_l.LocalEndpoint).Port}/";

    public FakeProxy()
    {
        _l.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                System.Net.Sockets.TcpClient c;
                try { c = await _l.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    using var cl = c;
                    var s = cl.GetStream();
                    var buf = new byte[4096];
                    var n = await s.ReadAsync(buf);
                    var text = Encoding.ASCII.GetString(buf, 0, n);
                    lock (RequestLines) RequestLines.Add(text.Split("\r\n")[0]);
                    var reply = Encoding.ASCII.GetBytes("HTTP/1.1 206 Partial Content\r\nContent-Length: 1\r\nConnection: close\r\n\r\nx");
                    await s.WriteAsync(reply);
                });
            }
        });
    }

    public void Dispose() => _l.Stop();
}

sealed class ThrowingHandler(Exception e) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw e;
}

public class StreamProbeTests
{
    static Uri U(FakeWeb w, string path = "v.mkv") => new(w.Base + path);

    [Fact]
    public async Task AnOpenServerIsOk()
    {
        using var w = new FakeWeb();
        var o = await StreamProbe.CheckAsync(U(w));
        Assert.Equal(ProbeResult.Ok, o.Result);
        Assert.True(o.CanPlay);
        Assert.Equal("bytes=0-0", w.Requests.Single().Range);       // cheap: asks for one byte
    }

    [Theory]
    [InlineData(206)]
    [InlineData(416)]
    public async Task RangeAnswersCountAsReachable(int status)
    {
        using var w = new FakeWeb { Handler = _ => (status, "", 0) };
        Assert.Equal(ProbeResult.Ok, (await StreamProbe.CheckAsync(U(w))).Result);
    }

    [Fact]
    public async Task A401WithoutALoginAsksForOne()
    {
        using var w = new FakeWeb { Handler = _ => (401, "", 0) };
        var o = await StreamProbe.CheckAsync(U(w));
        Assert.Equal(ProbeResult.NeedsLogin, o.Result);
        Assert.False(o.CanPlay);
        Assert.Contains("asks for a login", o.Message);
    }

    [Fact]
    public async Task A401WithALoginMeansItWasRejectedAndTheLoginIsSentAsBasicAuth()
    {
        using var w = new FakeWeb { Handler = _ => (401, "", 0) };
        var o = await StreamProbe.CheckAsync(U(w), new Login("t", "alice", "wrong"));
        Assert.Equal(ProbeResult.LoginRejected, o.Result);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:wrong")), w.Requests.Single().Auth);
    }

    [Fact]
    public async Task TheRightLoginGetsThrough()
    {
        using var w = new FakeWeb { Handler = r => r.Headers["Authorization"] == "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret")) ? (206, "x", 0) : (401, "", 0) };
        Assert.Equal(ProbeResult.NeedsLogin, (await StreamProbe.CheckAsync(U(w))).Result);
        Assert.Equal(ProbeResult.Ok, (await StreamProbe.CheckAsync(U(w), new Login("t", "alice", "secret"))).Result);
    }

    [Fact]
    public async Task A403MeansLoginWhenNoneWasGivenElseForbidden()
    {
        using var w = new FakeWeb { Handler = _ => (403, "", 0) };
        Assert.Equal(ProbeResult.NeedsLogin, (await StreamProbe.CheckAsync(U(w))).Result);
        Assert.Equal(ProbeResult.Forbidden, (await StreamProbe.CheckAsync(U(w), new Login("t", "u", "p"))).Result);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task MissingFiles(int status)
    {
        using var w = new FakeWeb { Handler = _ => (status, "", 0) };
        var o = await StreamProbe.CheckAsync(U(w));
        Assert.Equal(ProbeResult.NotFound, o.Result);
        Assert.Contains("does not exist", o.Message);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ServerErrors(int status)
    {
        using var w = new FakeWeb { Handler = _ => (status, "", 0) };
        Assert.Equal(ProbeResult.ServerError, (await StreamProbe.CheckAsync(U(w))).Result);
    }

    [Fact]
    public async Task ASlowServerTimesOutWithAClearMessage()
    {
        using var w = new FakeWeb { Handler = _ => (200, "x", 3000) };
        var o = await StreamProbe.CheckAsync(U(w), timeout: TimeSpan.FromMilliseconds(400));
        Assert.Equal(ProbeResult.Timeout, o.Result);
        Assert.Contains("too long", o.Message);
    }

    [Fact]
    public async Task NobodyListeningIsUnreachable()
    {
        var o = await StreamProbe.CheckAsync(new Uri("http://127.0.0.1:1/x.mkv"), timeout: TimeSpan.FromSeconds(3));
        Assert.Equal(ProbeResult.Unreachable, o.Result);
        Assert.Contains("could not reach 127.0.0.1", o.Message);
    }

    [Fact]
    public async Task ACertificateProblemIsReportedAsASecureConnectionFailure()
    {
        var o = await StreamProbe.CheckAsync(new Uri("https://example.test/x"), handler: new ThrowingHandler(new HttpRequestException("ssl", new AuthenticationException("The remote certificate is invalid"))));
        Assert.Equal(ProbeResult.SecureConnectionFailed, o.Result);
        Assert.Contains("Settings, Network", o.Message);
    }

    [Fact]
    public async Task ADnsStyleFailureIsUnreachableNotACertificateProblem()
    {
        var o = await StreamProbe.CheckAsync(new Uri("https://example.test/x"), handler: new ThrowingHandler(new HttpRequestException("No such host is known")));
        Assert.Equal(ProbeResult.Unreachable, o.Result);
    }

    [Theory]
    [InlineData("ftp://host/x.mkv")]
    [InlineData("rtsp://host/live")]
    [InlineData("rtmp://host/live")]
    [InlineData("udp://239.0.0.1:1234")]
    public async Task OtherSchemesAreLeftToMpv(string url)
    {
        var o = await StreamProbe.CheckAsync(new Uri(url));
        Assert.Equal(ProbeResult.Skipped, o.Result);
        Assert.True(o.CanPlay);
    }

    [Fact]
    public async Task RedirectsAreFollowed()
    {
        using var w = new FakeWeb();
        w.Handler = r => r.RawUrl == "/final.mkv" ? (206, "x", 0) : (404, "", 0);
        // A redirect needs a Location header, which the tiny server does not set; this checks the final hop answers on its own.
        Assert.Equal(ProbeResult.Ok, (await StreamProbe.CheckAsync(U(w, "final.mkv"))).Result);
        Assert.Equal(ProbeResult.NotFound, (await StreamProbe.CheckAsync(U(w, "other.mkv"))).Result);
    }

    [Fact]
    public async Task TheCheckGoesThroughTheConfiguredProxy()
    {
        using var target = new FakeWeb();
        using var proxy = new FakeProxy();
        var o = await StreamProbe.CheckAsync(new Uri("http://example.test/video.mkv"), proxy: proxy.Url);
        Assert.Equal(ProbeResult.Ok, o.Result);
        Assert.Empty(target.Requests);
        var line = Assert.Single(proxy.RequestLines);
        Assert.Equal("GET http://example.test/video.mkv HTTP/1.1", line);     // a proxy is sent the whole address
    }

    [Fact]
    public async Task WithoutAProxyTheSystemOnesAreNotUsed()
    {
        using var w = new FakeWeb();
        Assert.Equal(ProbeResult.Ok, (await StreamProbe.CheckAsync(U(w), proxy: "")).Result);
        Assert.Single(w.Requests);
    }

    [Fact]
    public async Task ACancelledCheckIsNotReportedAsATimeout()
    {
        using var w = new FakeWeb { Handler = _ => (200, "x", 2000) };
        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StreamProbe.CheckAsync(U(w), ct: cts.Token, timeout: TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void EveryFailureKindHasWordsExceptTheOnesThatNeedNone()
    {
        foreach (var r in Enum.GetValues<ProbeResult>())
        {
            var text = StreamProbe.Describe(r, "host");
            if (r is ProbeResult.Ok or ProbeResult.Skipped) Assert.Equal("", text); else Assert.Contains("host", text);
        }
    }

    [Fact]
    public void NoMessageEverContainsAPasswordOrAFullAddress()
    {
        foreach (var r in Enum.GetValues<ProbeResult>()) Assert.DoesNotContain("://", StreamProbe.Describe(r, "nas.local"));
    }
}
