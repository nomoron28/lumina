using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Lumina.Windows;

namespace Lumina.Updates;

public sealed record UpdateInfo(string Version, Uri Download, string Sha256, long Size, string Notes);

public enum UpdateStatus { UpToDate, Available, FeedUnreachable, FeedInvalid, FeedRefused }

public sealed record UpdateResult(UpdateStatus Status, string Message, UpdateInfo? Info = null);

public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Looks for a newer Lumina. The feed is a small JSON file (<c>latest.json</c>, written by scripts\package.ps1) that names the
/// installer and its SHA-256. Nothing is run until the downloaded file matches that checksum exactly.
/// </summary>
public static class UpdateCheck
{
    public const long MaxInstallerBytes = 600L * 1024 * 1024;

    static bool IsLoopback(Uri u) => u.IsLoopback;

    /// <summary>https only, so a network attacker cannot swap the file or the checksum. Local addresses are allowed for testing.</summary>
    public static bool IsAcceptable(Uri u) => u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && IsLoopback(u));

    /// <summary>Parse the feed. Null (with a reason) when it is not a usable feed.</summary>
    public static UpdateInfo? Parse(string json, Uri feedUrl, out string reason)
    {
        reason = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { reason = "The update feed is not in the expected shape."; return null; }
            string? Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var version = Str("version"); var file = Str("file") ?? Str("url"); var sha = Str("sha256")?.Trim().ToLowerInvariant();
            if (!AppInfo.TryParse(version, out _)) { reason = "The update feed has no valid version."; return null; }
            if (string.IsNullOrWhiteSpace(file)) { reason = "The update feed does not say where the installer is."; return null; }
            if (sha is not { Length: 64 } || !sha.All(Uri.IsHexDigit)) { reason = "The update feed has no valid checksum, so Lumina will not download from it."; return null; }
            var size = r.TryGetProperty("size", out var sv) && sv.ValueKind == JsonValueKind.Number && sv.TryGetInt64(out var n2) ? n2 : 0;
            if (!Uri.TryCreate(feedUrl, file, out var dl)) { reason = "The installer address in the feed is not valid."; return null; }
            if (!IsAcceptable(dl)) { reason = "The installer must be offered over https."; return null; }
            if (!string.Equals(dl.Host, feedUrl.Host, StringComparison.OrdinalIgnoreCase)) { reason = "The installer is on a different server than the feed, so Lumina will not use it."; return null; }
            return new UpdateInfo(version!.Trim().TrimStart('v', 'V'), dl, sha, size, Str("notes") ?? "");
        }
        catch (JsonException) { reason = "The update feed could not be read."; return null; }
    }

    public static async Task<UpdateResult> CheckAsync(Uri feedUrl, string currentVersion, HttpMessageHandler? handler = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!IsAcceptable(feedUrl)) return new(UpdateStatus.FeedRefused, "The update address must start with https://.");
        using var client = handler == null ? new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) } : new HttpClient(handler, disposeHandler: false) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        string body;
        try
        {
            using var resp = await client.GetAsync(feedUrl, ct);
            if (!resp.IsSuccessStatusCode) return new(UpdateStatus.FeedUnreachable, $"The update server answered {(int)resp.StatusCode}.");
            body = await resp.Content.ReadAsStringAsync(ct);
            if (body.Length > 64 * 1024) return new(UpdateStatus.FeedInvalid, "The update feed is far larger than expected.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(UpdateStatus.FeedUnreachable, "The update server took too long to answer."); }
        catch (HttpRequestException) { return new(UpdateStatus.FeedUnreachable, "Lumina could not reach the update server."); }

        var info = Parse(body, feedUrl, out var reason);
        if (info == null) return new(UpdateStatus.FeedInvalid, reason);
        var cmp = AppInfo.Compare(info.Version, currentVersion);
        return cmp is > 0
            ? new(UpdateStatus.Available, $"Lumina {info.Version} is available.", info)
            : new(UpdateStatus.UpToDate, $"You are up to date ({currentVersion}).", info);
    }

    /// <summary>
    /// Download to a temporary file, hashing as it arrives. Returns the finished path only if the SHA-256 (and size, when the feed gives one)
    /// match; otherwise the file is deleted and an <see cref="UpdateException"/> explains.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, string folder, IProgress<double>? progress = null, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        if (!IsAcceptable(info.Download)) throw new UpdateException("The installer must be offered over https.");
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(info.Download.LocalPath);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) name = $"Lumina-{info.Version}-setup.exe";
        var final = Path.Combine(folder, name);
        var part = final + ".part";
        using var client = handler == null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var resp = await client.GetAsync(info.Download, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) throw new UpdateException($"The download failed ({(int)resp.StatusCode}).");
            var total = resp.Content.Headers.ContentLength ?? info.Size;
            if (total > MaxInstallerBytes) throw new UpdateException("The installer is larger than Lumina allows.");
            using var sha = SHA256.Create();
            long read = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(part))
            {
                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    read += n;
                    if (read > MaxInstallerBytes) throw new UpdateException("The installer is larger than Lumina allows.");
                    sha.TransformBlock(buf, 0, n, null, 0);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)read / total));
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }
            if (info.Size > 0 && read != info.Size) throw new UpdateException("The download is not the size the update feed promised.");
            var actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (actual != info.Sha256) throw new UpdateException("The downloaded file did not match its checksum, so it was thrown away.");
            File.Move(part, final, overwrite: true);
            return final;
        }
        catch (HttpRequestException) { throw new UpdateException("The download was interrupted."); }
        finally { try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } }
    }

    /// <summary>The command line for the installer: silent, closing Lumina first and starting it again afterwards.</summary>
    public static string InstallerArguments => "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
}
