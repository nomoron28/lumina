using Lumina.Player;
using Lumina.Windows;
using Xunit;

namespace Lumina.Tests;

public class WindowFitTests
{
    static readonly MiniGeometry.Box Small = new(0, 0, 1366, 728);   // 1366x768 with a 40 px taskbar

    [Fact]
    public void Default_window_on_a_small_screen_fits_and_is_centred()
    {
        var b = WindowFit.Fit(new(0, 0, 1366, 820), Small, center: true);
        Assert.Equal(new MiniGeometry.Box(0, 0, 1366, 728), b);
    }

    [Fact]
    public void A_window_that_fits_keeps_its_size_and_is_centred()
    {
        var b = WindowFit.Fit(new(0, 0, 1000, 640), new(0, 0, 1920, 1040), center: true);
        Assert.Equal(new MiniGeometry.Box(460, 200, 1000, 640), b);
    }

    [Fact]
    public void A_saved_window_hanging_off_the_bottom_is_moved_back_inside()
    {
        var b = WindowFit.Fit(new(100, 300, 1200, 700), Small, center: false);
        Assert.Equal(new MiniGeometry.Box(100, 28, 1200, 700), b);
    }

    [Fact]
    public void Work_area_on_a_second_monitor_is_respected()
    {
        var b = WindowFit.Fit(new(1900, -50, 1366, 820), new(1920, 0, 1280, 984), center: false);
        Assert.Equal(new MiniGeometry.Box(1920, 0, 1280, 820), b);
    }
}
