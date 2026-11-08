using Lumina.Settings;
using Xunit;

namespace Lumina.Tests;

public class OnboardingTests
{
    [Fact]
    public void TheTourIsShortAndEveryStepSaysSomething()
    {
        Assert.InRange(Onboarding.Steps.Count, 3, 7);
        Assert.All(Onboarding.Steps, s => { Assert.False(string.IsNullOrWhiteSpace(s.Title)); Assert.True(s.Body.Length > 30); });
        Assert.Equal(Onboarding.Steps.Count, Onboarding.Steps.Select(s => s.Title).Distinct().Count());
    }

    [Fact]
    public void TheTourIsOnForANewInstallAndCanBeTurnedBackOn()
    {
        var d = Catalog.Find("general.tour")!;
        Assert.Equal("true", d.Default);
        Assert.Equal(Kind.Toggle, d.Kind);
    }
}
