using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lumina;

/// <summary>
/// The picture that follows the pointer over the seek bar. The video is drawn by a native window that WPF elements cannot sit on top of,
/// so the preview is a small window of its own, owned by the main window and never taking focus or clicks.
/// </summary>
public sealed class SeekPreview : Window
{
    readonly Image _image = new() { Width = 240, Stretch = Stretch.Uniform };
    readonly TextBlock _time = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };

    public SeekPreview(Window owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        _time.SetResourceReference(TextBlock.ForegroundProperty, "Lumina.Brush.Text");
        _time.FontFamily = (FontFamily)Application.Current.FindResource("Lumina.Font.Text");
        _time.FontSize = 14; _time.FontWeight = FontWeights.SemiBold;
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = new StackPanel { Children = { new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _image }, _time } },
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Lumina.Brush.SurfaceRaised");
        card.SetResourceReference(Border.BorderBrushProperty, "Lumina.Brush.Border");
        Content = card;
        System.Windows.Automation.AutomationProperties.SetName(this, "Seek preview");
        System.Windows.Automation.AutomationProperties.SetName(_image, "Preview picture");
    }

    const int GwlExStyle = -20, WsExTransparent = 0x20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x80;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        SetWindowLong(h, GwlExStyle, GetWindowLong(h, GwlExStyle) | WsExTransparent | WsExNoActivate | WsExToolWindow);   // clicks and focus pass straight through
    }

    /// <summary>Shows the picture and time with the bottom centre at the given screen point (device pixels). Null picture: just the time.</summary>
    public void ShowAt(ImageSource? picture, string time, double centreXPx, double bottomYPx, double minLeftPx, double maxRightPx)
    {
        _image.Source = picture;
        _image.Visibility = picture == null ? Visibility.Collapsed : Visibility.Visible;
        _time.Text = time;
        var scale = VisualTreeHelper.GetDpi(Owner).DpiScaleX;
        UpdateLayout();
        var wPx = (ActualWidth > 0 ? ActualWidth : 260) * scale;
        var hPx = (ActualHeight > 0 ? ActualHeight : 170) * scale;
        var left = Math.Clamp(centreXPx - wPx / 2, minLeftPx, Math.Max(minLeftPx, maxRightPx - wPx));
        Left = left / scale;
        Top = (bottomYPx - hPx) / scale;
        if (!IsVisible) Show();
    }

    public void HidePreview()
    {
        if (IsVisible) Hide();
    }
}
