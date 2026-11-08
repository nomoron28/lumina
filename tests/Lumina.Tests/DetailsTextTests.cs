using System.IO;
using Lumina.Library;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

public class DetailsTextTests
{
    [Theory]
    [InlineData(100, "1 hr 40 min")]
    [InlineData(54, "54 min")]
    [InlineData(120, "2 hr")]
    [InlineData(61, "1 hr 1 min")]
    [InlineData(0, "")]
    [InlineData(-5, "")]
    public void RuntimeReadsLikeAPerson(int minutes, string text) => Assert.Equal(text, DetailsText.Runtime(minutes));

    [Theory]
    [InlineData(3840, 2160, "4K")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1920, 800, "1080p")]          // a widescreen film is still 1080p
    [InlineData(1280, 720, "720p")]
    [InlineData(1280, 534, "720p")]
    [InlineData(854, 480, "480p")]
    [InlineData(640, 360, "360p")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(null, null, "")]
    [InlineData(100, 0, "")]
    public void QualityNamesTheUsualSteps(int? w, int? h, string text) => Assert.Equal(text, DetailsText.Quality(w, h));

    [Fact]
    public void CodecAndChannelNamesAreTheFamiliarOnes()
    {
        Assert.Equal(("H.264", "HEVC", "AV1", ""), (DetailsText.VideoCodec("h264"), DetailsText.VideoCodec("HEVC"), DetailsText.VideoCodec("av1"), DetailsText.VideoCodec(null)));
        Assert.Equal(("AAC", "E-AC3", "TrueHD", "XYZ"), (DetailsText.AudioCodec("aac"), DetailsText.AudioCodec("eac3"), DetailsText.AudioCodec("truehd"), DetailsText.AudioCodec("xyz")));
        Assert.Equal(("2.0", "5.1", "7.1", "", "5 ch"), (DetailsText.ChannelLayout(2), DetailsText.ChannelLayout(6), DetailsText.ChannelLayout(8), DetailsText.ChannelLayout(0), DetailsText.ChannelLayout(5)));
    }

    [Fact]
    public void TheFooterMatchesTheReferenceLine()
    {
        var spec = new FileSpec(348_000_000, "h264", 640, 360, 23.976, "aac", 2, 0.5);
        Assert.Equal(new[] { "332 MB", "H.264 (360p)", "AAC 2.0", "0.5 Mbps", "23.976 fps" }, DetailsText.SpecParts(spec));
    }

    [Fact]
    public void TheFooterLeavesOutWhatIsUnknown()
    {
        Assert.Empty(DetailsText.SpecParts(new FileSpec(0, null, null, null, null, null, null, null)));
        Assert.Equal(new[] { "1.0 KB", "H.264" }, DetailsText.SpecParts(new FileSpec(1024, "h264", null, null, null, null, null, null)));
        Assert.Equal(new[] { "(1080p)" }, DetailsText.SpecParts(new FileSpec(0, null, 1920, 1080, null, null, null, null)));
    }

    [Fact]
    public void MetaLineHasRuntimeYearQualityAndAtMostThreeGenres()
    {
        Assert.Equal(new[] { "1 hr 40 min", "2026", "1080p", "Science Fiction, Mystery, Thriller" },
            DetailsText.MetaParts(100, "2026", "1080p", new[] { "Science Fiction", "Mystery", "Thriller", "Drama" }));
        Assert.Equal(new[] { "2026" }, DetailsText.MetaParts(null, "2026", null, Array.Empty<string>()));
        Assert.Empty(DetailsText.MetaParts(null, null, "", Array.Empty<string>()));
        Assert.Equal(new[] { "54 min", "13 Oct 2026" }, DetailsText.MetaParts(54, "13 Oct 2026", null, Array.Empty<string>()));
    }

    [Theory]
    [InlineData("2026-10-13", "13 Oct 2026")]
    [InlineData("1999-01-05", "5 Jan 1999")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("13/10/2026", "")]
    [InlineData("2026-13-45", "")]
    public void AirDatesReadLikeTheReference(string? iso, string text) => Assert.Equal(text, DetailsText.AirDate(iso));

    [Theory]
    [InlineData(1, 1, "No Strings", false, "E1 - No Strings")]
    [InlineData(2, 5, "Title", true, "S2E5 - Title")]
    [InlineData(1, 3, null, false, "E3")]
    [InlineData(1, 3, "  ", true, "S1E3")]
    [InlineData(null, null, "Only a name", false, "Only a name")]
    public void EpisodeHeadingsLeadWithTheNumber(int? season, int? episode, string? name, bool showSeason, string text) =>
        Assert.Equal(text, DetailsText.EpisodeHeading(season, episode, name, showSeason));

    [Theory]
    [InlineData("Anne Gee Byrd", "AB")]
    [InlineData("Madonna", "M")]
    [InlineData("  ", "")]
    [InlineData("jean-luc picard", "JP")]
    public void InitialsStandInForAMissingPhoto(string name, string initials) => Assert.Equal(initials, DetailsText.Initials(name));

    [Fact]
    public void ScoresShowOnlyWhatExistsInOrder()
    {
        Assert.Equal(new[] { ("TMDB", "71%"), ("IMDb", "6.2"), ("Rotten Tomatoes", "86%") }, DetailsText.Scores(7.1, "6.2", "86%"));
        Assert.Equal(new[] { ("TMDB", "82%") }, DetailsText.Scores(8.2, null, null));
        Assert.Empty(DetailsText.Scores(0, null, ""));
        Assert.Empty(DetailsText.Scores(null, null, null));
    }
}

