using System.IO;
using System.Reflection;
using Lumina.Library;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>A network that silently drops api.themoviedb.org never refuses the connection: a request that hangs must switch hosts like a refused one does.</summary>
[Collection("db")]
public class TmdbHangTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-hang-" + Guid.NewGuid().ToString("N"));
    readonly FakeWeb _hung = new(), _ok = new();

    public TmdbHangTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "cfg.json"), """{"tmdbKey":"good"}""");
        Environment.SetEnvironmentVariable("LUMINA_CONFIG", Path.Combine(_dir, "cfg.json"));
        Environment.SetEnvironmentVariable("LUMINA_TMDB_HOSTS", _hung.Base + "3/," + _ok.Base + "3/");
        Environment.SetEnvironmentVariable("LUMINA_TMDB_TIMEOUT_MS", "300");
        typeof(Tmdb).GetField("_host", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);
        _hung.Handler = _ => (200, "{}", 5000);
    }

    public void Dispose()
    {
        _hung.Dispose(); _ok.Dispose();
        foreach (var v in new[] { "LUMINA_TMDB_HOSTS", "LUMINA_TMDB_TIMEOUT_MS", "LUMINA_CONFIG" }) Environment.SetEnvironmentVariable(v, null);
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task AHungHostFallsBackToTheOther()
    {
        _ok.Handler = _ => (200, """{"results":[{"id":5,"title":"Film","release_date":"2020-01-01"}]}""", 0);
        var found = await Tmdb.SearchMany("movie", "Film");
        Assert.Equal("Film", found.Single().Meta.Title);
        Assert.Single(_hung.Requests);
        Assert.Single(_ok.Requests);
    }

    [Fact]
    public async Task BothHostsHungReportsATimeout()
    {
        Environment.SetEnvironmentVariable("LUMINA_TMDB_HOSTS", _hung.Base + "3/," + _hung.Base + "3/");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Tmdb.SearchMany("movie", "Film"));
        Assert.Equal(2, _hung.Requests.Count);
    }
}
