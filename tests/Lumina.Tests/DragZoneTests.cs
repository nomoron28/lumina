using System.Windows.Controls;
using Xunit;

namespace Lumina.Tests;

public class DragZoneTests
{
    static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("STA body failed: " + error.Message, error);
    }

    [Fact]
    public void Empty_space_and_text_drag_but_controls_do_not() => OnSta(() =>
    {
        var text = new TextBlock { Text = "Home" };
        var inner = new TextBlock { Text = "Open" };
        var button = new Button { Content = inner };
        var box = new TextBox();
        var panel = new StackPanel();
        panel.Children.Add(text); panel.Children.Add(button); panel.Children.Add(box);
        var grid = new Grid();
        grid.Children.Add(panel);

        Assert.True(DragZone.IsEmptyArea(grid));
        Assert.True(DragZone.IsEmptyArea(text));
        Assert.False(DragZone.IsEmptyArea(button));
        Assert.False(DragZone.IsEmptyArea(inner)); // a label inside a button is still the button
        Assert.False(DragZone.IsEmptyArea(box));
        Assert.True(DragZone.IsEmptyArea(null));
    });
}