[Collection("db")]
public class FileProbeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-probe-" + Guid.NewGuid().ToString("N"));
    public FileProbeTests() { Directory.CreateDirectory(_dir); Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db")); Db.Init(); }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    [Fact]
    public void SpecComesFromTheFirstVideoAndAudioTracks()
    {
        var p = new Dictionary<string, string>
        {
            ["track-list/count"] = "4", ["duration"] = "100",
            ["track-list/0/type"] = "video", ["track-list/0/codec"] = "h264", ["track-list/0/demux-w"] = "1920", ["track-list/0/demux-h"] = "1080", ["track-list/0/demux-fps"] = "23.976",
            ["track-list/1/type"] = "audio", ["track-list/1/codec"] = "eac3", ["track-list/1/demux-channel-count"] = "6",
            ["track-list/2/type"] = "audio", ["track-list/2/codec"] = "aac", ["track-list/2/demux-channel-count"] = "2",
            ["track-list/3/type"] = "video", ["track-list/3/codec"] = "mjpeg", ["track-list/3/albumart"] = "yes",
        };
        var s = FileProbe.FromProperties(k => p.GetValueOrDefault(k), 125_000_000);
        Assert.Equal(("h264", 1920, 1080, "eac3", 6), (s.Video, s.Width, s.Height, s.Audio, s.Channels));
        Assert.Equal(23.976, s.Fps);
        Assert.Equal(10.0, s.Mbps!.Value, 3);                     // 125 MB over 100 s
    }

    [Fact]
    public void AMissingPropertyIsJustUnknown()
    {
        var s = FileProbe.FromProperties(_ => null, 0);
        Assert.Equal((null, null, null, null), (s.Video, s.Audio, s.Mbps, s.Fps));
    }

    [Fact]
    public async Task ARealFileIsProbedAndRememberedUntilItChanges()
    {
        if (!Fixtures.HasMpv) return;
        var src = Fixtures.Path_("multi.mkv");
        var copy = Path.Combine(_dir, "probe.mkv");
        File.Copy(src, copy);
        Assert.Null(FileProbe.Cached(copy));
        var spec = await FileProbe.GetAsync(copy);
        Assert.NotNull(spec);
        Assert.Equal(new FileInfo(copy).Length, spec!.Size);
        Assert.NotNull(spec.Video);
        Assert.True(spec.Width > 0 && spec.Height > 0);
        Assert.NotNull(spec.Audio);
        Assert.Equal(spec, FileProbe.Cached(copy));                       // second time comes from the database

        File.AppendAllText(copy, "x");                                     // the file changed: the old answer no longer applies
        Assert.Null(FileProbe.Cached(copy));
    }

    [Fact]
    public async Task AMissingOrBrokenFileGivesNullAndNeverThrows()
    {
        Assert.Null(await FileProbe.GetAsync(Path.Combine(_dir, "nope.mkv")));
        if (!Fixtures.HasMpv) return;
        var junk = Path.Combine(_dir, "junk.mkv");
        File.WriteAllText(junk, "this is not a video");
        Assert.Null(FileProbe.Read(junk, TimeSpan.FromSeconds(4)));
    }
}

[Collection("db")]
public class PlaylistAppendTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-pl-" + Guid.NewGuid().ToString("N"));
    public PlaylistAppendTests() { Directory.CreateDirectory(_dir); Environment.SetEnvironmentVariable("LUMINA_DB", Path.Combine(_dir, "t.db")); Db.Init(); }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    [Fact]
    public void AppendingKeepsOrderAndExistingItems()
    {
        var id = Db.SavePlaylist("Night", new[] { new QueueItem("a.mkv", "A"), new QueueItem("b.mkv", "B") });
        Assert.Equal(2, Db.AppendToPlaylist(id, new[] { new QueueItem("c.mkv", "C"), new QueueItem("d.mkv", "D") }));
        Assert.Equal(new[] { "A", "B", "C", "D" }, Db.PlaylistItems(id).Select(i => i.Title));
        Assert.Equal(0, Db.AppendToPlaylist(id, Array.Empty<QueueItem>()));
        Assert.Equal(4, Db.Playlists().Single().Count);
    }

    [Fact]
    public void AppendingToAnEmptyPlaylistStartsAtTheTop()
    {
        var id = Db.SavePlaylist("Empty", Array.Empty<QueueItem>());
        Db.AppendToPlaylist(id, new[] { new QueueItem("x.mkv", "X") });
        Assert.Equal(new[] { "X" }, Db.PlaylistItems(id).Select(i => i.Title));
    }

    [Theory]
    [InlineData("60%", true)]
    [InlineData("100%", true)]
    [InlineData("59%", false)]
    [InlineData("0%", false)]
    [InlineData("n/a", false)]
    public void RottenTomatoesLogoFollowsTheSixtyPercentRule(string score, bool fresh) => Assert.Equal(fresh, DetailsText.IsFresh(score));

    [Fact]
    public void TheGeneratedRatingLogosAreValidGeometry()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lumina", "Theme", "RatingLogos.xaml"));
        var paths = System.Text.RegularExpressions.Regex.Matches(xaml, "Geometry=\"([^\"]+)\"");
        Assert.True(paths.Count >= 3);
        foreach (System.Text.RegularExpressions.Match m in paths) Assert.False(System.Windows.Media.Geometry.Parse(m.Groups[1].Value).Bounds.IsEmpty);
    }
}
