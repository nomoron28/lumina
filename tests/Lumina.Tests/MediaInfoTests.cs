using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class MediaInfoTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(2822619, "2.7 MB")]
    [InlineData(150 * 1024 * 1024, "150 MB")]
    [InlineData(5.5 * 1024 * 1024 * 1024, "5.5 GB")]
    public void SizesReadNaturally(double bytes, string text) => Assert.Equal(text, MediaInfo.Size(bytes));

    [Theory]
    [InlineData(1, "Mono")]
    [InlineData(2, "Stereo")]
    [InlineData(6, "5.1")]
    [InlineData(8, "7.1")]
    [InlineData(12, "12 channels")]
    public void ChannelCountsGetFamiliarNames(int n, string text) => Assert.Equal(text, MediaInfo.Channels(n));

    [Theory]
    [InlineData("pq", "HDR10 (PQ)")]
    [InlineData("hlg", "HLG")]
    [InlineData("bt.1886", "SDR")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void DynamicRangeFromTransfer(string? gamma, string text) => Assert.Equal(text, MediaInfo.Dynamic(gamma));

    [Fact]
    public void MissingPropertiesProduceNoRowsInsteadOfBlankOnes()
    {
        var rows = MediaInfo.Build(_ => null);
        Assert.Empty(rows);
    }

    [Fact]
    public void StreamsAreNamedByUrlAndSkipTheFolder()
    {
        var rows = MediaInfo.Build(k => k == "path" ? "https://example.com/a.m3u8" : null);
        Assert.Contains(rows, r => r.Label == "Name" && r.Value == "https://example.com/a.m3u8");
        Assert.DoesNotContain(rows, r => r.Label == "Folder");
    }

    [Fact]
    public void DescribesTheMultiTrackFixtureFromRealMpv()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Fixtures.Eventually(m, "video-params/w");
        var rows = MediaInfo.Build(m.Get);
        string V(string section, string label) => rows.First(r => r.Section == section && r.Label.StartsWith(label)).Value;
        Assert.Equal("multi.mkv", V("File", "Name"));
        Assert.Equal("mkv", V("File", "Container"));
        Assert.Equal("0:12", V("File", "Duration"));
        Assert.Equal("3", V("File", "Chapters"));
        Assert.Equal("640 x 360", V("Video", "Resolution"));
        Assert.Equal("H264", V("Video", "Codec"));
        Assert.Equal(2, rows.Count(r => r.Section == "Audio"));
        Assert.Contains("AAC", V("Audio", "Track 1"));
        Assert.StartsWith("Track 2 (JPN", rows.Where(r => r.Section == "Audio").ElementAt(1).Label);
        Assert.Equal(2, rows.Count(r => r.Section == "Subtitles"));
        Assert.Contains("SUBRIP", V("Subtitles", "Track 1").ToUpperInvariant());
    }
}
