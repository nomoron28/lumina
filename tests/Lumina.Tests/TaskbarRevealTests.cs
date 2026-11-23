using Lumina.Windows;
using Xunit;

namespace Lumina.Tests;

/// <summary>
/// Reported: with an auto-hiding taskbar, any window opened over the maximized or fullscreen player closed again and Lumina came
/// back in front. Revealing the taskbar ended with Lumina taking the focus back, whatever the user had opened from it.
/// </summary>
public class TaskbarRevealTests
{
    static readonly IntPtr Tray = new(100), Other = new(200);

    [Fact]
    public void TakesTheFocusBackOnlyWhenTheTaskbarIsStillInFront() => Assert.True(TaskbarReveal.ShouldTakeBack(Tray, Tray));

    [Fact]
    public void LeavesAWindowTheUserOpenedInFront() => Assert.False(TaskbarReveal.ShouldTakeBack(Other, Tray));

    [Fact]
    public void DoesNothingWithoutATaskbar() => Assert.False(TaskbarReveal.ShouldTakeBack(IntPtr.Zero, IntPtr.Zero));
}
