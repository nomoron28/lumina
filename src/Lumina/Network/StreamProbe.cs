using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;

namespace Lumina.Network;

public enum ProbeResult { Ok, NeedsLogin, LoginRejected, NotFound, Forbidden, Unreachable, Timeout, SecureConnectionFailed, ServerError, Skipped }

public sealed record ProbeOutcome(ProbeResult Result, string Message, int? Status = null)
{
    public bool CanPlay => Result is ProbeResult.Ok or ProbeResult.Skipped;
}

/// <summary>
/// Asks the server about an address before handing it to the player, so the user gets a sentence they can act on
/// ("that needs a login", "address not found") instead of a video that silently never starts.
/// Only http and https are checked; other schemes go straight to mpv.
/// </summary>
public static class StreamProbe
{
    /// <summary>Plain-language text for the failures the user can do something about. Null for outcomes that need no message.</summary>
    public static string Describe(ProbeResult r, string host) => r switch
    {
        ProbeResult.NeedsLogin => $"{host} asks for a login.",
        ProbeResult.LoginRejected => $"{host} did not accept that login. Check the user name and password.",
        ProbeResult.NotFound => $"{host} says that address does not exist (404). Check it for typos.",
        ProbeResult.Forbidden => $"{host} will not let this account open that (403).",
        ProbeResult.Unreachable => $"Lumina could not reach {host}. Check the address and your connection.",
        ProbeResult.Timeout => $"{host} took too long to answer. It may be offline or very busy.",
        ProbeResult.SecureConnectionFailed => $"The secure connection to {host} failed. If you trust this server, you can turn off certificate checking in Settings, Network.",
        ProbeResult.ServerError => $"{host} reported a problem on its side. Try again in a moment.",
        _ => "",
    };

    public static async Task<ProbeOutcome> CheckAsync(Uri uri, Login? login = null, string? proxy = null, bool verifyTls = true,
        TimeSpan? timeout = null, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (uri.Scheme is not ("http" or "https")) return new(ProbeResult.Skipped, "");
        var host = uri.Host;
        var owns = handler == null;
        handler ??= MakeHandler(proxy, verifyTls);
        using var client = new HttpClient(handler, disposeHandler: owns) { Timeout = timeout ?? TimeSpan.FromSeconds(8) };
        try
        {
            // A byte range keeps the check cheap even when the server ignores HEAD.
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            if (login != null) req.Headers.Authorization = new AuthenticationHeaderValue("Basic", CredentialStore.BasicHeader(login.User, login.Password)[6..]);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)resp.StatusCode;
            var result = resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized => login == null ? ProbeResult.NeedsLogin : ProbeResult.LoginRejected,
                HttpStatusCode.Forbidden => login == null ? ProbeResult.NeedsLogin : ProbeResult.Forbidden,
                HttpStatusCode.NotFound or HttpStatusCode.Gone => ProbeResult.NotFound,
                >= HttpStatusCode.InternalServerError => ProbeResult.ServerError,
                _ when resp.IsSuccessStatusCode || code == 416 => ProbeResult.Ok,   // 416: a zero-length file rejects the range, but the server answered
                _ => ProbeResult.ServerError,
            };
            return new(result, Describe(result, host), code);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(ProbeResult.Timeout, Describe(ProbeResult.Timeout, host)); }
        catch (HttpRequestException e) when (HasInner<AuthenticationException>(e)) { return new(ProbeResult.SecureConnectionFailed, Describe(ProbeResult.SecureConnectionFailed, host)); }
        catch (HttpRequestException) { return new(ProbeResult.Unreachable, Describe(ProbeResult.Unreachable, host)); }
    }

    static bool HasInner<T>(Exception e) where T : Exception
    {
        for (var x = e; x != null; x = x.InnerException) if (x is T) return true;
        return false;
    }

    static HttpMessageHandler MakeHandler(string? proxy, bool verifyTls)
    {
        var h = new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5, ConnectTimeout = TimeSpan.FromSeconds(6) };
        if (!string.IsNullOrWhiteSpace(proxy) && Uri.TryCreate(proxy, UriKind.Absolute, out var p)) { h.Proxy = new WebProxy(p); h.UseProxy = true; }
        else h.UseProxy = false;
        if (!verifyTls) h.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return h;
    }

    /// <summary>Header value mpv should send, from a saved login, or null.</summary>
    public static string? AuthorizationFor(Login? l) => l == null ? null : CredentialStore.BasicHeader(l.User, l.Password);
}
