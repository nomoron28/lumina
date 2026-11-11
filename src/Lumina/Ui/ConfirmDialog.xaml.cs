using System.Windows;
using System.Windows.Input;

namespace Lumina;

/// <summary>Themed replacement for MessageBox: a title, a sentence or two, and one or two buttons.</summary>
public partial class ConfirmDialog : Window
{
    ConfirmDialog(string title, string body, string okText, bool showCancel, UIElement? extra = null, double width = 460)
    {
        InitializeComponent();
        Width = width;
        Extra.Content = extra;
        BodyText.Visibility = body.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Text = title;
        BodyText.Text = body;
        OkBtn.Content = okText;
        CancelBtn.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        // Dragging the card moves the dialog, since it has no title bar.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => { if (extra is System.Windows.Controls.TextBox t) { t.Focus(); t.SelectAll(); } else OkBtn.Focus(); };
    }

    internal void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
    void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>True if the user confirmed.</summary>
    /// <summary>A real question (not an "are you sure"), so it is asked even when confirmations are turned off.</summary>
    public static bool Question(Window? owner, string title, string body, string okText) =>
        new ConfirmDialog(title, body, okText, showCancel: true) { Owner = owner }.ShowDialog() == true;

    public static bool Ask(Window? owner, string title, string body, string okText = "Remove")
    {
        if (!Lumina.Settings.AppSettings.Bool("general.confirm")) return true;
        var d = new ConfirmDialog(title, body, okText, showCancel: true) { Owner = owner };
        return d.ShowDialog() == true;
    }

    /// <summary>Asks for one line of text. Null if cancelled or left empty.</summary>
    public static string? Prompt(Window? owner, string title, string body, string okText, string placeholder, string initial = "")
    {
        var box = new System.Windows.Controls.TextBox { Style = (Style)Application.Current.FindResource("Lumina.Input"), Tag = placeholder, Text = initial };
        System.Windows.Automation.AutomationProperties.SetName(box, placeholder);
        var d = new ConfirmDialog(title, body, okText, showCancel: true, extra: box, width: 560) { Owner = owner };
        return d.ShowDialog() == true && box.Text.Trim() is { Length: > 0 } t ? t : null;
    }

