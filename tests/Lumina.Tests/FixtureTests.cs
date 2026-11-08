using Xunit;

namespace Lumina.Tests;

// Proves the fixtures contain what later milestones rely on, so a broken fixture fails here and not as a mystery elsewhere.
public class FixtureTests
{
    [Fact]
    public void MultiHasTwoAudioTwoSubtitleTracksAndThreeChapters()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Assert.Equal("3", m.Get("chapter-list/count"));
        var types = Enumerable.Range(0, int.Parse(m.Get("track-list/count")!)).Select(i => m.Get($"track-list/{i}/type")).ToList();
        Assert.Equal(1, types.Count(t => t == "video"));
        Assert.Equal(2, types.Count(t => t == "audio"));
        Assert.Equal(2, types.Count(t => t == "sub"));
        Assert.Equal("jpn", m.Get("track-list/2/lang"));
    }

    [Fact]
    public void SurroundFixtureReportsSixChannels()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("surround51.mkv");
        Assert.Equal("6", Fixtures.Eventually(m, "audio-params/channel-count"));
    }

    [Fact]
    public void InterlacedTransportStreamIsDetectedAsTransportStream()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("interlaced.ts");
        Assert.Contains("mpegts", m.Get("file-format"));
        Assert.Equal("576", Fixtures.Eventually(m, "height"));
    }

    [Fact]
    public void HdrFixtureCarriesPqTransfer()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("hdr10.mkv");
        Assert.Equal("pq", Fixtures.Eventually(m, "video-params/gamma"));
    }

    [Fact]
    public void OscScriptLoadsWithoutErrorAndAnnouncesItself()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        Assert.Equal("yes", m.Get("user-data/lumina/ready")?.Trim('"')); // user-data strings come back JSON-quoted
        Assert.Equal("", m.Get("user-data/lumina/menu")?.Trim('"'));
    }

    [Fact]
    public void ControllerAcceptsAQueueFromTheHostAndReportsItBack()
    {
        if (!Fixtures.HasMpv) return;
        using var m = Fixtures.Open("multi.mkv");
        m.Command("script-message", "lumina-queue", "{\"cur\":1,\"shuffle\":true,\"repeat\":\"all\",\"items\":[\"a\",\"b\",\"c\"]}");
        Assert.Equal("3", Fixtures.Eventually(m, "user-data/lumina/queue_count")?.Trim('"'));
        Assert.Equal("1", m.Get("user-data/lumina/queue_cur")?.Trim('"'));
        Assert.Equal("all", m.Get("user-data/lumina/queue_repeat")?.Trim('"'));
        Assert.Equal("true", m.Get("user-data/lumina/queue_shuffle")?.Trim('"'));
        m.Command("script-message", "lumina-queue", "not json");   // garbage must not break the controller
        m.Command("script-message", "lumina-queue", "{\"cur\":-1,\"items\":[]}");
        Thread.Sleep(200);
        Assert.Equal("0", m.Get("user-data/lumina/queue_count")?.Trim('"'));
    }
}
