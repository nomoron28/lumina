using System.IO;
using Lumina.Diagnostics;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class DiagnosticsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-log-" + Guid.NewGuid().ToString("N"));
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_LOG");

    public DiagnosticsTests() => Environment.SetEnvironmentVariable("LUMINA_LOG", _dir);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LUMINA_LOG", _old);
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void ErrorsAreWrittenWithTheirStackAndCounted()
    {
        Assert.Equal(0, Log.ErrorCount());
        Log.Info("hello");
        Assert.Equal(0, Log.ErrorCount()); // INFO is not an error
        try { throw new InvalidOperationException("boom"); } catch (Exception e) { Log.Error("while testing", e); }
        var text = File.ReadAllText(Log.FilePath);
        Assert.Contains("ERROR while testing", text);
        Assert.Contains("InvalidOperationException: boom", text);
        Assert.Equal(1, Log.ErrorCount());
    }

    [Fact]
    public void UnhandledExceptionsAreLoggedAndTheDialogIsRateLimited()
    {
        var e = new Exception("ui blew up");
        Assert.True(Log.Unhandled("UI thread", e));    // first one tells the user
        Assert.False(Log.Unhandled("UI thread", e));   // a timer throwing every frame must not stack dialogs
        Assert.Equal(2, Log.ErrorCount());             // but both are logged
        Assert.False(Log.Unhandled("AppDomain", e, fatal: true)); // fatal: process is going down, no dialog
    }

    [Fact]
    public void LoggingNeverThrowsEvenWhenTheFolderIsUnusable()
    {
        File.WriteAllText(_dir, "a file where the folder should be"); // CreateDirectory will fail
        Log.Error("still fine");
        Assert.Equal(0, Log.ErrorCount());
        File.Delete(_dir);
    }

    [Fact]
    public void PruneRemovesOnlyOldLogs()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "lumina-20200101.log");
        var recent = Path.Combine(_dir, "lumina-20991231.log");
        File.WriteAllText(old, "x"); File.WriteAllText(recent, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        Log.Prune();
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
