using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class UserMpvConfTests
{
    [Fact]
    public void KeepsPlainOptionsAndScriptOpts()
    {
        var (kept, skipped) = UserMpvConf.Filter("# note\r\nhwdec=auto\r\n--volume=50\r\nscript-opts=foo-bar=1\r\ndeband\r\n");
        Assert.Equal("hwdec=auto\n--volume=50\nscript-opts=foo-bar=1\ndeband", kept);
        Assert.Empty(skipped);
    }

    [Theory]
    [InlineData("config=yes")] [InlineData("config-dir=C:\\x")] [InlineData("wid=5")] [InlineData("osc=yes")] [InlineData("no-osc")]
    [InlineData("input-conf=a")] [InlineData("input-default-bindings=yes")] [InlineData("input-builtin-bindings=yes")]
    [InlineData("scripts=a.lua")] [InlineData("script=a.lua")] [InlineData("load-scripts=yes")] [InlineData("idle=once")]
    [InlineData("force-window=yes")] [InlineData("ontop=yes")] [InlineData("border=no")] [InlineData("no-border")] [InlineData("fullscreen=yes")]
    [InlineData("window-maximized=yes")] [InlineData("geometry=50%")] [InlineData("autofit=50%")] [InlineData("profile=x")] [InlineData("include=x.conf")]
    public void SkipsOptionsThatBreakLumina(string line)
    {
        var (kept, skipped) = UserMpvConf.Filter("volume=40\n" + line + "\n");
        Assert.Equal("volume=40", kept);
        Assert.Equal(line, Assert.Single(skipped));
    }

    [Fact]
    public void ProfileSectionsAreDroppedToTheEnd()
    {
        var (kept, skipped) = UserMpvConf.Filter("volume=40\n[hq]\nhwdec=no\nvolume=10\n");
        Assert.Equal("volume=40", kept);
        Assert.Equal(3, skipped.Count);
    }
}
