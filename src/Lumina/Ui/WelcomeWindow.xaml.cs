using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lumina.Settings;

namespace Lumina;

/// <summary>
/// The first-run welcome: the logo, one line, and three steps. The first step does the real first thing (adds a media folder);
/// the last lets the user choose appearance and accent straight away. Nothing is required.
/// </summary>
public partial class WelcomeWindow : Window
{
    readonly Func<string?> _pickFolder;
    TextBlock? _folderNote;
    Button? _folderBtn;

    public WelcomeWindow(Window? owner, Func<string?> pickFolder)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;
        _pickFolder = pickFolder;
        Build();
    }

    static Style S(string key) => (Style)Application.Current.FindResource(key);

    Grid Row(int number, string title, string body, UIElement? extra)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("Lumina.Brush.SurfaceRaised"),
            Child = new TextBlock
            {
                Text = number.ToString(), FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Lumina.Font.Display"), FontWeight = FontWeights.SemiBold, FontSize = 17,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Lumina.Brush.Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        System.Windows.Automation.AutomationProperties.SetName(badge, "Step " + number);
        var text = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, Style = S("Lumina.Text.Label"), FontWeight = FontWeights.Medium, FontSize = 17 });
        text.Children.Add(new TextBlock { Text = body, Style = S("Lumina.Text.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 21 });
        if (extra != null) { ((FrameworkElement)extra).Margin = new Thickness(0, 12, 0, 0); text.Children.Add(extra); }
        Grid.SetColumn(text, 1);
        g.Children.Add(badge); g.Children.Add(text);
        return g;
    }

    void Build()
    {
        var steps = Onboarding.Steps;

        // 1: the first real action
        _folderBtn = new Button { Content = "Choose a folder...", Style = S("Lumina.Btn.Primary"), HorizontalAlignment = HorizontalAlignment.Left };
        _folderNote = new TextBlock { Style = S("Lumina.Text.Caption"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _folderBtn.Click += (_, _) =>
        {
            var path = _pickFolder();
            if (path == null) return;
            _folderNote!.Text = $"Added {path}. Lumina is reading it now; your files are not touched.";
            _folderNote.Visibility = Visibility.Visible;
            _folderBtn!.Content = "Add another folder...";
        };
        Rows.Children.Add(Row(1, steps[0].Title, steps[0].Body, new StackPanel { Children = { _folderBtn, _folderNote } }));

        // 2: the optional keys
        Rows.Children.Add(Row(2, steps[1].Title, steps[1].Body, null));

        // 3: appearance and accent, applied as they are chosen
        var theme = new StackPanel { Orientation = Orientation.Horizontal };
        var current = AppSettings.Get("general.theme");
        foreach (var (v, label) in new[] { ("dark", "Dark"), ("light", "Light"), ("system", "Match Windows") })
        {
            var rb = new RadioButton { Content = label, GroupName = "welcome-theme", Style = S("Lumina.Seg.Item"), IsChecked = v == current };
            System.Windows.Automation.AutomationProperties.SetName(rb, "Theme " + label);
            rb.Click += (_, _) => { AppSettings.Set("general.theme", v); Lumina.Themes.ThemeManager.Apply(); };
            theme.Children.Add(rb);
        }
        var segmented = new Border { Style = S("Lumina.Group"), CornerRadius = new CornerRadius(12), Padding = new Thickness(3), Child = theme, HorizontalAlignment = HorizontalAlignment.Left };
        var accents = SettingsView.AccentRow(AppSettings.Get("general.accent"), hex => { AppSettings.Set("general.accent", hex); Accent.Apply(hex); }, "welcome-accent");
        accents.Margin = new Thickness(0, 14, 0, 0);
        Rows.Children.Add(Row(3, steps[2].Title, steps[2].Body, new StackPanel { Children = { segmented, accents } }));
    }

    void OnKey(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
    void OnSkip(object sender, RoutedEventArgs e) => Close();
    void OnStart(object sender, RoutedEventArgs e) => Close();
}
