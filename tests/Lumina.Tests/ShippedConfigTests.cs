using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ShippedConfigTests
{
    // Every line of the mpv.conf we ship must be accepted by the libmpv we ship. A bad line is skipped at runtime
    // (and shown as a toast), so without this test a typo would silently disable part of the tuning.
    [Fact]
    public void ShippedMpvConfHasNoRejectedOptions()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"))) return;
        using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null");
        Assert.Empty(m.BadOptions);
    }
}
