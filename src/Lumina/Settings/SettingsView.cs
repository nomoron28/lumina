using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumina.Metadata;

namespace Lumina.Settings;

/// <summary>
/// The settings page, built from <see cref="Catalog"/>: category list and search on the left, rows on the right.
/// Every change is written to config.json straight away and announced through <see cref="Changed"/>.
/// </summary>
public sealed class SettingsView : UserControl
{
    public event Action<string>? Changed;          // setting key
    public event Action<string>? ActionRequested;  // SettingDef.Action of a button row

    readonly StackPanel _rows = new() { Margin = new Thickness(0, 0, 0, 56) };
    readonly System.Windows.Controls.TextBox _search;
    readonly TextBlock _status = new() { Style = Res<Style>("Lumina.Text.Muted"), Margin = new Thickness(0, 0, 0, 14) };
    readonly Dictionary<string, System.Windows.Controls.RadioButton> _nav = new();
    string _category = Environment.GetEnvironmentVariable("LUMINA_CATEGORY") is { Length: > 0 } c0 && Catalog.Categories.Any(c => c.Id == c0) ? c0 : "general";

    static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public SettingsView()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(0, 0, 20, 0) };
        _search = new System.Windows.Controls.TextBox { Style = Res<Style>("Lumina.Search"), Tag = "Search settings", Margin = new Thickness(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search settings");
        _search.TextChanged += (_, _) => Rebuild();
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        var navStack = new StackPanel();
        string? group = null;
        foreach (var c in Catalog.Categories)
        {
            if (c.Group != group)
            {
                group = c.Group;
                navStack.Children.Add(new TextBlock { Text = group, Style = Res<Style>("Lumina.Text.Caption"), Margin = new Thickness(12, navStack.Children.Count == 0 ? 0 : 16, 0, 6) });
            }
            var rb = new System.Windows.Controls.RadioButton
            {
                Content = c.Title, GroupName = "settings-nav", Style = Res<Style>("Lumina.Nav.Item"), Margin = new Thickness(0, 0, 0, 2),
                Tag = new Wpf.Ui.Controls.SymbolIcon { Symbol = Enum.Parse<Wpf.Ui.Controls.SymbolRegular>(c.Symbol), FontSize = 18 },
                IsChecked = c.Id == _category,
            };
            var id = c.Id;
            rb.Click += (_, _) => { _category = id; _search.Text = ""; Rebuild(); };
            _nav[id] = rb;
            navStack.Children.Add(rb);
        }
        left.Children.Add(new ScrollViewer { Content = navStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(left);

        // Fixed-width column up to 820 so every category lines up, shrinking with the window below that.
        var page = new Grid { Margin = new Thickness(0, 0, 18, 0) };      // its own gutter, so the scroll bar never sits on the cards
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 820 });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var body = new StackPanel { Children = { _status, _rows } };
        page.Children.Add(body);
        var right = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;
        Rebuild();
    }

    /// <summary>Show a short message above the rows (validation, confirmation).</summary>
    public void Say(string text) => _status.Text = text;

    /// <summary>Redraw every row from the stored values, for example after "Reset picture".</summary>
    public void Reload() => Rebuild();

    // ---------- layout ----------

    void Rebuild()
    {
        _rows.Children.Clear();
        _updaters.Clear();
        _status.Text = "";
        var q = _search.Text.Trim();
        IEnumerable<SettingDef> defs;
        if (q.Length == 0)
        {
            var cat = Catalog.Categories.First(c => c.Id == _category);
            _rows.Children.Add(new TextBlock { Text = cat.Heading, Style = Res<Style>("Lumina.Text.Page"), Margin = new Thickness(0, 0, 0, 22) });
            defs = Catalog.In(_category);
        }
        else
        {
            foreach (var r in _nav.Values) r.IsChecked = false;
            defs = Catalog.All.Where(d =>
                d.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (d.Help?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                d.Section.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            _rows.Children.Add(new TextBlock { Text = defs.Any() ? "Results" : "Nothing matches", Style = Res<Style>("Lumina.Text.Page"), Margin = new Thickness(0, 0, 0, 22) });
        }

        foreach (var section in defs.GroupBy(d => (d.Category, d.Section)))
        {
            var title = q.Length == 0 ? section.Key.Section : $"{Catalog.Categories.First(c => c.Id == section.Key.Category).Title}  ·  {section.Key.Section}";
            _rows.Children.Add(new TextBlock { Text = title, Style = Res<Style>("Lumina.Text.Caption"), Margin = new Thickness(6, 0, 0, 8) });
            var stack = new StackPanel();
            var first = true;
            foreach (var d in section)
            {
                if (!first) stack.Children.Add(new Border { Style = Res<Style>("Lumina.Hairline") });
                first = false;
                stack.Children.Add(Row(d));
            }
            _rows.Children.Add(new Border { Style = Res<Style>("Lumina.Group"), Margin = new Thickness(0, 0, 0, 28), Child = stack });
        }
    }

    UIElement Row(SettingDef d)
    {
        var multiline = d.Kind == Kind.Multiline;
        var grid = new Grid { Margin = new Thickness(20, 14, 20, 14) };
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        labels.Children.Add(new TextBlock { Text = d.Label, Style = Res<Style>("Lumina.Text.Label"), TextWrapping = TextWrapping.Wrap });
        if (d.Help is not null)
            labels.Children.Add(new TextBlock { Text = d.Help, Style = Res<Style>("Lumina.Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });

        var ctl = Control(d);
        if (multiline || d.Kind == Kind.Swatches)
        {
            grid.Children.Add(new StackPanel { Children = { labels, ctl } });
            ctl.SetValue(MarginProperty, new Thickness(0, 12, 0, 0));
            return grid;
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(labels);
        Grid.SetColumn((FrameworkElement)ctl, 1);
        ((FrameworkElement)ctl).VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(ctl);
        return grid;
    }

    // Lets the host move a visible control when it changes a setting itself (a preset filling the bands).
    readonly Dictionary<string, Action<string>> _updaters = new();

    public void Refresh(string key) { if (_updaters.TryGetValue(key, out var u)) u(AppSettings.Get(key)); }

    void Commit(SettingDef d, string value)
    {
        if (d.Kind != Kind.Text && d.Kind != Kind.Secret && d.Kind != Kind.Multiline && AppSettings.Get(d.Key) == AppSettings.Normalize(d, value)) return;   // nothing changed, nothing to announce
        try { AppSettings.Set(d.Key, value); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Say("Could not save: " + ex.Message); return; }
        Changed?.Invoke(d.Key);
    }

    // ---------- controls ----------

    UIElement Control(SettingDef d)
    {
        var value = AppSettings.Get(d.Key);
        switch (d.Kind)
        {
            case Kind.Toggle:
            {
                var t = new ToggleButton { Style = Res<Style>("Lumina.Toggle"), IsChecked = value == "true" };
                System.Windows.Automation.AutomationProperties.SetName(t, d.Label);
                t.Click += (_, _) => Commit(d, t.IsChecked == true ? "true" : "false");
                return t;
            }
            case Kind.Choice:
            {
                var cb = new System.Windows.Controls.ComboBox
                {
                    Width = 300, ItemsSource = d.Choices, DisplayMemberPath = "Label", SelectedValuePath = "Value", SelectedValue = value,
                };
                System.Windows.Automation.AutomationProperties.SetName(cb, d.Label);
                cb.SelectionChanged += (_, _) => { if (cb.SelectedValue is string s) Commit(d, s); };
                _updaters[d.Key] = v => cb.SelectedValue = v;
                return cb;
            }
            case Kind.Segmented:
            {
                var p = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var c in d.Choices!)
                {
                    var rb = new System.Windows.Controls.RadioButton { Content = c.Label, GroupName = d.Key, Style = Res<Style>("Lumina.Seg.Item"), IsChecked = c.Value == value };
                    rb.Click += (_, _) => Commit(d, c.Value);
                    p.Children.Add(rb);
                }
                return new Border { Style = Res<Style>("Lumina.Group"), CornerRadius = new CornerRadius(12), Padding = new Thickness(3), Child = p };
            }
            case Kind.Slider:
                return SliderRow(d);
            case Kind.Secret:
            {
                var pb = new Wpf.Ui.Controls.PasswordBox { Width = 300, RevealButtonEnabled = true, Password = Config.Raw(d.Key) };
                System.Windows.Automation.AutomationProperties.SetName(pb, d.Label);
                pb.LostFocus += (_, _) => CommitText(d, pb.Password, s => pb.Password = s);
                // WPF-UI's reveal button has no accessible name of its own.
                pb.Loaded += (_, _) => { if (pb.Template?.FindName("RevealButton", pb) is DependencyObject b) System.Windows.Automation.AutomationProperties.SetName(b, "Show or hide " + d.Label); };
                return pb;
            }
            case Kind.Text:
            case Kind.Multiline:
            case Kind.Color:
            {
                var multi = d.Kind == Kind.Multiline;
                var tb = new System.Windows.Controls.TextBox
                {
                    Style = Res<Style>("Lumina.Input"), Tag = d.Placeholder, Text = d.Kind == Kind.Color ? "#" + value : d.Kind == Kind.Multiline ? Config.Raw(d.Key) : value,
                };
                if (multi) { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; tb.MinHeight = 96; tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; tb.FontFamily = Res<FontFamily>("Lumina.Font.Mono"); }
                else tb.Width = d.Kind == Kind.Color ? 140 : 300;
                System.Windows.Automation.AutomationProperties.SetName(tb, d.Label);
                tb.LostFocus += (_, _) => CommitText(d, tb.Text, s => tb.Text = s);
                if (!multi) tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitText(d, tb.Text, s => tb.Text = s); };
                if (d.Kind != Kind.Color) return tb;
                var sw = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderBrush = Res<Brush>("Lumina.Brush.BorderInput"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
                void Paint() { try { sw.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(tb.Text.StartsWith('#') ? tb.Text : "#" + tb.Text)); } catch (FormatException) { } }
                tb.TextChanged += (_, _) => Paint();
                Paint();
                return new StackPanel { Orientation = Orientation.Horizontal, Children = { sw, tb } };
            }
            case Kind.Swatches:
                return AccentRow(value, hex => Commit(d, hex), d.Key);
            default: // Button
            {
                var b = new System.Windows.Controls.Button { Content = Verbs.GetValueOrDefault(d.Action!, "Run"), Style = Res<Style>("Lumina.Btn.Secondary") };
                System.Windows.Automation.AutomationProperties.SetName(b, d.Label);
                b.Click += (_, _) => ActionRequested?.Invoke(d.Action!);
                return b;
            }
        }
    }

    // Text fields validate on the way out: bad input snaps back to the stored value and says why.
    void CommitText(SettingDef d, string text, Action<string> reset)
    {
        text = text.Trim();
        if (d.Key == "subtitleLanguages" && text.Length > 0)
        {
            if (Config.NormalizeLanguages(text) is not { } langs) { reset(AppSettings.Get(d.Key)); Say("Languages must be codes like en, es or pt-br"); return; }
            text = langs;
        }
        if (d.Kind == Kind.Color && Normalize(d, text) is var n && n == d.Default && text.TrimStart('#').ToUpperInvariant() != d.Default) { reset("#" + AppSettings.Get(d.Key)); Say("Colours are six hex digits, like FFCC00"); return; }
        if (d.Kind == Kind.Color) text = text.TrimStart('#');
        if (text == (d.Kind == Kind.Multiline ? Config.Raw(d.Key) : AppSettings.Get(d.Key))) return;
        Commit(d, text);
    }

    static readonly Dictionary<string, string> Verbs = new()
    {
        ["clear-history"] = "Clear", ["show-shortcuts"] = "Show", ["open-folders"] = "Open", ["rescan"] = "Scan", ["reset-picture"] = "Reset", ["open-logs"] = "Open", ["edit-keys"] = "Customise", ["check-updates"] = "Check", ["show-about"] = "Show", ["show-logins"] = "Manage", ["unhide"] = "Show", ["show-profiles"] = "Manage", ["edit-controller"] = "Customise", ["set-pin"] = "Set or change", ["remove-pin"] = "Remove", ["lock-now"] = "Lock", ["assoc-add"] = "Add", ["assoc-default"] = "Open", ["assoc-remove"] = "Remove",
    };

    static string Normalize(SettingDef d, string v) => AppSettings.Normalize(d, v);

    UIElement SliderRow(SettingDef d)
    {
        var s = new Slider
        {
            Style = Res<Style>("Lumina.Slider"), Minimum = d.Min, Maximum = d.Max, SmallChange = d.Step, LargeChange = d.Step * 5,
            TickFrequency = d.Step, IsSnapToTickEnabled = true, Value = AppSettings.Num(d.Key), Width = 220,
        };
        System.Windows.Automation.AutomationProperties.SetName(s, d.Label);
        var read = new TextBlock { Style = Res<Style>("Lumina.Text.Label"), Width = 64, TextAlignment = TextAlignment.Right, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        void Show() => read.Text = Catalog.Num(s.Value) + d.Unit;
        Show();
        // The template's Fill border follows the thumb: width is the value's share of the track plus half a thumb.
        void Paint()
        {
            if (s.Template?.FindName("Fill", s) is not Border fill) return;
            var frac = (s.Value - s.Minimum) / (s.Maximum - s.Minimum);
            fill.Width = Math.Max(0, frac * (s.ActualWidth - 16) + 8);
        }
        s.Loaded += (_, _) => Paint();
        s.SizeChanged += (_, _) => Paint();
        // Dragging fires dozens of changes a second; write once the value has been still for a moment.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { timer.Stop(); Commit(d, Catalog.Num(s.Value)); };
        _updaters[d.Key] = v => { if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) s.Value = n; };
        s.ValueChanged += (_, _) => { Show(); Paint(); timer.Stop(); timer.Start(); };
        s.Unloaded += (_, _) => { if (timer.IsEnabled) { timer.Stop(); Commit(d, Catalog.Num(s.Value)); } };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { s, read } };
    }

    /// <summary>The nine accent swatches as a row of radio buttons: named, with a ring and a check mark on the chosen one. Used here and in the welcome.</summary>
    public static StackPanel AccentRow(string current, Action<string> picked, string group)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var hex in Catalog.AccentSwatches)
        {
            var rb = new System.Windows.Controls.RadioButton
            {
                GroupName = group, IsChecked = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 12, 0), MinWidth = 0, Width = 30, Height = 30, Padding = new Thickness(0), Template = Swatch(hex), ToolTip = Catalog.AccentName(hex),
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, "Accent " + Catalog.AccentName(hex));
            rb.Click += (_, _) => picked(hex);
            p.Children.Add(rb);
        }
        return p;
    }

    static ControlTemplate Swatch(string hex)
    {
        // the check mark sits on the swatch, so its colour is whichever of near-black and white reads better on that swatch
        var chosen = (Color)ColorConverter.ConvertFromString("#" + hex);
        var tick = Accent.Contrast(Colors.Black, chosen) >= Accent.Contrast(Colors.White, chosen) ? "#0C0D10" : "#FFFFFF";
        var xaml = $@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='RadioButton'>
  <Grid Width='30' Height='30'>
    <Ellipse x:Name='Ring' Stroke='Transparent' StrokeThickness='2' />
    <Ellipse Fill='#{hex}' Margin='5' />
    <Path x:Name='Tick' Data='M 9,15.5 L 13.2,19.5 L 21,10.5' Stroke='{tick}' StrokeThickness='2.4' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed' />
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='Ring' Property='Stroke' Value='#{hex}' /><Setter TargetName='Tick' Property='Visibility' Value='Visible' /></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
        return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}
