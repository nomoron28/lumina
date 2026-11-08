using System.IO;
using System.IO.Pipes;
using System.Text;
using Lumina.Windows;
using Xunit;

namespace Lumina.Tests;

public class LaunchArgsTests
{
    static LaunchArgs P(params string[] a) => LaunchArgs.Parse(a, @"C:\work");

    [Fact]
    public void NothingGivenMeansNothingToOpen()
    {
        var a = P();
        Assert.False(a.HasMedia);
        Assert.False(a.Fullscreen);
        Assert.Null(a.StartSeconds);
        Assert.Empty(a.Problems!);
    }

    [Fact]
    public void FilesFoldersAndUrlsAreKeptAndRelativePathsResolveAgainstTheCallersFolder()
    {
        var a = P(@"D:\media\a.mkv", "b.mkv", @"sub\c.mkv", "https://example.com/x.m3u8");
        Assert.Equal(new[] { @"D:\media\a.mkv", @"C:\work\b.mkv", @"C:\work\sub\c.mkv", "https://example.com/x.m3u8" }, a.Paths);
    }

    [Theory]
    [InlineData("--fullscreen")]
    [InlineData("-f")]
    [InlineData("--FULLSCREEN")]
    public void FullscreenSpellings(string flag) => Assert.True(P(flag, "a.mkv").Fullscreen);

    [Theory]
    [InlineData("--paused")]
    [InlineData("--pause")]
    public void PausedSpellings(string flag) => Assert.True(P(flag, "a.mkv").Paused);

    [Theory]
    [InlineData("--start-time=1:30", 90)]
    [InlineData("--start-time=90", 90)]
    [InlineData("--start-time=1h2m3s", 3723)]
    public void StartTimeWithEquals(string arg, double seconds) => Assert.Equal(seconds, P(arg, "a.mkv").StartSeconds);

    [Fact]
    public void StartTimeAsASeparateArgument()
    {
        var a = P("--start-time", "2:00", "a.mkv");
        Assert.Equal(120, a.StartSeconds);
        Assert.Equal(new[] { @"C:\work\a.mkv" }, a.Paths);       // the time was not mistaken for a file
    }

    [Fact]
    public void SubFilesAreCollectedAndMayRepeat()
    {
        var a = P("a.mkv", "--sub-file=eng.srt", "--sub-file", @"D:\subs\spa.srt");
        Assert.Equal(new[] { @"C:\work\eng.srt", @"D:\subs\spa.srt" }, a.SubFiles);
    }

    [Theory]
    [InlineData("--start-time=banana")]
    [InlineData("--start-time=-5")]
    [InlineData("--start-time=")]
    [InlineData("--sub-file=")]
    [InlineData("--nonsense")]
    [InlineData("--volume=50")]
    public void BadOrUnknownOptionsAreReportedNotThrown(string arg)
    {
        var a = P(arg, "a.mkv");
        Assert.Single(a.Problems!);
        Assert.Equal(new[] { @"C:\work\a.mkv" }, a.Paths);       // the file still opens
    }

    [Fact]
    public void DoubleDashEndsOptionsSoOddFileNamesWork()
    {
        var a = P("--", "--weird.mkv", "-f");
        Assert.Equal(2, a.Paths.Count);
        Assert.False(a.Fullscreen);
    }

    [Fact]
    public void ADashAloneAndEmptyArgumentsAreHarmless()
    {
        Assert.Single(P("-", "").Paths);
    }

    [Fact]
    public void ForwardingRoundTripsEveryOption()
    {
        var original = P("--fullscreen", "--paused", "--start-time=1:30", "--sub-file=eng.srt", "a.mkv", "--", "-dash.mkv");
        var again = LaunchArgs.Parse(original.ToArgs());      // what the second launch sends, what the first one parses
        Assert.Equal(original.Paths, again.Paths);
        Assert.Equal(original.Fullscreen, again.Fullscreen);
        Assert.Equal(original.Paused, again.Paused);
        Assert.Equal(original.StartSeconds, again.StartSeconds);
        Assert.Equal(original.SubFiles, again.SubFiles);
        Assert.Contains(@"C:\work\-dash.mkv", again.Paths);
    }

    [Fact]
    public void ForwardingNothingGivesNothing() => Assert.Empty(LaunchArgs.None.ToArgs());

    [Fact]
    public void WithoutAFolderRelativePathsStayAsGiven()
    {
        Assert.Equal(new[] { "a.mkv" }, LaunchArgs.Parse(new[] { "a.mkv" }).Paths);
    }
}

public class SingleInstanceTests
{
    static string Unique() => "LuminaTest." + Guid.NewGuid().ToString("N");

