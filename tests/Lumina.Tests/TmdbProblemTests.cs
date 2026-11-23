using System.Net;
using System.Net.Http;
using Lumina.Metadata;
using Xunit;

namespace Lumina.Tests;

/// <summary>A rejected TMDB key used to show .NET's own "Response status code does not indicate success: 401 (Unauthorized)".</summary>
public class TmdbProblemTests
{
    static HttpRequestException Status(HttpStatusCode? code) => new("raw .NET text", null, code);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "TMDB did not accept your key. Check it in Settings, Accounts")]
    [InlineData(HttpStatusCode.Forbidden, "TMDB did not accept your key. Check it in Settings, Accounts")]
    [InlineData(HttpStatusCode.TooManyRequests, "TMDB is busy right now. Try again in a minute")]
    [InlineData(HttpStatusCode.BadGateway, "TMDB answered with error 502. Try again later")]
    public void SaysWhatToDo(HttpStatusCode code, string want) => Assert.Equal(want, Tmdb.Problem(Status(code)));

    [Fact]
    public void NoAnswerAtAllIsAConnectionProblem() => Assert.Equal("Could not reach TMDB. Check your connection", Tmdb.Problem(Status(null)));

    [Fact]
    public void NeverShowsTheRawText() =>
        Assert.All(new HttpStatusCode?[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, null },
            c => Assert.DoesNotContain("raw", Tmdb.Problem(Status(c))));
}