    /// <summary>Shows a list and returns the index of the chosen row, or -1 if cancelled. The button stays off until something is picked.</summary>
    public static int Choose(Window? owner, string title, string body, IReadOnlyList<string> rows, string okText)
    {
        var list = new System.Windows.Controls.ListBox { ItemsSource = rows, MaxHeight = 300, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(list, "Choices");
        var d = new ConfirmDialog(title, body, okText, showCancel: true, extra: list, width: 720) { Owner = owner };
        d.OkBtn.IsEnabled = false;
        list.SelectionChanged += (_, _) => d.OkBtn.IsEnabled = list.SelectedIndex >= 0;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedIndex >= 0) d.DialogResult = true; };
        return d.ShowDialog() == true ? list.SelectedIndex : -1;
    }

    /// <summary>Asks for a PIN in a masked box. Null if cancelled or left empty.</summary>
    public static string? AskPin(Window? owner, string title, string body, string okText = "OK")
    {
        var pass = new Wpf.Ui.Controls.PasswordBox { RevealButtonEnabled = false, PlaceholderText = "PIN" };
        System.Windows.Automation.AutomationProperties.SetName(pass, "PIN");
        var d = new ConfirmDialog(title, body, okText, showCancel: true, extra: pass, width: 440) { Owner = owner };
        d.Loaded += (_, _) => pass.Focus();
        return d.ShowDialog() == true && pass.Password.Length > 0 ? pass.Password : null;
    }

    /// <summary>Wait for one key press and return its mpv name (SPACE, k, Ctrl+s, F5). Null when cancelled with Esc.</summary>
    public static string? CaptureKey(Window? owner, string actionLabel)
    {
        var prompt = new System.Windows.Controls.TextBlock
        {
            Text = "Press the key you want", Style = (Style)Application.Current.FindResource("Lumina.Text.Label"), FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 14),
        };
        System.Windows.Automation.AutomationProperties.SetName(prompt, "Press the key you want");
        var d = new ConfirmDialog("Key for " + actionLabel, "Esc cancels.", "Cancel", showCancel: false, extra: prompt, width: 460) { Owner = owner };
        string? got = null;
        d.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            if (key == System.Windows.Input.Key.Escape) { d.DialogResult = false; e.Handled = true; return; }
            if (key == System.Windows.Input.Key.Space) { got = "SPACE"; d.DialogResult = true; e.Handled = true; return; }
            if (Lumina.Player.MpvKeys.Name(key, System.Windows.Input.Keyboard.Modifiers) is { } n) { got = n; d.DialogResult = true; e.Handled = true; }
        };
        d.PreviewTextInput += (_, e) => { if (Lumina.Player.MpvKeys.Char(e.Text) is { } c) { got = c; d.DialogResult = true; e.Handled = true; } };
        d.ShowDialog();
        return got;
    }

    public sealed record Credentials(string User, string Password, bool Remember);

    /// <summary>Ask for a user name and password. Null if cancelled.</summary>
    public static Credentials? SignIn(Window? owner, string host, bool rejected, string? knownUser = null)
    {
        var user = new System.Windows.Controls.TextBox { Style = (Style)Application.Current.FindResource("Lumina.Input"), Tag = "User name", Text = knownUser ?? "" };
        System.Windows.Automation.AutomationProperties.SetName(user, "User name");
        var pass = new Wpf.Ui.Controls.PasswordBox { Margin = new Thickness(0, 10, 0, 0), RevealButtonEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(pass, "Password");
        var remember = new System.Windows.Controls.CheckBox { Content = "Remember on this PC", IsChecked = true, Margin = new Thickness(0, 12, 0, 0), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Lumina.Brush.TextMuted") };
        var panel = new System.Windows.Controls.StackPanel { Children = { user, pass, remember } };
        var body = rejected ? $"{host} did not accept that login. Try again." : $"{host} asks for a login. It is kept in Windows Credential Manager, never in Lumina's files.";
        var d = new ConfirmDialog("Sign in to " + host, body, "Sign in", showCancel: true, extra: panel, width: 520) { Owner = owner };
        return d.ShowDialog() == true && user.Text.Trim().Length > 0 ? new Credentials(user.Text.Trim(), pass.Password, remember.IsChecked == true) : null;
    }

    /// <summary>Address box with a list of recent addresses underneath. Null if cancelled or empty.</summary>
    public static string? PromptWithRecents(Window? owner, string title, string body, string okText, string placeholder, IReadOnlyList<string> recents, Action? clearRecents = null)
    {
        var box = new System.Windows.Controls.TextBox { Style = (Style)Application.Current.FindResource("Lumina.Input"), Tag = placeholder };
        System.Windows.Automation.AutomationProperties.SetName(box, placeholder);
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(box);
        ConfirmDialog? d = null;
        if (recents.Count > 0)
        {
            var list = new System.Windows.Controls.ListBox { ItemsSource = recents, MaxHeight = 170, Margin = new Thickness(0, 14, 0, 0), Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
            System.Windows.Automation.AutomationProperties.SetName(list, "Recent addresses");
            list.SelectionChanged += (_, _) => { if (list.SelectedItem is string s) { box.Text = s; box.CaretIndex = s.Length; } };
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is string) d?.OnOk(null!, null!); };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Recent", Style = (Style)Application.Current.FindResource("Lumina.Text.Caption"), Margin = new Thickness(2, 14, 0, 0) });
            panel.Children.Add(list);
            if (clearRecents != null)
            {
                var clear = new System.Windows.Controls.Button { Content = "Clear list", Style = (Style)Application.Current.FindResource("Lumina.Btn.Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                clear.Click += (_, _) => { clearRecents(); list.ItemsSource = Array.Empty<string>(); };
                panel.Children.Add(clear);
            }
        }
        d = new ConfirmDialog(title, body, okText, showCancel: true, extra: panel, width: 600) { Owner = owner };
        return d.ShowDialog() == true && box.Text.Trim() is { Length: > 0 } t ? t : null;
    }

    /// <summary>One-button dialog around arbitrary content, such as the shortcuts list.</summary>
    public static void ShowContent(Window? owner, string title, UIElement content, double width = 640)
    {
        var d = new ConfirmDialog(title, "", "Done", showCancel: false, extra: content, width: width) { Owner = owner };
        d.ShowDialog();
    }

    /// <summary>One-button message, for errors the user must see.</summary>
    public static void Tell(Window? owner, string title, string body)
    {
        var d = new ConfirmDialog(title, body, "OK", showCancel: false) { Owner = owner };
        d.ShowDialog();
    }
}