    [Fact]
    public void OnlyTheFirstLaunchOwnsTheInstance()
    {
        var n = Unique();
        using var first = new SingleInstance(n);
        using var second = new SingleInstance(n);
        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void AReleasedInstanceCanBeTakenAgain()
    {
        var n = Unique();
        var first = new SingleInstance(n);
        first.Dispose();
        using var again = new SingleInstance(n);
        Assert.True(again.IsFirst);
    }

    [Fact]
    public async Task ForwardedArgumentsArriveIntactIncludingUnicodeAndSpaces()
    {
        var n = Unique();
        using var first = new SingleInstance(n);
        var got = new TaskCompletionSource<string[]>();
        first.Received += a => got.TrySetResult(a);
        first.Listen();
        var sent = new[] { @"D:\Mu sic\café 日本語.mkv", "--start-time=1:30", "https://example.com/a?b=c&d=e" };
        Assert.True(SingleInstance.Send(sent, n));
        Assert.Equal(sent, await got.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void SendReturnsFalseQuicklyWhenNobodyIsListening()
    {
        var t = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(SingleInstance.Send(new[] { "a.mkv" }, Unique(), timeoutMs: 400));
        Assert.True(t.ElapsedMilliseconds < 2500);
    }

    [Fact]
    public void ManyLaunchesInARowAreAllDelivered()
    {
        var n = Unique();
        using var first = new SingleInstance(n);
        var count = 0;
        first.Received += _ => Interlocked.Increment(ref count);
        first.Listen();
        for (var i = 0; i < 10; i++) Assert.True(SingleInstance.Send(new[] { $"{i}.mkv" }, n));
        Assert.Equal(10, Volatile.Read(ref count));
    }

    [Fact]
    public async Task GarbageOnThePipeIsIgnoredAndTheListenerKeepsWorking()
    {
        var n = Unique();
        using var first = new SingleInstance(n);
        var got = new TaskCompletionSource<string[]>();
        first.Received += a => got.TrySetResult(a);
        first.Listen();
        using (var c = new NamedPipeClientStream(".", n + ".pipe", PipeDirection.InOut))
        {
            c.Connect(2000);
            var b = Encoding.UTF8.GetBytes("this is not json\n");
            c.Write(b, 0, b.Length); c.Flush();
        }
        Thread.Sleep(200);
        Assert.False(got.Task.IsCompleted);
        Assert.True(SingleInstance.Send(new[] { "ok.mkv" }, n));   // still alive afterwards
        await got.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task AClientThatConnectsAndSaysNothingDoesNotBlockLaterLaunches()
    {
        var n = Unique();
        using var first = new SingleInstance(n);
        var got = new TaskCompletionSource<string[]>();
        first.Received += a => got.TrySetResult(a);
        first.Listen();
        using var silent = new NamedPipeClientStream(".", n + ".pipe", PipeDirection.InOut);
        silent.Connect(2000);                                       // holds the single pipe slot and never writes
        Assert.True(SingleInstance.Send(new[] { "later.mkv" }, n, timeoutMs: 8000));   // served once the listener gives up on the silent one
        await got.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

[Collection("db")]   // the Windows credential service is shared, so these run one at a time
public class MaintenanceTests
{
    [Theory]
    [InlineData("--register-types", "register-types")]
    [InlineData("--unregister-types", "unregister-types")]
    [InlineData("--forget-logins", "forget-logins")]
    [InlineData("--REGISTER-TYPES", "register-types")]
    public void MaintenanceFlagsAreRecognised(string flag, string action)
    {
        var a = LaunchArgs.Parse(new[] { flag });
        Assert.Equal(action, a.Action);
        Assert.Empty(a.Problems!);
        Assert.False(a.HasMedia);
    }

    [Fact]
    public void NormalLaunchesHaveNoAction() => Assert.Null(LaunchArgs.Parse(new[] { "a.mkv", "--fullscreen" }).Action);

    [Fact]
    public void EveryDeclaredActionIsHandledAndUnknownOnesFailWithoutThrowing()
    {
        foreach (var a in LaunchArgs.MaintenanceActions) Assert.Equal(a, LaunchArgs.Parse(new[] { "--" + a }).Action);   // the declared list and the parser agree
        Assert.Equal(2, Maintenance.Run("no-such-action"));
    }

    [Fact]
    public void ForgetLoginsRemovesOnlyThisStoresLoginsAndTheirWindowsShareCopies()
    {
        var store = new Lumina.Network.CredentialStore("LuminaTest." + Guid.NewGuid().ToString("N") + ":");
        var server = "luminatest-" + Guid.NewGuid().ToString("N")[..10];
        var bystander = new Lumina.Network.CredentialStore("LuminaBystander." + Guid.NewGuid().ToString("N") + ":");
        try
        {
            store.Save("https://a:443", "u", "p");
            store.Save(@"\\" + server, "u", "p");
            Lumina.Network.CredentialStore.SaveForWindowsShare(server, "u", "p");
            bystander.Save("https://other:443", "x", "y");

            Settle.Until(() => store.List().Count == 2 && Lumina.Network.CredentialStore.HasWindowsShareLogin(server), ok => ok);   // let the writes land
            Assert.Equal(2, Maintenance.ForgetLogins(store));

            Assert.Empty(Settle.Until(() => store.List(), l => l.Count == 0));
            Assert.False(Settle.Until(() => Lumina.Network.CredentialStore.HasWindowsShareLogin(server), h => !h));
            Assert.Single(bystander.List());                       // someone else's entries are never touched
            Assert.Equal(0, Maintenance.ForgetLogins(store));      // and running it again finds nothing
        }
        finally
        {
            foreach (var l in bystander.List()) bystander.Delete(l.Target);
            Lumina.Network.CredentialStore.DeleteForWindowsShare(server);
        }
    }
}
