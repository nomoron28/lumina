using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ExternalPlayerTests
{
    const string File = @"D:\Films\Heat (1995).mkv";
    const string Sub = @"C:\Subs\heat.srt";

    [Theory]
    [InlineData("vlc", 754.5, "--no-one-instance --start-time=754.5 --sub-file=\"C:\\Subs\\heat.srt\" --extraintf=http --http-host=127.0.0.1 --http-port=4321 --http-password=pw \"D:\\Films\\Heat (1995).mkv\"", "4321:pw")]
    [InlineData("mpv", 754.5, "--start=754.5 --sub-file=\"C:\\Subs\\heat.srt\" --input-ipc-server=\\\\.\\pipe\\lumina-ext-1 \"D:\\Films\\Heat (1995).mkv\"", "lumina-ext-1")]
    [InlineData("mpvnet", 754.5, "--start=754.5 --sub-file=\"C:\\Subs\\heat.srt\" --input-ipc-server=\\\\.\\pipe\\lumina-ext-1 \"D:\\Films\\Heat (1995).mkv\"", "lumina-ext-1")]
    [InlineData("mpc-hc", 754.5, "\"D:\\Films\\Heat (1995).mkv\" /start 754500 /sub \"C:\\Subs\\heat.srt\"", null)]
    [InlineData("mpc-be", 754.5, "\"D:\\Films\\Heat (1995).mkv\" /start 754500 /sub \"C:\\Subs\\heat.srt\"", null)]
    [InlineData("potplayer", 3754.9, "\"D:\\Films\\Heat (1995).mkv\" /seek=01:02:34 /sub=\"C:\\Subs\\heat.srt\"", null)]
    public void EachPlayerGetsTheStartAndSubtitleInItsOwnSyntax(string id, double start, string expected, string? channel) =>
        Assert.Equal(expected, ExternalPlayer.Arguments(id, File, start, Sub, channel));

    [Theory]
    [InlineData("vlc", "--no-one-instance \"D:\\Films\\Heat (1995).mkv\"")]
    [InlineData("mpv", "\"D:\\Films\\Heat (1995).mkv\"")]
    [InlineData("mpc-hc", "\"D:\\Films\\Heat (1995).mkv\"")]
    [InlineData("potplayer", "\"D:\\Films\\Heat (1995).mkv\"")]
    public void FromTheStartWithoutSubtitlesOnlyTheFileIsPassed(string id, string expected) =>
        Assert.Equal(expected, ExternalPlayer.Arguments(id, File, 0, null));

    [Fact]
    public void AnotherProgramUsesTheTemplate()
    {
        Assert.Equal("-f \"D:\\Films\\Heat (1995).mkv\" -t 12.5 -ms 12500 -s C:\\Subs\\heat.srt",
            ExternalPlayer.Arguments("custom", File, 12.5, Sub, template: "-f \"{file}\" -t {start} -ms {startms} -s {sub}"));
        Assert.Equal("\"D:\\Films\\Heat (1995).mkv\"", ExternalPlayer.Arguments("custom", File, 12.5, null, template: ""));
    }

    [Fact]
    public void ThePlayerIsFoundThroughAppPathsFirstThenProgramFoldersThenPath()
    {
        var vlc = ExternalPlayer.Get("vlc")!;
        var appPath = @"E:\Apps\VLC\vlc.exe";
        Assert.Equal(appPath, ExternalPlayer.Locate(vlc, p => p == appPath, exe => exe == "vlc.exe" ? appPath : null));

        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"VideoLAN\VLC\vlc.exe");
        Assert.Equal(pf, ExternalPlayer.Locate(vlc, p => p == pf, _ => @"E:\gone\vlc.exe"));   // a stale App Paths entry is skipped

        Assert.Null(ExternalPlayer.Locate(vlc, _ => false, _ => null));
    }

    [Fact]
    public void The64BitMpcIsPreferredOverThe32Bit()
    {
        var mpc = ExternalPlayer.Get("mpc-hc")!;
        Assert.EndsWith("mpc-hc64.exe", ExternalPlayer.Locate(mpc, p => p.EndsWith(".exe"), _ => null));
    }

    [Fact]
    public void VlcStatusGivesPositionAndLength()
    {
        Assert.Equal((754.0, 6120.0), ExternalPlayer.ParseVlcStatus("""{"time":754,"length":6120,"state":"playing"}"""));
        Assert.Equal((0.0, null), ExternalPlayer.ParseVlcStatus("""{"time":0,"length":0,"state":"stopped"}"""));   // nothing loaded yet
        Assert.Equal((null, null), ExternalPlayer.ParseVlcStatus("<html>401</html>"));
    }

    [Fact]
    public void MpvRepliesGiveANumberOrNothing()
    {
        Assert.Equal(12.5, ExternalPlayer.ParseMpvReply("""{"data":12.5,"request_id":1,"error":"success"}"""));
        Assert.Null(ExternalPlayer.ParseMpvReply("""{"request_id":1,"error":"property unavailable"}"""));
        Assert.Null(ExternalPlayer.ParseMpvReply("not json"));
        Assert.Null(ExternalPlayer.ParseMpvReply(null));
    }
}
