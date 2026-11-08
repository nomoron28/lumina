using System.Windows;

namespace Lumina;

/// <summary>
/// True while the user is driving with the keyboard. Focus rings read this so they show for Tab and arrow keys
/// but not as a leftover after a mouse click. Inherited, so setting it on the window reaches every template.
/// </summary>
public static class FocusMode
{
    public static readonly DependencyProperty KeyboardProperty = DependencyProperty.RegisterAttached(
        "Keyboard", typeof(bool), typeof(FocusMode),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetKeyboard(DependencyObject d) => (bool)d.GetValue(KeyboardProperty);
    public static void SetKeyboard(DependencyObject d, bool v) => d.SetValue(KeyboardProperty, v);
}
