using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lumina.Metadata;

namespace Lumina;

/// <summary>
/// "Edit metadata": search TMDB by hand and choose which title a movie or show is. The answer is the chosen match,
/// or "none" (keep what the file name says and stop asking TMDB), or null when the window was closed.
/// </summary>
public partial class MetadataPicker : Window
{
    public sealed class Row : INotifyPropertyChanged
    {
        public Tmdb.Candidate Candidate { get; }
        public bool IsCurrent { get; }
        ImageSource? _poster;
        public Row(Tmdb.Candidate c, bool current, ImageSource? poster = null) { Candidate = c; IsCurrent = current; _poster = poster; }
        public string Title => Candidate.Meta.Title;
        public string Overview => Candidate.Meta.Overview ?? "";
        public string DateText => DateTime.TryParse(Candidate.Date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : (Candidate.Meta.Year?.ToString() ?? "");
        public string AutomationName => string.IsNullOrEmpty(DateText) ? Title : $"{Title}, {DateText}";
        public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
        public ImageSource? Poster { get => _poster; set { _poster = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Poster))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>What was decided. Meta is null for "none".</summary>
    public sealed record Choice(Lumina.Library.Meta? Meta);

    readonly string _kind;
    readonly long _currentId;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(380) };
    int _searchNo;
    Choice? _result;

    MetadataPicker(Window? owner, string fileName, string kind, string initialQuery, long currentId)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;
        _kind = kind; _currentId = currentId;
        FileText.Text = fileName;
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = SearchAsync(SearchBox.Text); };
        SearchBox.Text = initialQuery;
        Results.PreviewMouseLeftButtonUp += OnResultClick;
        Loaded += (_, _) => { SearchBox.Focus(); SearchBox.CaretIndex = SearchBox.Text.Length; _ = SearchAsync(SearchBox.Text); };
    }

    /// <summary>Opens the window. Returns null when it was closed without a choice.</summary>
    public static Choice? Show(Window? owner, string fileName, string kind, string initialQuery, long currentId)
    {
        var w = new MetadataPicker(owner, fileName, kind, initialQuery, currentId);
        w.ShowDialog();
        return w._result;
    }

    void OnKey(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
    void OnClose(object sender, RoutedEventArgs e) => Close();

    void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { _debounce.Stop(); _debounce.Start(); }

    void OnNone(object sender, RoutedEventArgs e) { _result = new Choice(null); Close(); }

    void OnResultClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d) return;
        while (d != null && d is not System.Windows.Controls.ListBoxItem) d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        if (d is System.Windows.Controls.ListBoxItem { DataContext: Row row }) { _result = new Choice(row.Candidate.Meta); Close(); }
    }

    async Task SearchAsync(string text)
    {
        var no = ++_searchNo;
        text = text.Trim();
        if (text.Length == 0) { Results.ItemsSource = null; StatusText.Text = "Type a title to search TMDB."; return; }
        StatusText.Text = "Searching";
        var (title, year) = Library.TitleQuery.Split(text);
        List<Tmdb.Candidate> found;
        try { found = await Tmdb.SearchMany(_kind, title); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (no == _searchNo) { Results.ItemsSource = null; StatusText.Text = "TMDB did not answer. Check your connection and key, then try again."; }
            return;
        }
        if (no != _searchNo) return;                       // a newer search is on its way
        // the year only decides the order: a title of that year first, then the others, so a wrong guess is easy to correct
        var rows = found.OrderByDescending(c => year != null && c.Meta.Year == year).Select(c => new Row(c, c.Meta.TmdbId == _currentId)).ToList();
        Results.ItemsSource = rows;
        StatusText.Text = rows.Count == 0 ? "No matches. Try fewer words, or the title as TMDB spells it." : "";
        foreach (var row in rows) _ = LoadPosterAsync(row, no);
    }

    async Task LoadPosterAsync(Row row, int no)
    {
        var file = await Tmdb.SmallPosterAsync(row.Candidate.Meta.PosterPath);
        if (file == null || no != _searchNo) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(file); bmp.DecodePixelWidth = 112; bmp.EndInit(); bmp.Freeze();
            row.Poster = bmp;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or InvalidOperationException) { }
    }
}
