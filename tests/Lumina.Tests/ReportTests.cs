using System.IO;
using Lumina.Diagnostics;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class ReportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-report-" + Guid.NewGuid().ToString("N"));
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_LOG");

    public ReportTests() => Environment.SetEnvironmentVariable("LUMINA_LOG", _dir);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_LOG", _old);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Theory]
    [InlineData(@"Could not open C:\Users\Someone\Videos\My Holiday (2020).mkv", "Could not open <path>.mkv")]
    [InlineData(@"Scan of \\NAS\share\Films\Private Film.mp4 failed", "Scan of <path>.mp4 failed")]
    [InlineData("GET https://api.example.org/3/search?api_key=abc123&query=x", "GET https://api.example.org/3/search?api_key=<hidden>&query=x")]
    [InlineData("token: eyJhbGciOi.payload.sig", "token=<hidden>")]
    [InlineData("INFO Lumina started, Microsoft Windows NT 10.0.26100.0", "INFO Lumina started, Microsoft Windows NT 10.0.26100.0")]
    public void PathsAndSecretsAreTakenOut(string line, string expected) => Assert.Equal(expected, Report.Redact(line));

    [Fact]
    public void TheUserNameIsTakenOut()
    {
        var name = Environment.UserName;
        if (name.Length <= 2) return;
        Assert.DoesNotContain(name, Report.Redact($"profile of {name} loaded"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheIssueLinkCarriesVersionAndWindowsButNoLog()
    {
        var url = Report.IssueUrl("0.1.1", "Windows 10.0.26100");
        Assert.StartsWith(Report.NewIssue + "?labels=bug&body=", url);
        var body = Uri.UnescapeDataString(url[(url.IndexOf("body=") + 5)..]);
        Assert.Contains("**Lumina** 0.1.1", body);
        Assert.Contains("Windows 10.0.26100", body);
        Assert.True(url.Length < 2000, "the link stays short enough for every browser");
    }

    [Fact]
    public void TheLogTailIsTheEndOfTodaysLogRedacted()
    {
        Assert.Equal("", Report.LogTail());                      // no log yet
        for (var i = 0; i < 100; i++) Log.Info($"line {i}");
        Log.Error(@"Could not play D:\Films\Secret.mkv");
        var tail = Report.LogTail(10);
        Assert.Equal(10, tail.Split('\n').Length);
        Assert.Contains("line 99", tail);
        Assert.DoesNotContain("line 50", tail);
        Assert.Contains("<path>.mkv", tail);
        Assert.DoesNotContain("Secret", tail);
    }
}
