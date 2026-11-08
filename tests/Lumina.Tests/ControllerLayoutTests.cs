using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ControllerLayoutTests
{
    [Theory]
    [InlineData(null, "chap+cc+audio+speed+queue+gear+snap")]
    [InlineData("", "chap+cc+audio+speed+queue+gear+snap")]
    [InlineData("garbage", "chap+cc+audio+speed+queue+gear+snap")]
    [InlineData("speed+cc+gear", "speed+cc+gear")]
    [InlineData("speed+cc", "speed+cc+gear")]                    // the gear can never be taken away
    [InlineData("cc+cc+audio+gear", "cc+audio+gear")]            // repeats dropped
    [InlineData("cc,nope,audio+gear", "cc+audio+gear")]          // unknown dropped, comma accepted
    [InlineData(" snap + gear ", "snap+gear")]
    public void ParsingKeepsOnlyKnownButtonsAndAlwaysTheGear(string? text, string expected) =>
        Assert.Equal(expected, ControllerLayout.Format(ControllerLayout.Parse(text)));

    [Fact]
    public void DefaultIsDefaultAndOtherOrdersAreNot()
    {
        Assert.True(ControllerLayout.IsDefault(null));
        Assert.True(ControllerLayout.IsDefault("chap+cc+audio+speed+queue+gear+snap"));
        Assert.False(ControllerLayout.IsDefault("cc+gear"));
        Assert.False(ControllerLayout.IsDefault("gear+snap+queue+speed+audio+cc+chap"));
    }

    [Fact]
    public void ParseThenFormatIsStable()
    {
        var once = ControllerLayout.Format(ControllerLayout.Parse("audio+cc+speed"));
        Assert.Equal(once, ControllerLayout.Format(ControllerLayout.Parse(once)));
    }
}
