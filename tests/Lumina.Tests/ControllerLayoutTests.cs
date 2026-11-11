using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ControllerLayoutTests
{
    [Theory]
    [InlineData(null, "cc+audio+speed+queue+chap+snap+gear")]
    [InlineData("", "cc+audio+speed+queue+chap+snap+gear")]
    [InlineData("garbage", "cc+audio+speed+queue+chap+snap+gear")]
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
        Assert.True(ControllerLayout.IsDefault("cc+audio+speed+queue+chap+snap+gear"));
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
