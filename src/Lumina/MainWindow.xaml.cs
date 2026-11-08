using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Lumina.Library;
using Lumina.Metadata;
using Lumina.Player;
using Lumina.Settings;
using Lumina.Windows;
using Lumina.Subtitles;
using Lumina.Sync;
using Microsoft.Win32;

namespace Lumina;

/// <summary>One line on the Folders page.</summary>
/// <summary>One person on the details page: photo if there is one, otherwise initials.</summary>
public record CastCard(string Name, string Role, System.Windows.Media.ImageSource? Photo)
{
    public string Initials => Lumina.Library.DetailsText.Initials(Name);
    public System.Windows.Visibility InitialsShown => Photo == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string Label => Role.Length > 0 ? $"{Name}, {Role}" : Name;
}

public record PlaylistRow(long Id, string Name, string Info)
{
    // Button names must differ per row for screen readers.
    public string PlayName => "Play " + Name;
    public string RenameName => "Rename " + Name;
    public string SubsName => "Find subtitles for " + Name;
    public string DeleteName => "Delete " + Name;
    public override string ToString() => Name; // what a screen reader says for the row
}

public record FolderRow(string Path, string Info, bool Locked = false)
{
    public string LockText => Locked ? "Unlock" : "Lock";
    public string LockName => Locked ? "Unlock folder" : "Lock folder";
}

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    enum Page { Home, Library, Details, Folders, Playlists, Settings }

    Mpv? _mpv;
    readonly Fullscreen _fs;
    readonly Lumina.Windows.TaskbarReveal _taskbarReveal;
    readonly MouseForwarder _mouse; // kept alive for its event handlers and idle-cursor timer
    PlaybackSession? _session;

    List<LibraryEntry> _entries = new();
    LibraryEntry? _entry;
    string? _detailsKey;          // set while the details page is the screen playback returns to
    Page _page = Page.Home;
    Page _prevPage = Page.Home;   // where Back from details goes
    Page _pageBeforePlay = Page.Home;
    string _libMode = "movies";   // movies | shows | unwatched | search
    string _query = "";
    string _navKey = "home";
    bool _quietSearch;            // true while code (not the user) edits the search box
    bool _busy;
    // ---- taskbar progress and jump list ----

    readonly System.Windows.Threading.DispatcherTimer _taskbarTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    void StartTaskbar()
    {
        TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo();
        _taskbarTimer.Tick += (_, _) => UpdateTaskbar();
        _taskbarTimer.Start();
        RebuildJumpList();
    }

    static double D(string? s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    void UpdateTaskbar()
    {
        TaskbarState st;
        try
        {
            st = _mpv is { } m && AppSettings.Bool("general.taskbar")
                ? TaskbarState.From(true, D(m.Get("time-pos")), D(m.Get("duration")), m.Get("pause") == "yes", m.Get("paused-for-cache") == "yes")
                : TaskbarState.From(false, 0, 0, false, false);
            // Test seam: lets the end-to-end tour read what we decided, since the real taskbar button cannot be read back.
            if (_mpv is { } m2) m2.Set("user-data/lumina/taskbar", st.ToString());
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { st = TaskbarState.From(false, 0, 0, false, false); }

        var info = TaskbarItemInfo;
        if (info == null) return;
        info.ProgressState = st.Kind switch
        {
            ProgressKind.Normal => System.Windows.Shell.TaskbarItemProgressState.Normal,
            ProgressKind.Paused => System.Windows.Shell.TaskbarItemProgressState.Paused,
            ProgressKind.Indeterminate => System.Windows.Shell.TaskbarItemProgressState.Indeterminate,
            _ => System.Windows.Shell.TaskbarItemProgressState.None,
        };
        info.ProgressValue = st.Value;
    }

    void RememberRecent(string path)
    {
        if (!AppSettings.Bool("general.jumpList")) return;
        try
        {
            var list = RecentFiles.Add(RecentFiles.Deserialize(Db.KvGet("recent_files")), path);
            Db.KvSet("recent_files", RecentFiles.Serialize(list));
            if (!path.Contains("://")) System.Windows.Shell.JumpList.AddToRecentCategory(path);
        }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException) { Lumina.Diagnostics.Log.Error("Could not remember a recent file", e); }
    }

    /// <summary>
    /// The taskbar jump list shows Windows' own "Recent" category for Lumina. Windows keeps the list; Lumina only reports each file it plays.
    /// (A custom category rebuilt on every play crashed inside the runtime's COM marshalling after a few plays, so none is built.)
    /// </summary>
    void RebuildJumpList()
    {
        try
        {
            var jl = System.Windows.Shell.JumpList.GetJumpList(Application.Current);
            if (jl == null) { jl = new System.Windows.Shell.JumpList(); System.Windows.Shell.JumpList.SetJumpList(Application.Current, jl); }
            jl.ShowRecentCategory = AppSettings.Bool("general.jumpList"); jl.ShowFrequentCategory = false;
            jl.Apply();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
        { Lumina.Diagnostics.Log.Error("Could not update the jump list", e); }
    }

    // ================= sidebar =================

    const double SidebarFull = 248, SidebarCompact = 76;

    void ApplyCompactSidebar(bool compact, bool animate = false)
    {
        SidebarMode.SetIsCompact(Sidebar, compact);
        var width = compact ? SidebarCompact : SidebarFull;
        SidebarCol.Width = new GridLength(width);
        SidebarGrid.Margin = compact ? new Thickness(10, 12, 10, 14) : new Thickness(14, 12, 14, 14);
        Wordmark.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchField.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchCompact.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Status.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TraktText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        TraktIcon.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        // The collapse button sits beside the dot when wide; below it when there is no room beside it.
        Brand.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        CompactToggle.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        Grid.SetRow(CompactToggle, compact ? 1 : 0);
        CompactToggle.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        BrandRow.Margin = compact ? new Thickness(0, 6, 0, 18) : new Thickness(12, 6, 0, 18);
        CompactGlyph.Symbol = compact ? Wpf.Ui.Controls.SymbolRegular.PanelLeftExpand20 : Wpf.Ui.Controls.SymbolRegular.PanelLeftContract20;
        var label = compact ? "Expand sidebar" : "Collapse sidebar";
        CompactToggle.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(CompactToggle, label);
        // Icons alone need names on hover.
        foreach (var r in new[] { NavHome, NavMovies, NavShows, NavUnwatched, NavPlaylists, NavFolders, NavSettings }) r.ToolTip = compact ? r.Content : null;
        TraktBtn.ToolTip = compact ? TraktText.Text : null;
    }

    void OnCompactToggle(object sender, RoutedEventArgs e)
    {
        var on = !SidebarMode.GetIsCompact(Sidebar);
        AppSettings.Set("interface.compactSidebar", on ? "true" : "false");
        ApplyCompactSidebar(on);
    }

    void OnSearchCompact(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("interface.compactSidebar", "false");
        ApplyCompactSidebar(false);
        SearchBox.Focus();                       // the search field needs the room, so open the sidebar and type
    }

    // ================= right-click menus =================

    /// <summary>The library item (entry or episode) a click or key landed on, or null.</summary>
    static object? ItemAt(DependencyObject? d)
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: LibraryEntry or MediaItem } fe && fe is ListBoxItem or ContentPresenter or Border)
                return fe.DataContext;
        return null;
    }

    void OnContextRequest(object sender, MouseButtonEventArgs e)
    {
        if (Stage.Visibility == Visibility.Visible || e.OriginalSource is not DependencyObject src || ItemAt(src) is not { } item) return;
        ShowItemMenu(item, (FrameworkElement)src);
        e.Handled = true;
    }

    void OnContextKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;      // F10 arrives as a "system" key
        var menuKey = key == Key.Apps || (key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift);
        if (!menuKey || Stage.Visibility == Visibility.Visible || Keyboard.FocusedElement is not DependencyObject f || ItemAt(f) is not { } item) return;
        ShowItemMenu(item, (FrameworkElement)f);
        e.Handled = true;
    }

    void ShowItemMenu(object item, FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        MenuItem Add(string header, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            System.Windows.Automation.AutomationProperties.SetName(mi, header);
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
            return mi;
        }
        switch (item)
        {
            case LibraryEntry en:
                Add(en.PlayLabel, () => Play(en.Next.Path, en.Next));
                Add(en.AllWatched ? "Mark as unwatched" : "Mark as watched", () => SetWatched(en.Items, !en.AllWatched));
                menu.Items.Add(new Separator());
                Add(en.IsShow ? "Find subtitles for all episodes..." : "Find subtitles...", () => _ = FindSubtitlesFor(en.Items, en.Title), enabled: !string.IsNullOrEmpty(Config.OpenSubtitlesKey));
                Add("Refresh info", () => RematchEntry(en, askForTitle: false), enabled: !string.IsNullOrEmpty(Config.TmdbKey));
                Add("Edit metadata...", () => EditMetadata(en), enabled: !string.IsNullOrEmpty(Config.TmdbKey));
                // a wrongly filed title: a show's files become films one by one; a film's whole folder becomes a show
                Add(en.IsShow ? "This is a movie, not a show" : "This is a TV show, not a movie", () => _ = SetKind(
                    en.IsShow ? en.Items.Select(i => i.Path) : en.Items.Select(i => Path.GetDirectoryName(i.Path) ?? i.Path).Distinct(StringComparer.OrdinalIgnoreCase),
                    en.IsShow ? "movie" : "show"));
                menu.Items.Add(new Separator());
                Add("Show in Explorer", () => Reveal(en.Next.Path), Explorer.CanReveal(en.Next.Path));
                Add("Hide from library...", () => HideItems(en.Items, en.Title));
                break;
            case MediaItem ep:
                Add(ep.PlayText, () => Play(ep.Path, ep));
                Add(ep.Watched ? "Mark as unwatched" : "Mark as watched", () => SetWatched(new[] { ep }, !ep.Watched));
                Add("Find subtitles", () => _ = FindSubtitlesFor(new[] { ep }, ep.EpisodeTitle), enabled: !string.IsNullOrEmpty(Config.OpenSubtitlesKey));
                if (ep.Season is { } s && ep.Kind == "show")
                {
                    var season = Db.List().Where(i => i.Kind == "show" && i.TmdbId == ep.TmdbId && i.Title == ep.Title && i.Season == s).ToList();
                    Add(season.All(i => i.Watched) ? "Mark season as unwatched" : "Mark season as watched", () => SetWatched(season, !season.All(i => i.Watched)));
                    Add("This file is a movie, not an episode", () => _ = SetKind(new[] { ep.Path }, "movie"));
                }
                menu.Items.Add(new Separator());
                Add("Show in Explorer", () => Reveal(ep.Path), Explorer.CanReveal(ep.Path));
                Add("Hide from library...", () => HideItems(new[] { ep }, ep.EpisodeTitle));
                break;
        }
        menu.IsOpen = true;
    }

    void Reveal(string path)
    {
        if (!Explorer.CanReveal(path)) { Status.Text = "That file is not there right now. Is its drive connected?"; return; }
        Explorer.Reveal(path);
    }

    void SetWatched(IEnumerable<MediaItem> items, bool watched)
    {
        var list = items.ToList();
        Db.MarkWatched(list.Select(i => i.Id), watched);
        var rows = watched ? null : Db.LocalWatchRows().Where(r => list.Any(i => i.Id == r.Id)).ToList();
        Refresh();
        if (rows is { Count: > 0 } && Trakt.IsConnected)
            _ = Task.Run(() => Trakt.RemoveHistoryAsync(rows)).ContinueWith(t => { if (t.Result.Length > 0) Dispatcher.Invoke(() => Status.Text = t.Result); }, TaskScheduler.Default);
    }

    /// <summary>Right-click, "This is a TV show / a movie": remembered for those files or folders and applied by a rescan.</summary>
    async Task SetKind(IEnumerable<string> paths, string kind)
    {
        try
        {
            Db.SetKind(paths.ToList(), kind);
            Status.Text = kind == "show" ? "Filing these as TV episodes." : "Filing this as a movie.";
            await Rescan();
        }
        catch (Microsoft.Data.Sqlite.SqliteException e)
        {
            Lumina.Diagnostics.Log.Error("Set kind failed", e);
            Status.Text = "Could not change that: " + e.Message;
        }
    }

    /// <summary>Right-click, Edit metadata: search TMDB by hand and choose which title this movie or show is.</summary>
    async void EditMetadata(LibraryEntry en)
    {
        try
        {
            var first = en.Items[0];
            var shown = en.IsShow ? en.Title : Path.GetFileName(first.Path);
            var query = first.FileYear is { } fy ? $"{first.FileTitle} {fy}" : first.FileTitle;
            var choice = MetadataPicker.Show(this, shown, en.IsShow ? "show" : "movie", query, en.TmdbId);
            if (choice == null) return;
            Db.AssignMatch(en.Items.Select(i => i.Id), choice.Meta);
            Refresh();
            Status.Text = choice.Meta == null ? "Using the file's own information." : "Getting details for " + choice.Meta.Title;
            await Rescan();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Windows.Markup.XamlParseException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            Lumina.Diagnostics.Log.Error("Edit metadata failed", e);
            Status.Text = "Could not open Edit metadata: " + e.Message;
        }
    }

    async void RematchEntry(LibraryEntry en, bool askForTitle)
    {
        string? qt = null; int? qy = null;
        if (askForTitle)
        {
            var text = ConfirmDialog.Prompt(this, "Fix match", "Type the title as TMDB knows it, with the year if you know it, for example Dune 2021.", "Search", "Title and year", en.Year is { } y ? $"{en.Title} {y}" : en.Title);
            if (text == null) return;
            (qt, qy) = TitleQuery.Split(text);
        }
        foreach (var g in en.Items.GroupBy(i => (i.Kind, i.FileTitle, i.FileYear))) Db.Rematch(g.Key.Kind, g.Key.FileTitle, g.Key.FileYear, qt, qy);
        Refresh();
        await Rescan();
    }

    void HideItems(IEnumerable<MediaItem> items, string name)
    {
        var list = items.ToList();
        var body = $"{name} disappears from Lumina. The file{(list.Count == 1 ? " is" : "s are")} not deleted or moved. You can bring {(list.Count == 1 ? "it" : "them")} back in Settings, Library.";
        if (!ConfirmDialog.Ask(this, "Hide from library?", body, "Hide")) return;
        Db.Hide(list.Select(i => i.Path));
        Refresh();
        if (_page == Page.Details) { _detailsKey = null; ShowPage(_prevPage); }
    }

    LaunchArgs? _launch;   // options from the command line, used by the next video that starts

    /// <summary>A launch (this process, or another one forwarding to us) asks to open something.</summary>
    public void OpenFromLaunch(LaunchArgs a)
    {
        if (a.Problems is { Count: > 0 }) Status.Text = a.Problems[0];
        BringToFront();
        if (!a.HasMedia) return;
        _launch = a;
        OpenPaths(a.Paths);
    }

    void BringToFront()
    {
        if (!IsVisible) Show();                         // hidden in the tray
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;                                 // the usual way to win foreground rights from a background launch
        Topmost = AppSettings.Bool("interface.keepOnTop");
        Focus();
    }

    FrameworkElement[] AllPages => new FrameworkElement[] { HomePage, LibraryPage, DetailsPage, FoldersPage, PlaylistsPage, SettingsPage };
    readonly System.Collections.ObjectModel.ObservableCollection<PlaylistRow> _playlistRows = new();
    readonly PlaybackQueue _queue = new();
    string _playingPath = "";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyKindVisibility(); StartTaskbar();
            if (AppSettings.Bool("general.startWithWindows")) { try { StartWithWindows.Set(true); } catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Lumina.Diagnostics.Log.Error("Could not refresh the start-with-Windows entry", e); } }
            if (App.Launch.Background && !App.Launch.HasMedia)       // signed in with the start-with-Windows entry: stay out of the way
            {
                if (AppSettings.Bool("general.tray")) { EnsureTray(); Hide(); }
                else WindowState = WindowState.Minimized;
            }
        };
        Loaded += (_, _) => MaybeCheckOnStart();
        if (AppSettings.Bool("general.tour") && !App.Launch.HasMedia)
            Loaded += (_, _) => Dispatcher.BeginInvoke(() => Onboarding.Show(this, PickAndAddFolder), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ApplyCompactSidebar(AppSettings.Bool("interface.compactSidebar"));
        Topmost = AppSettings.Bool("interface.keepOnTop");
        RestoreWindow();
        _fs = new Fullscreen(this, Bar);
        _taskbarReveal = new Lumina.Windows.TaskbarReveal(this);
        SmoothScroll.Attach(this);
        // A maximized window with its own title bar hides an auto-hiding taskbar; let hovering the screen edge bring it up anyway.
        void UpdateReveal() { if (WindowState == WindowState.Maximized && !_fs.On) _taskbarReveal.Start(); else if (!_fs.On) _taskbarReveal.Stop(); }
        StateChanged += (_, _) => UpdateReveal();
        Loaded += (_, _) => UpdateReveal();
        SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(TouchHook);
        Stage.IsVisibleChanged += (_, _) => { UpdateTitleRow(); Lumina.Windows.PlaybackPriority.Set(Stage.IsVisible); ApplyTranslucency(); };
        Loaded += (_, _) => ApplyTranslucency();
        Bar.IsVisibleChanged += (_, _) => UpdateTitleRow();
        _mouse = new MouseForwarder(Stage, () => _mpv, () => _fs.On);
        Db.Init();
        // Test seam: lets the end-to-end tour start with a library folder without driving the folder picker.
        if (Environment.GetEnvironmentVariable("LUMINA_SEED_ROOT") is { Length: > 0 } seed) Db.AddRoot(seed);
        Closing += OnWindowClosing;
        Application.Current.SessionEnding += (_, _) => _quitting = true;       // Windows is signing out or shutting down: never hold that up
        Closed += (_, _) => { LeaveMiniplayer(); RemoveTray(); SaveWindow(); _session?.Stop(); SaveVolume(); _mpv?.Dispose(); };
        // mpv's window lives on its own thread and never gets keyboard focus from us, so forward keys to it.
        PreviewKeyDown += OnPlayerKey;
        PreviewMouseRightButtonUp += OnContextRequest;
        PreviewKeyDown += OnContextKey;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.Control && Stage.Visibility != Visibility.Visible) { OpenStream(); e.Handled = true; } };
        PreviewTextInput += OnPlayerText;

        var poster = (DataTemplate)FindResource("Lumina.PosterCard");
        ShelfContinue.CardTemplate = (DataTemplate)FindResource("Lumina.HeroCard");
        foreach (var s in new[] { ShelfFavourites, ShelfRecent, ShelfUnwatched, ShelfTopRated, ShelfMovies, ShelfShows }) s.CardTemplate = poster;
        foreach (var s in new[] { ShelfContinue, ShelfFavourites, ShelfRecent, ShelfUnwatched, ShelfTopRated, ShelfMovies, ShelfShows }) s.ItemInvoked += OpenEntry;
        ShelfFavourites.SeeAllClicked += () => Navigate("favourites");
        ShelfTopRated.SeeAllClicked += () => Navigate("toprated");
        ShelfFilmGenres.ItemInvoked += t => { if (t is GenreTile g) OpenGenre(g); };
        ShelfShowGenres.ItemInvoked += t => { if (t is GenreTile g) OpenGenre(g); };
        ShelfRecent.SeeAllClicked += () => Navigate("movies");
        ShelfUnwatched.SeeAllClicked += () => Navigate("unwatched");
        ShelfMovies.SeeAllClicked += () => Navigate("movies");
        ShelfShows.SeeAllClicked += () => Navigate("shows");

        // Focus rings are for keyboard users: switch them on for navigation keys, off again on any click.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Up or Key.Down or Key.Enter or Key.Space) FocusMode.SetKeyboard(this, true);
        };
        PreviewMouseDown += (_, _) => FocusMode.SetKeyboard(this, false);
        // Start with focus on the page itself so no button shows a keyboard ring until the user presses Tab.
        Loaded += (_, _) => Pages.Focus();
        Loaded += (_, _) => Dispatcher.BeginInvoke(NameTitleBarButtons, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Refresh();
        UpdateTraktButton();
        if (App.Launch.HasMedia || App.Launch.Problems is { Count: > 0 }) Loaded += (_, _) => OpenFromLaunch(App.Launch);
        else if (AppSettings.Bool("general.scanOnStart")) Loaded += async (_, _) => await Rescan();
        // Lets screenshots and UI tests open a page without clicking, e.g. LUMINA_PAGE=settings.
        if (Environment.GetEnvironmentVariable("LUMINA_PAGE") is { Length: > 0 } startPage) Loaded += (_, _) => Navigate(startPage);
        DetailsPage.SizeChanged += (_, _) => { if (_page == Page.Details) FitHero(); };
        // LUMINA_EDIT=<title> opens that title's Edit metadata window (screenshots and tests)
        if (Environment.GetEnvironmentVariable("LUMINA_EDIT") is { Length: > 0 } editTitle)
            Loaded += async (_, _) => { await Task.Delay(1800); if (_entries.FirstOrDefault(x => x.Title.Contains(editTitle, StringComparison.OrdinalIgnoreCase)) is { } hit) EditMetadata(hit); };
        // and LUMINA_DETAILS=<title> opens that title's page after the library has loaded
        if (Environment.GetEnvironmentVariable("LUMINA_DETAILS") is { Length: > 0 } startTitle)
            Loaded += async (_, _) => { await Task.Delay(1500); if (_entries.FirstOrDefault(x => x.Title.Contains(startTitle, StringComparison.OrdinalIgnoreCase)) is { } hit) ShowDetails(hit.Key); };
    }

    // ================= navigation =================

    void ShowPage(Page p, bool animate = true)
    {
        _page = p;
        var target = p switch
        {
            Page.Home => (FrameworkElement)HomePage,
            Page.Library => LibraryPage,
            Page.Details => DetailsPage,
            Page.Folders => FoldersPage,
            Page.Playlists => PlaylistsPage,
            _ => SettingsPage,
        };
        foreach (var el in AllPages)
            el.Visibility = el == target ? Visibility.Visible : Visibility.Collapsed;
        if (animate) Fade(target);
    }

    // Short fade and rise so a page change reads as a change. Skipped when Windows animations are off.
    static void Fade(FrameworkElement e)
    {
        if (!SystemParameters.ClientAreaAnimation) { e.Opacity = 1; return; }
        var t = new TranslateTransform(0, 10);
        e.RenderTransform = t;
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        t.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void SetNav(string key)
    {
        _navKey = key;
        var radio = key switch
        {
            "home" => NavHome, "movies" => NavMovies, "shows" => NavShows,
            "unwatched" => NavUnwatched, "playlists" => NavPlaylists, "folders" => NavFolders, "settings" => NavSettings, _ => null,
        };
        if (radio != null) radio.IsChecked = true;
        else foreach (var r in new[] { NavHome, NavMovies, NavShows, NavUnwatched, NavPlaylists, NavFolders, NavSettings }) r.IsChecked = false;
    }

    void ClearSearchQuietly()
    {
        if (SearchBox.Text.Length == 0) return;
        _quietSearch = true;
        SearchBox.Clear();
        _quietSearch = false;
        _query = "";
    }

    void Navigate(string key)
    {
        _detailsKey = null;
        ClearSearchQuietly();
        SetNav(key);
        switch (key)
        {
            case "home": ShowPage(Page.Home); HomePage.ScrollToTop(); break;
            case "movies": case "shows": case "unwatched": case "favourites": case "toprated":
                _libMode = key;
                ResetLibraryFilters();
                RenderLibrary();
                ShowPage(Page.Library);
                break;
            case "folders": RenderFolders(); ShowPage(Page.Folders); break;
            case "playlists": ShowPage(Page.Playlists); RenderPlaylists(); break;
            case "settings": LoadSettings(); ShowPage(Page.Settings); break;
        }
    }

    void OnNav(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { CommandParameter: string key }) Navigate(key);
    }

    GenreTile? _genre;

    /// <summary>The Library page showing one genre of films or shows.</summary>
    void OpenGenre(GenreTile g)
    {
        _detailsKey = null;
        ClearSearchQuietly();
        SetNav(g.Kind == "show" ? "shows" : "movies");
        _genre = g;
        _libMode = "genre";
        ResetLibraryFilters();
        RenderLibrary();
        ShowPage(Page.Library);
    }

    void OnTile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string key }) Navigate(key);
    }

    void OnBannerSettings(object sender, RoutedEventArgs e) => Navigate("settings");

    void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_quietSearch) return;
        _query = SearchBox.Text.Trim();
        if (_query.Length == 0)
        {
            if (_libMode == "search" || _page == Page.Library && _navKey == "") Navigate("home");
            return;
        }
        _detailsKey = null;
        _libMode = "search";
        SetNav("");
        ResetLibraryFilters();
        RenderLibrary();
        ShowPage(Page.Library, animate: _page != Page.Library);
    }

    // ================= data =================

    List<LibraryEntry> _allEntries = new();     // everything scanned; _entries is what the user chose to show

    void Refresh()
    {
        var items = VisibleItems();
        _allEntries = LibraryEntry.Build(items);
        var movies = AppSettings.Bool("library.showMovies"); var shows = AppSettings.Bool("library.showShows");
        _entries = _allEntries.Where(e => e.IsShow ? shows : movies).ToList();
        RefreshHome();
        if (_page == Page.Library) RenderLibrary();
        if (_page == Page.Folders) RenderFolders();
        if (_page == Page.Details && _detailsKey != null) ShowDetails(_detailsKey, inPlace: true);   // episode pictures, cast and scores appear as they are fetched
    }

    void RefreshHome()
    {
        var empty = _entries.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        HomeRows.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        KeyBanner.Visibility = !empty && string.IsNullOrEmpty(Config.TmdbKey) ? Visibility.Visible : Visibility.Collapsed;

        var movies = _entries.Where(e => !e.IsShow).ToList();
        var shows = _entries.Where(e => e.IsShow).ToList();
        var unwatched = LibraryEntry.Unwatched(_entries);
        TileMoviesCount.Text = Count(movies.Count, "title");
        TileShowsCount.Text = Count(shows.Count, "show");
        TileUnwatchedCount.Text = Count(unwatched.Count, "title");
        TileFoldersCount.Text = Count(Db.Roots().Count, "folder");

        ShelfFavourites.SetItems(HomeShelves.Favourites(_entries, Db.Favourites()).Take(20));
        ShelfFilmGenres.SetItems(HomeShelves.Genres(_entries, "movie"));
        ShelfShowGenres.SetItems(HomeShelves.Genres(_entries, "show"));
        ShelfTopRated.SetItems(HomeShelves.TopRated(_entries, "movie"));
        ShelfContinue.SetItems(LibraryEntry.ContinueWatching(_entries));
        ShelfRecent.SetItems(LibraryEntry.RecentlyAdded(_entries));
        // Shelves that would only repeat the one above are left out; the tiles still lead to the full lists.
        ShelfUnwatched.SetItems(unwatched.Count < _entries.Count ? unwatched.Take(20) : Array.Empty<LibraryEntry>());
        var big = _entries.Count > 8;
        ShelfMovies.SetItems(big ? movies.Take(20) : Array.Empty<LibraryEntry>());
        ShelfShows.SetItems(big ? shows.Take(20) : Array.Empty<LibraryEntry>());
    }

    static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    void OpenEntry(object item)
    {
        if (item is LibraryEntry e) ShowDetails(e.Key);
    }

    // ================= library page =================

    void ResetLibraryFilters() => FilterAll.IsChecked = true;   // sort and view are the user's standing choices, so they are not reset

    bool _syncingView;

    /// <summary>Make the header controls and the list show what the settings say.</summary>
    void ApplyLibraryView()
    {
        _syncingView = true;
        SortBox.ItemsSource ??= LibrarySort.Choices;
        SortBox.SelectedValue = LibrarySort.Name(LibrarySort.Parse(AppSettings.Get("interface.librarySort")));
        var list = AppSettings.Get("interface.libraryView") == "list";
        ViewGrid.IsChecked = !list; ViewList.IsChecked = list;
        var size = AppSettings.Get("interface.gridSize");
        SizeS.IsChecked = size == "small"; SizeM.IsChecked = size == "medium"; SizeL.IsChecked = size == "large";
        foreach (var r in new[] { SizeS, SizeM, SizeL }) r.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        LibGrid.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        LibList.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        var scale = size switch { "small" => 0.82, "large" => 1.2, _ => 1.0 };
        LibGrid.LayoutTransform = scale == 1.0 ? System.Windows.Media.Transform.Identity : new System.Windows.Media.ScaleTransform(scale, scale);
        _syncingView = false;
    }

    void OnSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingView || SortBox.SelectedValue is not string v) return;
        AppSettings.Set("interface.librarySort", v);
        RenderLibrary();
    }

    void OnViewChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("interface.libraryView", ViewList.IsChecked == true ? "list" : "grid");
        ApplyLibraryView();
    }

    void OnSizeChanged(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("interface.gridSize", SizeS.IsChecked == true ? "small" : SizeL.IsChecked == true ? "large" : "medium");
        ApplyLibraryView();
    }

    void OnListClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(LibList, d) is ListBoxItem { DataContext: { } item })
            OpenEntry(item);
    }

    void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && LibList.SelectedItem is { } item) { OpenEntry(item); e.Handled = true; }
    }

    void OnLibraryFilter(object sender, RoutedEventArgs e) => RenderLibrary();

    void RenderLibrary()
    {
        IEnumerable<LibraryEntry> src = _libMode switch
        {
            "movies" => _entries.Where(x => !x.IsShow),
            "shows" => _entries.Where(x => x.IsShow),
            "unwatched" => LibraryEntry.Unwatched(_entries),
            "favourites" => HomeShelves.Favourites(_entries, Db.Favourites()),
            "toprated" => HomeShelves.TopRated(_entries, "movie", int.MaxValue),
            "genre" when _genre != null => HomeShelves.InGenre(_entries, _genre.Kind, _genre.Name),
            "search" => _entries.Where(x => Matches(x, _query)),
            _ => _entries,
        };
        if (FilterUnwatched.IsChecked == true) src = src.Where(x => x.AnyUnwatched);
        else if (FilterProgress.IsChecked == true) src = src.Where(x => x.InProgress);

        var list = LibrarySort.Apply(src, LibrarySort.Parse(AppSettings.Get("interface.librarySort")));
        ApplyLibraryView();
        LibGrid.ItemsSource = list;
        LibList.ItemsSource = list;
        LibTitle.Text = _libMode switch
        {
            "movies" => "Movies", "shows" => "TV shows", "unwatched" => "Unwatched",
            "favourites" => "Favourites", "toprated" => "Top rated films",
            "genre" when _genre != null => _genre.Name,
            _ => $"Results for “{_query}”",
        };
        LibCount.Text = Count(list.Count, "title");
        LibEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LibEmptyText.Text = _libMode == "search" ? "Nothing matches that search."
            : FilterAll.IsChecked == true ? "No titles here yet." : "Nothing matches this filter.";
    }

    static bool Matches(LibraryEntry e, string q) =>
        e.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || e.Items.Any(i => i.EpisodeName?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true);

    void OnGridClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(LibGrid, d) is ListBoxItem { DataContext: { } item })
            OpenEntry(item);
    }

    void OnGridKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && LibGrid.SelectedItem is { } item) { OpenEntry(item); e.Handled = true; }
    }

    // ================= details page =================

    MediaItem? _selEp;            // the episode the details page is showing (a movie's only file otherwise)
    bool _filling;

    /// <summary>The picture at the top takes what is left after the season tabs and the episode row (or the cast row), so the row is on screen when the page opens.</summary>
    void FitHero()
    {
        var room = DetailsPage.ActualHeight;
        if (room <= 0) return;
        var below = _entry?.IsShow == true ? 300 : 150;
        DHero.Height = Math.Clamp(room - below, 400, 580);
    }

    void ShowDetails(string key, bool inPlace = false)
    {
        var en = _entries.FirstOrDefault(x => x.Key == key);
        if (en == null) { _detailsKey = null; ShowPage(_page == Page.Details ? _prevPage : _page); return; } // removed or rescanned away
        if (_page != Page.Details) _prevPage = _page;
        var sameTitle = _detailsKey == key;
        _detailsKey = key;
        _entry = en;

        DBackdrop.Source = en.HeroImage is { } img ? LoadImage(img) : null;
        var detail = en.TmdbId > 0 ? Db.GetDetail(en.Kind, en.TmdbId) : null;

        // Title: the logo when TMDB has one and it is downloaded, otherwise plain text.
        var logo = detail?.Logo is { } lp && File.Exists(Db.ArtPath(lp)) ? Db.ArtPath(lp) : null;
        DLogo.Source = logo != null ? LoadImage(logo) : null;
        DLogo.Visibility = logo != null ? Visibility.Visible : Visibility.Collapsed;
        DTitle.Text = en.Title;
        DTitle.Visibility = logo != null ? Visibility.Collapsed : Visibility.Visible;

        DCast.ItemsSource = detail?.Cast.Select(c => new CastCard(c.Person, c.Role, c.Photo is { } cp && File.Exists(Db.ArtPath(cp)) ? LoadImage(Db.ArtPath(cp)) : null)).ToList();
        DCastBlock.Visibility = detail is { Cast.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        DShowBlock.Visibility = en.IsShow ? Visibility.Visible : Visibility.Collapsed;
        DEpisodeTitle.Visibility = en.IsShow ? Visibility.Visible : Visibility.Collapsed;
        if (en.IsShow)
        {
            // keep the episode the user picked when the page refreshes (marking watched rebuilds it), otherwise start at what is next
            var keep = sameTitle && _selEp != null ? en.Items.FirstOrDefault(i => i.Id == _selEp.Id) : null;
            var start = keep ?? en.Next;
            BuildSeasons(en, start.Season, start);
        }
        else Select(en, en.Items[0]);

        ShowPage(Page.Details, animate: _page != Page.Details);
        FitHero();
        if (!inPlace) DetailsPage.ScrollToTop();       // a refresh while pictures arrive must not move what the user is reading
    }

    /// <summary>Everything on the page that depends on which file is chosen: its heading, facts, summary, scores, Play, progress, actions and footer.</summary>
    void Select(LibraryEntry en, MediaItem sel)
    {
        _selEp = sel;
        var detail = en.TmdbId > 0 ? Db.GetDetail(en.Kind, en.TmdbId) : null;
        var ratings = en.TmdbId > 0 ? Db.GetRatings(en.Kind, en.TmdbId) : new Ratings(null, null);
        var multiSeason = en.Seasons > 1;

        DEpisodeTitle.Text = en.IsShow ? DetailsText.EpisodeHeading(sel.Season, sel.Episode, sel.EpisodeName, multiSeason) : "";
        var overview = en.IsShow ? sel.EpisodeOverview ?? en.Overview : en.Overview;
        DOverview.Text = overview ?? "";
        DOverview.Visibility = string.IsNullOrWhiteSpace(overview) ? Visibility.Collapsed : Visibility.Visible;

        DPlayText.Text = sel.PlayText;
        System.Windows.Automation.AutomationProperties.SetName(DPlay, sel.PlayText);
        DProgressBlock.Visibility = sel.CanResume ? Visibility.Visible : Visibility.Collapsed;
        if (sel.CanResume)
        {
            DProgress.Value = sel.Percent;
            DProgressText.Text = sel.Duration is > 0 ? $"{MediaItem.Clock(sel.Position)} of {MediaItem.Clock(sel.Duration.Value)}" : $"Stopped at {MediaItem.Clock(sel.Position)}";
        }

        FillMeta(en, detail, sel, null);
        DScores.Children.Clear();
        foreach (var (source, value) in DetailsText.Scores(en.IsShow ? sel.EpRating ?? en.Rating : en.Rating, ratings.Imdb, ratings.RottenTomatoes))
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 4) };
            sp.Children.Add(ScoreLogo(source, value));
            sp.Children.Add(new TextBlock { Text = value, Style = (Style)FindResource("Lumina.Text.Label"), VerticalAlignment = VerticalAlignment.Center });
            System.Windows.Automation.AutomationProperties.SetName(sp, $"{source} {value}");
            DScores.Children.Add(sp);
        }

        UpdateDetailActions(en, sel);

        DFile.Text = Path.GetFileName(sel.Path);
        DSpecs.Text = FileProbe.Cached(sel.Path) is { } known ? string.Join("   ", DetailsText.SpecParts(known)) : "";
        if (DSpecs.Text.Length == 0) _ = ProbeForDetails(sel, en, detail);
    }

    /// <summary>
    /// The source's own mark, left of its score. Rotten Tomatoes' rules: the Fresh tomato only with 60% or more, the Rotten splat only with 59% or less.
    /// </summary>
    UIElement ScoreLogo(string source, string value)
    {
        System.Windows.Media.ImageSource? img = source switch
        {
            "IMDb" => (System.Windows.Media.ImageSource)FindResource("Lumina.Logo.Imdb"),
            "TMDB" => (System.Windows.Media.ImageSource)FindResource("Lumina.Logo.Tmdb"),
            _ => RottenLogo(value),
        };
        var image = new Image { Source = img, Height = 20, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(image, System.Windows.Media.BitmapScalingMode.HighQuality);
        return image;
    }

    static System.Windows.Media.ImageSource RottenLogo(string value)
    {
        bool fresh = DetailsText.IsFresh(value);
        return new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/Assets/Ratings/rt-{(fresh ? "fresh" : "rotten")}.png"));
    }

    /// <summary>The line above the synopsis: runtime, year, quality and genres, with the certificate as a small badge.</summary>
    void FillMeta(LibraryEntry en, Detail? detail, MediaItem sel, FileSpec? spec)
    {
        DMeta.Children.Clear();
        var when = en.IsShow ? DetailsText.AirDate(sel.AirDate) is { Length: > 0 } ad ? ad : en.Year?.ToString() : en.Year?.ToString();
        var parts = DetailsText.MetaParts(en.IsShow ? sel.EpRuntime ?? detail?.Runtime : detail?.Runtime, when, spec != null ? DetailsText.Quality(spec.Width, spec.Height) : null, detail?.Genres ?? Array.Empty<string>());
        if (parts.Count == 0 && detail?.Certification == null) parts.Add(en.IsShow ? "TV show" : "Movie");
        TextBlock T(string text) => new() { Text = text, Style = (Style)FindResource("Lumina.Text.Muted"), Foreground = (System.Windows.Media.Brush)FindResource("Lumina.Brush.Text"), Opacity = 0.82, FontSize = 14, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };   // over artwork: 4.5:1 at worst in the muted grey, comfortably more in this
        for (int i = 0; i < parts.Count; i++)
        {
            DMeta.Children.Add(T(parts[i]));
            if (i == 1 && detail?.Certification is { } cert) DMeta.Children.Add(Badge(cert));
        }
        if (parts.Count < 2 && detail?.Certification is { } c2) DMeta.Children.Add(Badge(c2));
    }

    Border Badge(string text)
    {
        var b = new Border
        {
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, Style = (Style)FindResource("Lumina.Text.Muted"), FontSize = 12, FontWeight = FontWeights.SemiBold },
        };
        b.SetResourceReference(Border.BorderBrushProperty, "Lumina.Brush.TextMuted");
        System.Windows.Automation.AutomationProperties.SetName(b, "Rated " + text);
        return b;
    }

    CancellationTokenSource? _probeCts;

    /// <summary>Reads the file's codec and size in the background and fills the footer, unless the user has moved on.</summary>
    async Task ProbeForDetails(MediaItem sel, LibraryEntry en, Detail? detail)
    {
        _probeCts?.Cancel();
        var cts = _probeCts = new CancellationTokenSource();
        var spec = await FileProbe.GetAsync(sel.Path, cts.Token);
        if (spec == null || cts.IsCancellationRequested || _selEp?.Id != sel.Id || _entry != en) return;
        DSpecs.Text = string.Join("   ", DetailsText.SpecParts(spec));
        FillMeta(en, detail, sel, spec);
    }

    void UpdateDetailActions(LibraryEntry en, MediaItem sel)
    {
        var all = sel.Watched;
        DWatchedIcon.Symbol = all ? Wpf.Ui.Controls.SymbolRegular.EyeOff24 : Wpf.Ui.Controls.SymbolRegular.Eye24;
        var wName = all ? "Mark as unwatched" : "Mark as watched";
        DWatched.ToolTip = wName; System.Windows.Automation.AutomationProperties.SetName(DWatched, wName);

        var canFav = en.TmdbId > 0;
        var fav = canFav && Db.IsFavourite(en.Kind, en.TmdbId);
        DFavouriteIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Star24;
        DFavouriteIcon.Filled = fav;
        DFavourite.IsEnabled = canFav;
        var fName = !canFav ? "Add to favourites (needs a TMDB match)" : fav ? "Remove from favourites" : "Add to favourites";
        DFavourite.ToolTip = fName; System.Windows.Automation.AutomationProperties.SetName(DFavourite, fName);
    }

    void OnDetailsWatched(object sender, RoutedEventArgs e)
    {
        if (_entry is not { } en || _selEp is not { } sel) return;
        SetWatched(new[] { sel }, !sel.Watched);
    }

    void OnDetailsFavourite(object sender, RoutedEventArgs e)
    {
        if (_entry is not { TmdbId: > 0 } en) return;
        Db.SetFavourite(en.Kind, en.TmdbId, !Db.IsFavourite(en.Kind, en.TmdbId));
        RefreshHome();
        if (_selEp is { } cur) UpdateDetailActions(en, cur);
    }

    void OnDetailsMore(object sender, RoutedEventArgs e)
    {
        if (_entry is { } en) ShowItemMenu(en.IsShow && _selEp != null ? _selEp : en, DMore);
    }

    void OnDetailsPlaylist(object sender, RoutedEventArgs e)
    {
        if (_entry is not { } en) return;
        var menu = new ContextMenu { PlacementTarget = DPlaylist, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(string header, Action act)
        {
            var mi = new MenuItem { Header = header };
            System.Windows.Automation.AutomationProperties.SetName(mi, header);
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        var items = (en.IsShow ? en.Items : en.Items.Take(1)).Select(i => new QueueItem(i.Path, en.IsShow ? $"{en.Title} {i.Sub}" : en.Title, i.Id)).ToList();
        foreach (var p in Db.Playlists())
        {
            var id = p.Id; var name = p.Name;
            Add("Add to " + name, () => { var n = Db.AppendToPlaylist(id, items); Status.Text = $"{Count(n, "item")} added to {name}"; });
        }
        Add("New playlist...", () =>
        {
            var name = ConfirmDialog.Prompt(this, "New playlist", en.IsShow ? $"All {Count(items.Count, "episode")} of {en.Title} will be added." : $"{en.Title} will be added.", "Create", "Playlist name");
            if (name == null) return;
            Db.SavePlaylist(name, items);
            Status.Text = $"Created {name.Trim()}";
        });
        menu.IsOpen = true;
    }

    void BuildSeasons(LibraryEntry en, int? selected, MediaItem? start)
    {
        DSeasons.Children.Clear();
        var seasons = en.Items.Select(i => i.Season).Distinct().OrderBy(s => s ?? 0).ToList();
        var chosen = seasons.Contains(selected) ? selected : seasons.FirstOrDefault();
        foreach (var s in seasons)
        {
            var label = en.Items.First(i => i.Season == s).SeasonLabel;
            var rb = new RadioButton
            {
                Content = label, GroupName = "seasons", Tag = s, Margin = new Thickness(0, 0, 6, 6),
                Style = (Style)FindResource("Lumina.Seg.Item"), IsChecked = s == chosen,
            };
            // picking another season shows its first episode, the way you would start it
            rb.Checked += (_, _) => FillEpisodes(en, (int?)rb.Tag, null);
            DSeasons.Children.Add(rb);
        }
        // A lone season needs no selector: a plain heading stands in for it.
        DSeasons.Visibility = seasons.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        DEpisodesHeading.Visibility = seasons.Count > 1 ? Visibility.Collapsed : Visibility.Visible;
        FillEpisodes(en, chosen, start);
    }

    void FillEpisodes(LibraryEntry en, int? season, MediaItem? start)
    {
        var list = en.Items.Where(i => i.Season == season).ToList();
        _filling = true;
        DEpisodes.ItemsSource = list;
        var pick = start != null && list.Any(i => i.Id == start.Id) ? list.First(i => i.Id == start.Id) : list.FirstOrDefault();
        DEpisodes.SelectedItem = pick;
        _filling = false;
        if (pick != null)
        {
            Select(en, pick);
            DEpisodes.ScrollIntoView(pick);
        }
    }

    // Load fully into memory so the cached file is not locked while shown.
    static BitmapImage LoadImage(string path)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.UriSource = new Uri(path);
        img.EndInit();
        return img;
    }

    void OnDetailsBack(object sender, RoutedEventArgs e)
    {
        _detailsKey = null;
        ShowPage(_prevPage);
    }

    void OnDetailsPlay(object sender, RoutedEventArgs e)
    {
        if (_selEp is { } sel) Play(sel.Path, sel);
        else if (_entry != null) Play(_entry.Next.Path, _entry.Next);
    }

    void OnEpisodeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || _entry is not { } en || DEpisodes.SelectedItem is not MediaItem ep) return;
        Select(en, ep);
    }

    void OnEpisodeDouble(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource as DependencyObject) is MediaItem ep) Play(ep.Path, ep);
    }

    void OnEpisodeKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DEpisodes.SelectedItem is MediaItem ep) { Play(ep.Path, ep); e.Handled = true; }
    }

    /// <summary>Over a sideways row (episodes, cast) the wheel moves the row; at either end, or when everything fits, it goes on to scroll the page.</summary>
    void OnRowWheel(object sender, MouseWheelEventArgs e)
    {
        var row = sender as ScrollViewer ?? (sender as DependencyObject is { } d ? ScrollForward.FindScroller(d) : null);
        bool more = row is { ScrollableWidth: > 0 } && (e.Delta < 0 ? row.HorizontalOffset < row.ScrollableWidth - 1 : row.HorizontalOffset > 1);
        if (more) SmoothScroll.By(row!, -e.Delta, true); else SmoothScroll.By(DetailsPage, -e.Delta);
        e.Handled = true;
    }

    // ================= scanning and Trakt =================

    void SetBusy(bool on)
    {
        _busy = on;
        ScanBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task Rescan()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            Status.Text = "Scanning folders";
            var n = await Task.Run(Scanner.ScanAll);
            try { Db.KvSet("last_scan", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch (Microsoft.Data.Sqlite.SqliteException) { }
            Refresh();
            // Resumes on the UI thread, so the callback can touch controls directly.
            int steps = 0;
            var err = !AppSettings.Bool("privacy.fetchArtwork") ? "" : await Tmdb.FillAsync(msg => { Status.Text = msg; if (++steps % 10 == 0) Refresh(); });
            Refresh();
            Status.Text = err != "" ? err : n > 0 ? $"{n} new or changed" : "";
            if (Trakt.IsConnected) { Status.Text = "Syncing Trakt"; Status.Text = await Trakt.SyncAsync(); Refresh(); }
        }
        finally { SetBusy(false); }
    }

    void UpdateTraktButton()
    {
        var on = Trakt.IsConnected;
        TraktText.Text = on ? "Trakt connected" : "Connect Trakt";
        TraktIcon.Symbol = on ? Wpf.Ui.Controls.SymbolRegular.PlugConnected24 : Wpf.Ui.Controls.SymbolRegular.PlugDisconnected24;
        TraktIcon.Foreground = on ? (Brush)FindResource("Lumina.Brush.Accent") : (Brush)FindResource("Lumina.Brush.TextMuted");
    }

    async void OnTrakt(object sender, RoutedEventArgs e)
    {
        if (Trakt.IsConnected) { TraktBtn.ContextMenu!.PlacementTarget = TraktBtn; TraktBtn.ContextMenu.IsOpen = true; return; }
        if (!Trakt.HasCredentials)
        {
            Status.Text = "Add your Trakt client id and secret in Settings first.";
            Navigate("settings");
            return;
        }
        if (new TraktConnectWindow { Owner = this }.ShowDialog() == true)
        {
            UpdateTraktButton();
            await SyncTrakt();
        }
    }

    void OnTraktSync(object sender, RoutedEventArgs e) => _ = SyncTrakt();

    void OnTraktDisconnect(object sender, RoutedEventArgs e)
    {
        Trakt.Disconnect();
        UpdateTraktButton();
        Status.Text = "Trakt disconnected";
    }

    async Task SyncTrakt()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            Status.Text = "Syncing Trakt";
            Status.Text = await Trakt.SyncAsync();
            UpdateTraktButton(); // a failed refresh disconnects
            Refresh();
        }
        finally { SetBusy(false); }
    }

    // ================= settings =================

    SettingsView? _settings;

    void LoadSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsView();
            _settings.Changed += OnSettingChanged;
            _settings.ActionRequested += OnSettingAction;
            SettingsPage.Child = _settings;
        }
        _settings.Reload();
    }

    void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "general.accent":
                Accent.Apply(AppSettings.Get(key));
                try { _mpv?.Command("script-message", "lumina-accent", AppSettings.Get(key)); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
                break;
            case "general.theme":
                Lumina.Themes.ThemeManager.Apply();
                break;
            case "interface.keepOnTop": Topmost = AppSettings.Bool(key); break;
            case "interface.translucency": Lumina.Themes.ThemeManager.Apply(); ApplyTranslucency(); break;
            case "interface.controls": PushControls(); break;
            case "general.tray": if (!AppSettings.Bool(key)) RemoveTray(); break;
            case "general.startWithWindows":
                try { StartWithWindows.Set(AppSettings.Bool(key)); }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                { Lumina.Diagnostics.Log.Error("Could not change the start-with-Windows entry", e); _settings?.Say("Windows did not allow that: " + e.Message); }
                break;
            case "interface.seekThumbs": if (!AppSettings.Bool(key)) StopThumbnails(); break;
            case "interface.controllerStyle":
                try { _mpv?.Command("script-message", "lumina-do", "style", AppSettings.Get(key)); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
                break;
            case "library.showMovies": case "library.showShows": ApplyKindVisibility(); Refresh(); break;
            case "shortcuts": ApplyKeysLive(new Dictionary<string, string>(), KeyOverrides()); break;
            case "audio.eqPreset": ApplyEqPreset(); ApplyAudioFx(); break;
            case var k when System.Text.RegularExpressions.Regex.IsMatch(k, @"^audio\.eq\d+$"):
                if (AppSettings.Get("audio.eqPreset") != "custom") { AppSettings.Set("audio.eqPreset", "custom"); _settings?.Refresh("audio.eqPreset"); }
                ApplyAudioFx();
                break;
            case "audio.compressor": case "audio.spatial": case "audio.stereoWidth": case "audio.normalize": case "audio.night": ApplyAudioFx(); break;
            case "interface.compactSidebar": ApplyCompactSidebar(AppSettings.Bool(key)); break;
            case "interface.librarySort": case "interface.libraryView": case "interface.gridSize": ApplyLibraryView(); if (_page == Page.Library) RenderLibrary(); break;
            case "general.jumpList": if (!AppSettings.Bool(key)) Db.KvDelete("recent_files"); RebuildJumpList(); break;
            case "general.taskbar": UpdateTaskbar(); break;
            case "playback.shuffle": case "playback.queueRepeat": ApplyQueueSettings(); if (_mpv is { } pm) PushQueue(pm); break;
            case "tmdbKey": case "openSubtitlesKey": Refresh(); break; // the missing-key banner may need to go
            case "traktClientId": case "traktClientSecret":
                // Tokens belong to the Trakt app that issued them, so a different app means signing in again.
                if (Trakt.IsConnected) { Trakt.Disconnect(); _settings?.Say("Trakt credentials changed. Connect again from the sidebar."); }
                UpdateTraktButton();
                break;
        }
    }

    async void OnSettingAction(string action)
    {
        switch (action)
        {
            case "rescan": await Rescan(); _settings?.Say("Scan finished"); break;
            case "open-folders": Navigate("folders"); break;
            case "clear-history":
                if (!ConfirmDialog.Ask(this, "Clear watch history?", "Every title goes back to unwatched and resume positions are forgotten. Your files stay.", "Clear")) break;
                Db.ClearWatch();
                Refresh();
                _settings?.Say("Watch history cleared");
                break;
            case "show-shortcuts": ShowShortcuts(); break;
            case "edit-keys": EditKeys(); break;
            case "show-about": ShowAbout(); break;
            case "check-updates": await CheckForUpdatesAsync(userAsked: true); break;
            case "show-logins": ShowLogins(); break;
            case "show-profiles": ShowProfiles(); break;
            case "set-pin": if (SetNewPin()) { _settings?.Say("PIN saved. Lock folders from the Folders page."); RenderFolders(); } break;
            case "remove-pin":
                if (!LockStore.HasPin) { _settings?.Say("There is no PIN."); break; }
                if (!AskForPin("Enter your PIN to remove it. Every folder will be unlocked.")) break;
                LockStore.ClearPin(); _unlocked = false; Refresh(); RenderFolders();
                _settings?.Say("PIN removed. No folder is locked.");
                break;
            case "lock-now":
                if (!LockStore.HasPin || LockStore.Folders.Count == 0) { _settings?.Say("No folder is locked yet. Lock one from the Folders page."); break; }
                LockNow(); _settings?.Say("Locked.");
                break;
            case "edit-controller": EditController(); break;
            case "unhide":
            {
                var n = Db.HiddenCount();
                Db.UnhideAll();
                if (n > 0) await Rescan();
                _settings?.Say(n == 0 ? "Nothing was hidden." : $"{Count(n, "file")} brought back.");
                break;
            }
            case "assoc-add":
                try { FileAssociations.ForThisApp().Register(notifyShell: true); _settings?.Say("Lumina now appears in Open with for video files. Use the next row to make it the default."); }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Lumina.Diagnostics.Log.Error("Could not register file types", e); _settings?.Say("Windows did not allow that: " + e.Message); }
                break;
            case "assoc-remove":
                try { FileAssociations.ForThisApp().Unregister(notifyShell: true); _settings?.Say("Lumina was removed from Open with."); }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Lumina.Diagnostics.Log.Error("Could not unregister file types", e); _settings?.Say("Windows did not allow that: " + e.Message); }
                break;
            case "assoc-default":
                if (!FileAssociations.ForThisApp().IsRegistered()) { FileAssociations.ForThisApp().Register(notifyShell: true); }
                FileAssociations.OpenDefaultAppsSettings();
                break;
            case "open-logs":
                System.IO.Directory.CreateDirectory(Lumina.Diagnostics.Log.Dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Lumina.Diagnostics.Log.Dir + "\"") { UseShellExecute = true });
                break;
            case "reset-picture":
                AppSettings.Set(new Dictionary<string, string> { ["video.brightness"] = "0", ["video.contrast"] = "0", ["video.saturation"] = "0", ["video.gamma"] = "0", ["video.hue"] = "0" });
                _settings?.Reload();
                break;
        }
    }

    // ================= folders =================

    /// <summary>Sidebar entries and Home tiles for a kind the user turned off go away with it.</summary>
    void ApplyKindVisibility()
    {
        var movies = AppSettings.Bool("library.showMovies"); var shows = AppSettings.Bool("library.showShows");
        NavMovies.Visibility = movies ? Visibility.Visible : Visibility.Collapsed;
        NavShows.Visibility = shows ? Visibility.Visible : Visibility.Collapsed;
        TileMovies.Visibility = movies ? Visibility.Visible : Visibility.Collapsed;
        TileShows.Visibility = shows ? Visibility.Visible : Visibility.Collapsed;
        Tiles.Columns = 2 + (movies ? 1 : 0) + (shows ? 1 : 0);
        FolderShowMovies.IsChecked = movies; FolderShowShows.IsChecked = shows;
        if ((_libMode == "movies" && !movies || _libMode == "shows" && !shows) && _page == Page.Library) Navigate("home");
    }

    void OnKindToggle(object sender, RoutedEventArgs e)
    {
        AppSettings.Set(new Dictionary<string, string>
        {
            ["library.showMovies"] = FolderShowMovies.IsChecked == true ? "true" : "false",
            ["library.showShows"] = FolderShowShows.IsChecked == true ? "true" : "false",
        });
        _settings?.Refresh("library.showMovies"); _settings?.Refresh("library.showShows");
        ApplyKindVisibility();
        Refresh();
    }

    async void OnRefreshMetadata(object sender, RoutedEventArgs e)
    {
        if (_busy) { FolderStatus.Text = "A scan is running, try again in a moment."; return; }
        if (string.IsNullOrEmpty(Config.TmdbKey)) { FolderStatus.Text = "Add a TMDB key in Settings first."; return; }
        Db.RequeueMetadata();
        FolderStatus.Text = "Fetching details, scores and pictures again";
        await Rescan();
        FolderStatus.Text = "Metadata refreshed.";
    }

    void RenderFolders()
    {
        var stats = LibraryStats.From(VisibleItems());
        StatFilms.Text = stats.Films.ToString(); StatEpisodes.Text = stats.Episodes.ToString(); StatSize.Text = stats.SizeText;
        FolderUpdated.Text = "Last update: " + LibraryStats.LastUpdate(long.TryParse(Db.KvGet("last_scan"), out var ls) ? ls : null, DateTime.Now);
        FolderShowMovies.IsChecked = AppSettings.Bool("library.showMovies"); FolderShowShows.IsChecked = AppSettings.Bool("library.showShows");
        var roots = Db.Roots();
        var files = _entries.SelectMany(e => e.Items).Select(i => i.Path).ToList();
        var rows = roots.Select(r =>
        {
            var prefix = Db.Prefix(r);
            var n = files.Count(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var info = Directory.Exists(r) ? Count(n, "item") : $"Offline. {Count(n, "item")} kept in your library.";
            var lockedHere = LockStore.HasPin && LockStore.IsFolderLocked(r);
            if (lockedHere && !_unlocked) info = "Locked. Unlock to see its videos.";
            else if (lockedHere) info += ". Locked.";
            return new FolderRow(r, info, lockedHere);
        }).ToList();
        FolderUnlockBtn.Visibility = LockStore.HasPin && LockStore.Folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FolderUnlockBtn.Content = _unlocked ? "Lock now" : "Unlock";
        System.Windows.Automation.AutomationProperties.SetName(FolderUnlockBtn, _unlocked ? "Lock the locked folders now" : "Unlock the locked folders");
        FolderList.ItemsSource = rows;
        FolderEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TileFoldersCount.Text = Count(rows.Count, "folder");
    }

    void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderRow row) return;
        if (_busy) { FolderStatus.Text = "A scan is running, try again in a moment."; return; }
        var nl = Environment.NewLine;
        var ok = ConfirmDialog.Ask(this, "Remove this folder?",
            $"{row.Path}{nl}{nl}Your files are not touched. Watch progress for items found only in this folder is cleared here. Trakt keeps its own copy.");
        if (!ok) return;
        var n = Db.RemoveRoot(row.Path);
        Refresh();
        RenderFolders();
        FolderStatus.Text = $"Removed {row.Path} ({Count(n, "item")}).";
    }

    void OnAddFolder(object sender, RoutedEventArgs e) => PickAndAddFolder();

    /// <summary>Asks for a folder, adds it to the library and starts reading it. Returns the folder, or null if the dialog was cancelled.</summary>
    string? PickAndAddFolder()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true) return null;
        Db.AddRoot(dlg.FolderName);
        if (_page == Page.Folders) RenderFolders();
        _ = Rescan();
        return dlg.FolderName;
    }

    void OnRescan(object sender, RoutedEventArgs e) => _ = Rescan();

    void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Video|*.mkv;*.mp4;*.m4v;*.mov;*.avi;*.ts;*.m2ts;*.mts;*.webm;*.wmv;*.iso;*.flv|All files|*.*" };
        if (dlg.ShowDialog() == true) OpenPaths(dlg.FileNames);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } f) OpenPaths(f);
    }

    // ================= playback =================

    void Play(string path, MediaItem? item, bool keepQueue = false)
    {
        if (IsLockedPath(path))
        {
            if (!AskForPin("This video is in a folder you locked.")) return;
            _unlocked = true;
            Refresh();
        }
        if (!keepQueue) _queue.Clear();
        _playingPath = path;
        _playingItem = item;
        RememberRecent(path);
        var opts = _launch; _launch = null;
        var streamAuth = _streamAuth; _streamAuth = null;
        var loadUrl = _streamLoadUrl; _streamLoadUrl = null;
        if (Stage.Visibility != Visibility.Visible) _pageBeforePlay = _page;
        StopPlayback();
        var host = new MpvHost();
        Stage.Child = host;
        foreach (var el in AllPages) el.Visibility = Visibility.Collapsed;
        Stage.Visibility = Visibility.Visible;
        Bar.Title = AppSettings.Bool("interface.showFileName") ? Path.GetFileName(path) : "";
        // Hwnd exists only once the host is in the visual tree.
        host.Loaded += (_, _) =>
        {
            try
            {
                var m = _mpv = new Mpv(host.Hwnd, AppContext.BaseDirectory, AppSettings.MpvExtra());
                host.PassInputThrough();
                // mpv quit (q / window gone) -> back to where we were. Ignore a stale instance's late shutdown.
                m.Ended += why => { if (why == "shutdown") Dispatcher.BeginInvoke(() => { if (_mpv == m) Stop(); }); };
                // B key (input.conf) -> script-message; file-loaded -> attach a cached subtitle or hint.
                m.ClientMessage += a => Dispatcher.BeginInvoke(() =>
                {
                    if (_mpv != m || a.Length == 0) return;
                    switch (a[0])
                    {
                        case "lumina-subs-pick": _ = PickSubs(m, path, item); break;
                        case "lumina-substyle": if (a.Length > 3) SaveSubStyle(path, a[1], a[2], a[3]); break;
                        case "lumina-miniplayer": ToggleMiniplayer(); break;
                        case "lumina-record": ToggleRecording(m, path, item); break;
                        case "lumina-sleep": SleepCommand(m, a.Length > 1 ? a[1] : "cycle"); break;
                        case "lumina-bookmark": ToggleBookmark(m, path); break;
                        case "lumina-bookmark-clear": Db.ClearBookmarks(path); PushBookmarks(m, path); m.ShowText("Bookmarks removed", 1800); break;
                        case "lumina-thumb": if (a.Length > 3) ShowSeekPreview(a[1], a[2], a[3]); break;
                        case "lumina-thumb-hide": _seekPreview?.HidePreview(); break;
                        case "lumina-thumbs-toggle":
                        {
                            var on = !AppSettings.Bool("interface.seekThumbs");
                            AppSettings.Set("interface.seekThumbs", on ? "true" : "false");
                            if (on) StartThumbnails(m, path); else { StopThumbnails(); _seekPreview?.HidePreview(); }
                            m.Command("script-message", "lumina-thumbs-state", on ? "yes" : "no");
                            m.ShowText(on ? "Seek-bar preview on" : "Seek-bar preview off", 1500);
                            break;
                        }
                        case "lumina-subs": _ = PickSubs(m, path, item); break;   // always a list to choose from: the best guess is not always the right release
                        case "lumina-next": GoNeighbour(+1, item); break;
                        case "lumina-prev": GoNeighbour(-1, item); break;
                        case "lumina-eof": AfterEnd(item); break;
                        case "lumina-jump": JumpToTime(m); break;
                        case "lumina-info": ShowMediaInfo(m); break;
                        case "lumina-fx": if (a.Length > 1) FxCommand(a[1], a.Length > 2 ? a[2] : null); break;
                        case "lumina-audio-device": if (a.Length > 1) AppSettings.Set("audio.device", a[1]); break;
                        case "lumina-queue-jump": if (a.Length > 1 && int.TryParse(a[1], out var qi)) { if (_queue.JumpTo(qi) is { } qj) PlayQueueItem(qj); } break;
                        case "lumina-queue-remove": if (a.Length > 1 && int.TryParse(a[1], out var rm)) QueueRemove(rm); break;
                        case "lumina-queue-move": if (a.Length > 2 && int.TryParse(a[1], out var mf) && int.TryParse(a[2], out var md)) { _queue.Move(mf, mf + md); PushQueue(m); } break;
                        case "lumina-queue-shuffle": AppSettings.Set("playback.shuffle", AppSettings.Bool("playback.shuffle") ? "false" : "true"); ApplyQueueSettings(); PushQueue(m); break;
                        case "lumina-queue-repeat": AppSettings.Set("playback.queueRepeat", AppSettings.Get("playback.queueRepeat") switch { "off" => "all", "all" => "one", _ => "off" }); ApplyQueueSettings(); PushQueue(m); break;
                        case "lumina-queue-clear": _queue.Clear(); PushQueue(m); SendNeighbors(m, item); break;
                        case "lumina-queue-save": SaveQueueAsPlaylist(); break;
                        case "lumina-fullscreen": _fs.Toggle(); SendFullscreenState(); break;
                        case "lumina-exit-fullscreen": if (_fs.On) { _fs.Exit(); SendFullscreenState(); } else Stop(); break;   // Esc: leave fullscreen first, then go back to the page
                    }
                });
                m.FileLoaded += () => Dispatcher.BeginInvoke(() => { SendTitle(m, path, item); _ = OnFileLoaded(m, path); if (_mpv == m) { StartThumbnails(m, path); PushBookmarks(m, path); PushSleepState(m); StartMirror(); _recordingPath = null; PushMiniState(m); ApplySubStyle(m, path); } });
                m.EndFile += (reason, error) => { if (reason == 4) Dispatcher.BeginInvoke(() => PlaybackFailed(m, path, error)); };
                // Resume unless it was watched through or barely started.
                var start = opts?.StartSeconds ?? (item is { Watched: false, Position: > 5 } && AppSettings.Get("playback.resume") == "always" ? item.Position : 0);
                if (opts?.Paused == true) m.Set("pause", "yes");
                if (opts?.SubFiles is { Count: > 0 } subs) m.FileLoaded += () => { foreach (var f in subs) { try { m.SubAdd(f); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { } } };
                if (streamAuth != null) m.Set("http-header-fields", "Authorization: " + streamAuth);
                _mpv.Load(loadUrl ?? path, start);
                if (m.BadOptions.Count > 0) m.ShowText("Ignored bad mpv option: " + string.Join(", ", m.BadOptions), 5000);
                if (item != null)
                {
                    _session = new PlaybackSession(_mpv, item);
                }
                if ((opts?.Fullscreen == true || AppSettings.Bool("video.fullscreenStart")) && !_fs.On) _fs.Toggle();
                SendFullscreenState();
                SendNeighbors(m, item);
                PushQueue(m);
                PushFx(m);
            }
            catch (Exception ex)
            {
                Stop();
                ConfirmDialog.Tell(this, "Could not start playback", ex.Message);
            }
        };
    }

    // The on-screen controller shows a heading and a line under it; tell it what we know beyond the file name.
    void SendTitle(Mpv m, string path, MediaItem? item)
    {
        var title = item?.Title ?? Path.GetFileNameWithoutExtension(path);
        var sub = item == null ? "" : item.Kind == "show" ? item.Sub : item.Year?.ToString() ?? "";
        // The Windows Now playing overlay and media keys read the title from mpv.
        try { m.Set("force-media-title", sub.Length > 0 ? $"{title}  {sub}" : title); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
        try { m.Command("script-message", "lumina-title", title, sub); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    // ---- subtitles for many videos, a chosen match, and each video's own look ----

    bool _bulkSubsBusy;

    static BulkItem ToBulk(MediaItem i) => new(i.Path, i.Kind == "show" ? i.Title : i.Title, i.Kind == "movie" ? i.Year ?? i.MetaYear : null, i.Season, i.Episode);

    static BulkItem ToBulk(string path)
    {
        var p = NameParser.Parse(Path.GetFileNameWithoutExtension(path));
        return new BulkItem(path, p.Title, p.Year, p.Season, p.Episode);
    }

    /// <summary>Finds subtitles for every video in the list, one after another in the background. Videos that already have some are left alone.</summary>
    async Task FindSubtitlesFor(IEnumerable<MediaItem> items, string label) =>
        await FindSubtitlesFor(items.Where(i => File.Exists(i.Path)).Select(ToBulk).ToList(), label);

    async Task FindSubtitlesFor(IReadOnlyList<BulkItem> list, string label)
    {
        if (_bulkSubsBusy) { Status.Text = "Already searching for subtitles."; return; }
        if (list.Count == 0) { Status.Text = "No videos to find subtitles for."; return; }
        if (string.IsNullOrEmpty(Config.OpenSubtitlesKey)) { ConfirmDialog.Tell(this, "No OpenSubtitles key", "Add a free key in Settings, Accounts, to search for subtitles."); return; }
        if (list.Count > 3 && !ConfirmDialog.Ask(this, $"Find subtitles for {list.Count} videos?", $"{label}. Lumina asks OpenSubtitles one video at a time, skips any that already have subtitles, and stops if the daily download limit is reached.", "Search"))
            return;
        _bulkSubsBusy = true;
        try
        {
            var result = await Task.Run(() => BulkSubtitles.RunAsync(list,
                i => OpenSubtitles.FetchAsync(i.Path, i.Title, i.Year, i.Season, i.Episode),
                p => OpenSubtitles.HasSidecar(p) || OpenSubtitles.Cached(p, OpenSubtitles.Hash(p)) != null,
                (n, total, title) => Dispatcher.Invoke(() => Status.Text = $"Subtitles {n}/{total}: {title}"),   // Invoke, not BeginInvoke: a late progress line must not overwrite the summary
                pause: TimeSpan.FromMilliseconds(300)));
            Status.Text = "Subtitles: " + result.Summary;
            if (result.Stopped != null || list.Count == 1) ConfirmDialog.Tell(this, "Subtitles", result.Summary);
        }
        finally { _bulkSubsBusy = false; }
    }

    void OnPlaylistSubs(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlaylistRow row) return;
        var paths = Db.PlaylistItems(row.Id).Select(i => i.Path).Where(p => !p.Contains("://") && File.Exists(p)).Distinct().ToList();
        _ = FindSubtitlesFor(paths.Select(ToBulk).ToList(), $"The playlist {row.Name}");
    }

    void OnFolderSubs(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderRow row) return;
        if (LockStore.HasPin && !_unlocked && LockStore.IsFolderLocked(row.Path)) { Status.Text = "Unlock this folder first."; return; }
        var prefix = Db.Prefix(row.Path);
        var items = Db.List().Where(i => i.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        _ = FindSubtitlesFor(items, $"Everything in {row.Path}");
    }

    /// <summary>Lists every match for the playing video and uses the one the user picks. It is remembered for this video.</summary>
    async Task PickSubs(Mpv m, string path, MediaItem? item)
    {
        if (_subsBusy) return;
        _subsBusy = true;
        try
        {
            m.ShowText("Searching OpenSubtitles", 15000);
            var p = item != null ? new Parsed(item.Kind, item.Title, item.Year, item.Season, item.Episode) : NameParser.Parse(Path.GetFileNameWithoutExtension(path));
            var hits = await OpenSubtitles.SearchAsync(path, p.Title, p.Year, p.Season, p.Episode);
            if (_mpv != m) return;
            m.ShowText("", 1);
            if (hits.Count == 0) { m.ShowText("No subtitles found on OpenSubtitles", 3500); return; }
            var labels = hits.Take(40).Select(OpenSubtitles.Describe).ToList();
            var pick = ConfirmDialog.Choose(this, "Choose subtitles", "Best matches first. If one is out of sync or for another release, search again and pick a different one. Your pick is remembered for this video.", labels, "Use these");
            if (pick < 0 || _mpv != m) return;
            var file = await OpenSubtitles.ChooseAsync(path, hits[pick]);
            if (_mpv != m) return;
            m.SubAdd(file);
            m.ShowText("Subtitles: " + labels[pick], 4500);
        }
        catch (OpenSubtitles.SubsException e) { if (_mpv == m) m.ShowText(e.Message, 4500); }
        catch (System.Net.Http.HttpRequestException) { if (_mpv == m) m.ShowText("OpenSubtitles unreachable", 4000); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or TaskCanceledException) { }
        finally { _subsBusy = false; }
    }

    void SaveSubStyle(string path, string delay, string scale, string pos)
    {
        if (SubStyle.Parse(delay, scale, pos) is not { } style) return;
        try { SubStyleStore.Save(path, style, AppSettings.Num("subtitles.position")); }
        catch (Exception e) when (e is Microsoft.Data.Sqlite.SqliteException or IOException) { Lumina.Diagnostics.Log.Error("Could not remember the subtitle look", e); }
    }

    void ApplySubStyle(Mpv m, string path)
    {
        try
        {
            if (SubStyleStore.Load(path) is not { } s) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            m.Command("script-message", "lumina-substyle-apply", s.Delay.ToString("0.###", inv), s.Scale.ToString("0.###", inv), s.Position.ToString("0", inv));
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { }
    }

    // ---- miniplayer ----

    (Rect Bounds, WindowState State, bool Topmost, double MinW, double MinH)? _beforeMini;

    bool InMiniplayer => _beforeMini != null;

    void PushMiniState(Mpv? m)
    {
        try { m?.Command("script-message", "lumina-mini-state", InMiniplayer ? "yes" : "no"); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Shrinks to a small window in the bottom-right corner that stays on top of everything, or goes back to how it was.</summary>
    void ToggleMiniplayer()
    {
        if (InMiniplayer) { LeaveMiniplayer(); PushMiniState(_mpv); return; }
        if (_mpv == null || Stage.Visibility != Visibility.Visible) return;
        if (_fs.On) { _fs.Exit(); SendFullscreenState(); }
        _beforeMini = (new Rect(Left, Top, Width, Height), WindowState, Topmost, MinWidth, MinHeight);
        WindowState = WindowState.Normal;
        double.TryParse(_mpv.Get("video-params/aspect"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var aspect);
        var chrome = Math.Max(0, ActualHeight - Stage.ActualHeight);
        var wa = SystemParameters.WorkArea;
        var box = MiniGeometry.For(new MiniGeometry.Box(wa.Left, wa.Top, wa.Width, wa.Height), aspect, chrome);
        MinWidth = 280; MinHeight = 160;                // the normal minimum is far bigger than a miniplayer
        Topmost = true;
        Left = box.Left; Top = box.Top; Width = box.Width; Height = box.Height;
        PushMiniState(_mpv);
    }

    void LeaveMiniplayer()
    {
        if (_beforeMini is not { } b) return;
        _beforeMini = null;
        MinWidth = b.MinW; MinHeight = b.MinH;
        Topmost = b.Topmost;
        WindowState = WindowState.Normal;
        Left = b.Bounds.Left; Top = b.Bounds.Top; Width = b.Bounds.Width; Height = b.Bounds.Height;
        WindowState = b.State;
    }

    // ---- recording a stream ----

    string? _recordingPath;

    void PushRecordState(Mpv? m, bool on)
    {
        try { m?.Command("script-message", "lumina-record-state", on ? "yes" : "no"); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void ToggleRecording(Mpv m, string source, MediaItem? item)
    {
        if (_recordingPath is { } running)
        {
            var size = Recorder.Stop(m, running);
            _recordingPath = null;
            PushRecordState(m, false);
            m.ShowText($"Recording saved: {Path.GetFileName(running)} ({MediaInfo.Size(size)})", 4500);
            Status.Text = "Recording saved to " + running;
            return;
        }
        var title = item?.Title ?? m.Get("media-title") ?? "Recording";
        var (path, error) = Recorder.Start(m, source, Recorder.FolderFor(AppSettings.Get("recording.folder")), title, DateTime.Now);
        if (path == null) { PushRecordState(m, false); m.ShowText(error ?? "Could not start recording.", 6000); return; }
        _recordingPath = path;
        PushRecordState(m, true);
        m.ShowText("Recording started", 2500);
    }

    /// <summary>Leaving the video while recording keeps what was saved.</summary>
    void FinishRecording()
    {
        if (_recordingPath is not { } running) return;
        _recordingPath = null;
        try { if (_mpv != null) { var size = Recorder.Stop(_mpv, running); Status.Text = $"Recording saved to {running} ({MediaInfo.Size(size)})"; } }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    // ---- screen reader controls ----

    PlayerMirror? _mirror;
    MediaItem? _playingItem;

    void StartMirror()
    {
        _mirror ??= new PlayerMirror(
            name => { try { return _mpv?.Get(name); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { return null; } },
            args => { try { _mpv?.Command(args); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { } },
            () => _fs.On,
            () => Math.Max(1, AppSettings.Num("controls.skip")),
            () => GoNeighbour(-1, _playingItem), () => GoNeighbour(+1, _playingItem),
            () => { _fs.Toggle(); SendFullscreenState(); }, Stop);
        MirrorHost.Child ??= _mirror;
        MirrorHost.Visibility = Visibility.Visible;
        _mirror.Start();
    }

    void StopMirror()
    {
        _mirror?.Stop();
        MirrorHost.Visibility = Visibility.Collapsed;
    }

    // ---- parental lock ----

    bool _unlocked;                                   // the PIN was entered during this run of the app
    readonly AttemptLimiter _pinLimiter = new();

    /// <summary>Library items minus anything inside a locked folder while the app is locked.</summary>
    List<MediaItem> VisibleItems()
    {
        var items = Db.List();
        if (_unlocked || !LockStore.HasPin || LockStore.Folders.Count == 0) return items;
        var locked = LockedPaths.Matcher(LockStore.Folders);
        return items.Where(i => !locked(i.Path)).ToList();
    }

    bool IsLockedPath(string path) => !_unlocked && LockStore.HasPin && LockedPaths.IsLocked(path, LockStore.Folders);

    /// <summary>Asks for the PIN until it is right or the user gives up. Five wrong tries in a row pause the question for half a minute.</summary>
    bool AskForPin(string why)
    {
        if (!LockStore.HasPin) return true;
        while (true)
        {
            var now = DateTime.Now;
            if (!_pinLimiter.Allowed(now)) { ConfirmDialog.Tell(this, "Too many tries", $"Wait {Math.Ceiling(_pinLimiter.Wait(now).TotalSeconds)} seconds and try again."); return false; }
            var pin = ConfirmDialog.AskPin(this, "Enter your PIN", why, "Unlock");
            if (pin == null) return false;
            if (LockStore.CheckPin(pin)) { _pinLimiter.Succeeded(); return true; }
            _pinLimiter.Failed(DateTime.Now);
            ConfirmDialog.Tell(this, "That PIN is not right", "Try again.");
        }
    }

    bool SetNewPin()
    {
        if (LockStore.HasPin && !AskForPin("Enter your current PIN to change it.")) return false;
        var a = ConfirmDialog.AskPin(this, "Choose a PIN", "Four to eight digits.", "Next");
        if (a == null) return false;
        if (!Pin.IsValid(a)) { ConfirmDialog.Tell(this, "That is not a PIN", "A PIN is four to eight digits, numbers only."); return false; }
        var b = ConfirmDialog.AskPin(this, "Type it again", "To make sure it is what you meant.", "Save");
        if (b == null) return false;
        if (a != b) { ConfirmDialog.Tell(this, "The two did not match", "Nothing was changed."); return false; }
        LockStore.SetPin(a);
        return true;
    }

    void LockNow()
    {
        _unlocked = false;
        Refresh();
        if (_page == Page.Details && _detailsKey != null && !_entries.Any(e => e.Key == _detailsKey)) { _detailsKey = null; Navigate("home"); }
        if (_page == Page.Library) RenderLibrary();
    }

    void OnFolderUnlock(object sender, RoutedEventArgs e)
    {
        if (_unlocked) { LockNow(); RenderFolders(); return; }
        if (!AskForPin("Unlock the folders you locked, until you close Lumina or lock them again.")) return;
        _unlocked = true;
        Refresh();
        RenderFolders();
    }

    void OnLockFolder(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderRow row) return;
        if (row.Locked)
        {
            if (!_unlocked && !AskForPin("Take the lock off this folder.")) return;
            LockStore.SetFolderLocked(row.Path, false);
            FolderStatus.Text = $"{row.Path} is no longer locked.";
        }
        else
        {
            if (!LockStore.HasPin && !SetNewPin()) return;
            LockStore.SetFolderLocked(row.Path, true);
            _unlocked = false;                          // locking takes effect at once
            FolderStatus.Text = $"{row.Path} is locked. Its videos are hidden until you enter the PIN.";
        }
        Refresh();
        RenderFolders();
    }

    // ---- sleep timer, tray ----

    readonly SleepTimer _sleep = new();
    System.Windows.Threading.DispatcherTimer? _sleepTick;
    string _sleepShown = "";
    TrayIcon? _tray;
    bool _quitting, _trayHinted;

    void PushSleepState(Mpv? m)
    {
        var label = _sleep.Label(DateTime.Now);
        _sleepShown = label;
        try { m?.Command("script-message", "lumina-sleep-state", label); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void SleepCommand(Mpv m, string arg)
    {
        var now = DateTime.Now;
        if (arg == "cycle") _sleep.Cycle(now);
        else if (arg == "off") _sleep.Off();
        else if (arg == "eof") _sleep.StartAtEnd();
        else if (double.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes)) _sleep.StartMinutes(minutes, now);
        else return;
        PushSleepState(m);
        m.ShowText("Sleep timer: " + _sleep.Label(now), 1800);
        EnsureSleepTick();
    }

    void EnsureSleepTick()
    {
        _sleepTick ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sleepTick.Tick -= OnSleepTick;
        _sleepTick.Tick += OnSleepTick;
        _sleepTick.Start();
    }

    void OnSleepTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        if (_sleep.TimeIsUp(now)) { FireSleep(); return; }
        if (_sleep.Current == SleepTimer.Mode.Off) { _sleepTick?.Stop(); return; }
        var label = _sleep.Label(now);
        if (label != _sleepShown) PushSleepState(_mpv);       // the minutes counting down
    }

    /// <summary>The time is up (or the file ended): stop watching, then do what the user chose for this moment.</summary>
    void FireSleep()
    {
        _sleep.Off();
        _sleepTick?.Stop();
        var action = AppSettings.Get("playback.afterSleep");
        try { _mpv?.Set("pause", "yes"); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
        PushSleepState(_mpv);
        switch (action)
        {
            case "stop": Stop(); Status.Text = "Sleep timer: stopped."; break;
            case "sleep": case "shutdown":
                try
                {
                    PowerAction.Run(action);
                    var text = action == "sleep" ? "Sleep timer: putting the PC to sleep." : "Sleep timer: shutting down in one minute. Run shutdown /a to cancel.";
                    _mpv?.ShowText(text, 6000); Status.Text = text;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Lumina.Diagnostics.Log.Error("Sleep timer could not " + action, e); Status.Text = "Sleep timer: Windows would not let Lumina " + action + "."; }
                break;
            default: _mpv?.ShowText("Sleep timer: paused", 4000); Status.Text = "Sleep timer: paused."; break;
        }
        _tray?.Balloon("Lumina", "The sleep timer ended.");
    }

    // ---- tray ----

    void EnsureTray()
    {
        if (_tray != null) return;
        _tray = new TrayIcon("Lumina");
        _tray.OpenRequested += () => Dispatcher.BeginInvoke(RestoreFromTray);
        _tray.QuitRequested += () => Dispatcher.BeginInvoke(() => { _quitting = true; Close(); });
        _tray.Show();
    }

    void RemoveTray() { _tray?.Dispose(); _tray = null; }

    void RestoreFromTray()
    {
        if (!IsVisible) Show();
        BringToFront();
    }

    void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_quitting || !AppSettings.Bool("general.tray")) { RemoveTray(); return; }
        // close button with the tray option on: tuck the window away and keep the app (and its tray icon) running
        e.Cancel = true;
        try { _mpv?.Set("pause", "yes"); } catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
        EnsureTray();
        Hide();
        if (!_trayHinted) { _trayHinted = true; _tray?.Balloon("Lumina is still running", "Click the tray icon to open it again."); }
    }

    // ---- bookmarks ----

    void PushBookmarks(Mpv m, string path)
    {
        try
        {
            var list = Db.Bookmarks(path).Select(b => new { t = b.Position, label = b.Label });
            m.Command("script-message", "lumina-bookmarks", System.Text.Json.JsonSerializer.Serialize(list));
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { }
    }

    void ToggleBookmark(Mpv m, string path)
    {
        if (!double.TryParse(m.Get("time-pos"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)) return;
        var (added, all) = Db.ToggleBookmark(path, t);
        PushBookmarks(m, path);
        m.ShowText(added ? $"Bookmark added at {MediaItem.Clock(t)}" : "Bookmark removed", 1800);
    }

    // ---- seek bar previews ----

    SeekPreview? _seekPreview;
    CancellationTokenSource? _thumbCts;
    string? _thumbDir;
    int _thumbCount;
    double _thumbStep;
    readonly Dictionary<string, BitmapImage> _thumbPictures = new();

    /// <summary>Makes the preview pictures for the playing file in the background, at low priority, unless they already exist or it is a stream.</summary>
    void StartThumbnails(Mpv m, string path)
    {
        StopThumbnails();
        if (!AppSettings.Bool("interface.seekThumbs") || path.Contains("://") || !File.Exists(path)) return;
        if (!double.TryParse(m.Get("duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dur) || dur <= 1) return;
        (_thumbCount, _thumbStep) = Thumbnails.Plan(dur);
        _thumbDir = Thumbnails.FolderFor(path);
        var cts = _thumbCts = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; Thumbnails.Generate(path, dur, cts.Token); }
            catch (Exception e) when (e is IOException or InvalidOperationException) { Lumina.Diagnostics.Log.Error("Seek previews could not be made", e); }
        }, cts.Token);
    }

    void StopThumbnails()
    {
        _thumbCts?.Cancel();
        _thumbCts = null;
        _thumbDir = null;
        _thumbPictures.Clear();
        _seekPreview?.HidePreview();
    }

    /// <summary>The controller reports a time and a spot on the seek bar (window pixels); show the nearest picture just above it.</summary>
    void ShowSeekPreview(string timeText, string xText, string yText)
    {
        if (!AppSettings.Bool("interface.seekThumbs") || Stage.Visibility != Visibility.Visible) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(timeText, System.Globalization.NumberStyles.Float, inv, out var t) || !double.TryParse(xText, System.Globalization.NumberStyles.Float, inv, out var x)
            || !double.TryParse(yText, System.Globalization.NumberStyles.Float, inv, out var y)) return;
        _seekPreview ??= new SeekPreview(this);
        ImageSource? picture = null;
        if (_thumbDir != null && Thumbnails.Nearest(_thumbDir, _thumbCount, _thumbStep, t) is { } p)
        {
            if (_thumbPictures.Count > 40) _thumbPictures.Clear();
            if (!_thumbPictures.TryGetValue(p, out var img)) { try { img = _thumbPictures[p] = LoadImage(p); } catch (Exception e) when (e is IOException or NotSupportedException) { img = null; } }
            picture = img;
        }
        var origin = Stage.PointToScreen(new Point(0, 0));
        var width = Stage.ActualWidth * VisualTreeHelper.GetDpi(this).DpiScaleX;
        _seekPreview.ShowAt(picture, MediaItem.Clock(t), origin.X + x, origin.Y + y - 10, origin.X + 8, origin.X + width - 8);
    }

    async Task OnFileLoaded(Mpv m, string path)
    {
        try
        {
            if (path.Contains("://")) return; // no file to hash for a stream
            var hash = await Task.Run(() => OpenSubtitles.Hash(path));
            if (_mpv != m) return;
            if (OpenSubtitles.Cached(path, hash) is { } cached) { m.SubAdd(cached); m.ShowText("Subtitles loaded from cache", 2500); }
            else if (!m.HasSubtitleTrack()) m.ShowText("No subtitles in this file. Press Ctrl+B to search OpenSubtitles", 4000);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { } // file gone or player closed
    }

    bool _subsBusy;

    async Task FindSubs(Mpv m, string path, MediaItem? item)
    {
        if (_subsBusy) return;
        _subsBusy = true;
        try
        {
            m.ShowText("Searching OpenSubtitles", 15000);
            var p = item != null
                ? new Parsed(item.Kind, item.Title, item.Year, item.Season, item.Episode)
                : NameParser.Parse(Path.GetFileNameWithoutExtension(path));
            var r = await OpenSubtitles.FetchAsync(path, p.Title, p.Year, p.Season, p.Episode);
            if (_mpv != m) return;
            if (r.Path != null) m.SubAdd(r.Path);
            m.ShowText(r.Message, 4000);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally { _subsBusy = false; }
    }

    void OnPlayerKey(object sender, KeyEventArgs e)
    {
        if (_mpv == null || Stage.Visibility != Visibility.Visible) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Space or Key.Enter && Keyboard.FocusedElement is DependencyObject focused && _mirror != null && _mirror.IsAncestorOf(focused)) return;
        if (MpvKeys.Name(key, Keyboard.Modifiers) is { } name) { SendKey(name); e.Handled = true; }
    }

    void OnPlayerText(object sender, TextCompositionEventArgs e)
    {
        if (_mpv == null || Stage.Visibility != Visibility.Visible) return;
        if (MpvKeys.Char(e.Text) is { } name) { SendKey(name); e.Handled = true; }
    }

    // WPF-UI's caption buttons carry no accessible name; give them one so a screen reader can say what they do.
    void NameTitleBarButtons()
    {
        var names = new Dictionary<string, string> { ["TitleBarMinimizeButton"] = "Minimize", ["TitleBarMaximizeButton"] = "Maximize", ["TitleBarCloseButton"] = "Close" };
        void Walk(DependencyObject d)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is FrameworkElement fe)
                {
                    var key = System.Windows.Automation.AutomationProperties.GetAutomationId(fe) is { Length: > 0 } id ? id : fe.Name;
                    if (names.TryGetValue(key, out var label)) System.Windows.Automation.AutomationProperties.SetName(fe, label);
                }
                Walk(c);
            }
        }
        Walk(this);
    }

    // ================= queue and playlists =================

    void ApplyQueueSettings()
    {
        _queue.Repeat = AppSettings.Get("playback.queueRepeat") switch { "all" => RepeatMode.All, "one" => RepeatMode.One, _ => RepeatMode.Off };
        _queue.SetShuffle(AppSettings.Bool("playback.shuffle"));
    }

    /// <summary>Start playing several things one after another.</summary>
    void PlayQueue(IReadOnlyList<QueueItem> items, int start = 0)
    {
        if (items.Count == 0) return;
        ApplyQueueSettings();
        _queue.Load(items, start);
        PlayQueueItem(_queue.CurrentItem!);
    }

    void PlayQueueItem(QueueItem it)
    {
        var library = Db.List().FirstOrDefault(x => string.Equals(x.Path, it.Path, StringComparison.OrdinalIgnoreCase));
        Play(it.Path, library, keepQueue: true);
    }

    /// <summary>Open whatever the user gave us: one file plays alone, several files or a folder become a queue.</summary>
    void OpenPaths(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        // a web address on the command line (or sent by a second launch) plays as a stream
        if (list.Count == 1 && Uri.TryCreate(list[0], UriKind.Absolute, out var web) && web.Scheme is "http" or "https" or "rtsp" or "rtmp" or "ftp") { Play(list[0], null); return; }
        var items = PlaybackQueue.FromPaths(list);
        if (items.Count == 0)
        {
            // Not a video we know: let mpv try a single file anyway (odd extensions), otherwise say so.
            if (list.Count == 1 && File.Exists(list[0])) Play(list[0], null);
            else Status.Text = "No videos found in what you opened";
            return;
        }
        if (items.Count == 1) Play(items[0].Path, Db.List().FirstOrDefault(x => string.Equals(x.Path, items[0].Path, StringComparison.OrdinalIgnoreCase)));
        else PlayQueue(items);
    }

    void GoNeighbour(int dir, MediaItem? item)
    {
        if (_queue.Count > 0)
        {
            if ((dir > 0 ? _queue.Next() : _queue.Previous()) is { } q) PlayQueueItem(q);
            return;
        }
        if (item == null) return;
        var all = Db.List();
        if ((dir > 0 ? MediaItem.NextEpisode(all, item) : MediaItem.PreviousEpisode(all, item)) is { } ep) Play(ep.Path, ep);
    }

    /// <summary>What happens when a video plays to its last frame.</summary>
    void AfterEnd(MediaItem? item)
    {
        if (_sleep.StopsAtEnd) { FireSleep(); return; }      // "stop when this ends" wins over the queue and the next episode
        if (_queue.Count > 0)
        {
            if (_queue.Advance() is { } q) PlayQueueItem(q);
            return;
        }
        if (item != null && AppSettings.Get("playback.afterEpisode") == "next" && MediaItem.NextEpisode(Db.List(), item) is { } next) Play(next.Path, next);
    }

    void SendNeighbors(Mpv m, MediaItem? item)
    {
        bool prev, next;
        if (_queue.Count > 0) { prev = _queue.HasPrevious; next = _queue.HasNext; }
        else
        {
            var all = item == null ? null : Db.List();
            prev = item != null && MediaItem.PreviousEpisode(all!, item) != null;
            next = item != null && MediaItem.NextEpisode(all!, item) != null;
        }
        try { m.Command("script-message", "lumina-neighbors", prev ? "yes" : "no", next ? "yes" : "no"); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Send the queue to the controller's panel.</summary>
    void PushQueue(Mpv m)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            cur = _queue.Current,
            shuffle = _queue.Shuffle,
            repeat = _queue.Repeat.ToString().ToLowerInvariant(),
            items = _queue.Items.Select(i => i.Title).ToArray(),
        });
        try { m.Command("script-message", "lumina-queue", json); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void QueueRemove(int index)
    {
        var wasCurrent = index == _queue.Current;
        _queue.Remove(index);
        if (_mpv is { } m) { if (wasCurrent && _queue.CurrentItem is { } now) PlayQueueItem(now); else { PushQueue(m); SendNeighbors(m, null); } }
    }

    void SaveQueueAsPlaylist()
    {
        if (_queue.Count == 0) return;
        var name = ConfirmDialog.Prompt(this, "Save this queue as a playlist", "", "Save", "Playlist name");
        if (name == null) return;
        Db.SavePlaylist(name, _queue.Items);
        _mpv?.ShowText($"Saved playlist {name}", 2500);
    }

    void ShowMediaInfo(Mpv m)
    {
        var rows = MediaInfo.Build(m.Get);
        var panel = new StackPanel();
        foreach (var section in rows.GroupBy(r => r.Section))
        {
            panel.Children.Add(new TextBlock { Text = section.Key, Style = (Style)FindResource("Lumina.Text.Caption"), Margin = new Thickness(2, panel.Children.Count == 0 ? 0 : 16, 0, 6) });
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var i = 0;
            foreach (var r in section)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = r.Label, Style = (Style)FindResource("Lumina.Text.Muted"), Margin = new Thickness(0, 3, 12, 3), TextTrimming = TextTrimming.CharacterEllipsis };
                var v = new TextBlock { Text = r.Value, Style = (Style)FindResource("Lumina.Text"), Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap };
                Grid.SetRow(l, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
                g.Children.Add(l); g.Children.Add(v);
                i++;
            }
            panel.Children.Add(g);
        }
        if (rows.Count == 0) panel.Children.Add(new TextBlock { Text = "Nothing is playing.", Style = (Style)FindResource("Lumina.Text.Muted") });
        ConfirmDialog.ShowContent(this, "Media info", new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 700);
    }

    void JumpToTime(Mpv m)
    {
        var dur = double.TryParse(m.Get("duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        var text = ConfirmDialog.Prompt(this, "Jump to time", dur > 0 ? $"This video is {Timecode.Format(dur)} long. Try 1:30, 1:02:03 or 90." : "Try 1:30, 1:02:03 or 90.", "Go", "Time, like 1:30");
        if (text == null) return;
        if (Timecode.Parse(text) is not { } t || (dur > 0 && t > dur)) { m.ShowText("That time is not in this video", 2500); return; }
        try { m.Command("seek", t.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "absolute+exact"); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void RenderPlaylists()
    {
        // Edit one collection in place: swapping ItemsSource leaves WPF's automation tree without the rows' buttons.
        if (PlaylistList.ItemsSource != _playlistRows) PlaylistList.ItemsSource = _playlistRows;
        var fresh = Db.Playlists().Select(p => new PlaylistRow(p.Id, p.Name, Count(p.Count, "item"))).ToList();
        if (!fresh.SequenceEqual(_playlistRows)) // records compare by value; leaving unchanged rows alone keeps their automation peers alive
        {
            _playlistRows.Clear();
            foreach (var r in fresh) _playlistRows.Add(r);
        }
        PlaylistEmpty.Visibility = _playlistRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnPlaylistPlay(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlaylistRow row) return;
        var all = Db.PlaylistItems(row.Id);
        var items = all.Where(i => i.Path.Contains("://") || File.Exists(i.Path)).ToList();
        if (items.Count == 0) { ConfirmDialog.Tell(this, "Nothing to play", "None of the files in this playlist can be found. Are the drives connected?"); return; }
        PlayQueue(items);
        if (items.Count < all.Count) _mpv?.ShowText($"{all.Count - items.Count} missing file(s) skipped", 4000);
    }

    void OnPlaylistRename(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlaylistRow row) return;
        var name = ConfirmDialog.Prompt(this, "Rename playlist", "", "Rename", "Playlist name", row.Name);
        if (name == null || name == row.Name) return;
        if (!Db.RenamePlaylist(row.Id, name)) ConfirmDialog.Tell(this, "That name is taken", "Another playlist already uses it. Pick a different name.");
        RenderPlaylists();
    }

    void OnPlaylistDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not PlaylistRow row) return;
        if (!ConfirmDialog.Ask(this, "Delete this playlist?", $"{row.Name} is removed. The video files are not touched.", "Delete")) return;
        Db.DeletePlaylist(row.Id);
        RenderPlaylists();
    }

    // ================= audio effects =================

    static string LabelOf(Lumina.Settings.Choice[] choices, string value) => choices.FirstOrDefault(c => c.Value == value)?.Label ?? value;

    /// <summary>Rebuild the filter chain from settings and hand it to the player. Safe to call whenever nothing is playing.</summary>
    void ApplyAudioFx()
    {
        if (_mpv is not { } m) return;
        try { m.Set("af", AudioFx.Compose(AudioFxSettings.FromSettings())); PushFx(m); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void PushFx(Mpv m)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            eq = LabelOf(AudioFx.PresetChoices, AppSettings.Get("audio.eqPreset")),
            comp = LabelOf(AudioFx.CompressorChoices, AppSettings.Get("audio.compressor")),
            space = LabelOf(AudioFx.SpatialChoices, AppSettings.Get("audio.spatial")),
            width = (int)AppSettings.Num("audio.stereoWidth"),
            norm = AppSettings.Bool("audio.normalize"),
            night = AppSettings.Bool("audio.night"),
        });
        try { m.Command("script-message", "lumina-fx-state", json); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Choosing a preset fills the ten band sliders; moving a band afterwards makes it Custom.</summary>
    void ApplyEqPreset()
    {
        if (!AudioFx.Presets.TryGetValue(AppSettings.Get("audio.eqPreset"), out var gains)) return;
        AppSettings.Set(gains.Select((g, i) => ("audio.eq" + (i + 1), Lumina.Settings.Catalog.Num(g))).ToDictionary(x => x.Item1, x => x.Item2));
        for (var i = 1; i <= gains.Length; i++) _settings?.Refresh("audio.eq" + i);
    }

    void FxCommand(string name, string? arg)
    {
        switch (name)
        {
            case "eq": AppSettings.Set("audio.eqPreset", AudioFx.Next(AudioFx.PresetChoices, AppSettings.Get("audio.eqPreset"))); ApplyEqPreset(); break;
            case "comp": AppSettings.Set("audio.compressor", AudioFx.Next(AudioFx.CompressorChoices, AppSettings.Get("audio.compressor"))); break;
            case "space": AppSettings.Set("audio.spatial", AudioFx.Next(AudioFx.SpatialChoices, AppSettings.Get("audio.spatial"))); break;
            case "width":
                var step = double.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 10;
                AppSettings.Set("audio.stereoWidth", Lumina.Settings.Catalog.Num(Math.Clamp(AppSettings.Num("audio.stereoWidth") + step, 0, 100)));
                break;
            case "norm": AppSettings.Set("audio.normalize", AppSettings.Bool("audio.normalize") ? "false" : "true"); break;
            case "night": AppSettings.Set("audio.night", AppSettings.Bool("audio.night") ? "false" : "true"); break;
            case "refresh": break;
            default: return;
        }
        _settings?.Reload();      // the Settings page may be open behind the player
        ApplyAudioFx();
    }

    void OnOpenStream(object sender, RoutedEventArgs e) => OpenStream();

    static readonly string[] StreamSchemes = { "http", "https", "rtsp", "rtmp", "udp", "ftp", "file" };

    List<string> RecentStreams() => RecentFiles.Deserialize(Db.KvGet("recent_streams"));

    void OpenStream()
    {
        var url = ConfirmDialog.PromptWithRecents(this, "Open a network stream",
            "Paste a web address. Direct video links, HLS and DASH playlists, FTP, and anything yt-dlp understands all work.", "Play", "https://example.com/video.m3u8",
            RecentStreams(), clearRecents: () => Db.KvDelete("recent_streams"));
        if (url == null) return;
        if (Lumina.Network.CredentialStore.ShareServer(url) != null || url.StartsWith(@"\\"))
        {
            Status.Text = "That is a file share. Add it as a library folder, or open a file from it.";
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !StreamSchemes.Contains(u.Scheme))
        {
            ConfirmDialog.Tell(this, "That is not a web address", "Start it with http://, https://, ftp://, rtsp:// or rtmp://.");
            return;
        }
        _ = PlayStreamAsync(u);
    }

    string? _streamAuth;      // Authorization header value for the next stream, set by PlayStreamAsync
    string? _streamLoadUrl;   // address with credentials for schemes that cannot take a header (ftp); never shown or stored

    /// <summary>Check the address, sign in if it asks, then play. Each failure ends with a sentence the user can act on.</summary>
    async Task PlayStreamAsync(Uri uri)
    {
        var store = new Lumina.Network.CredentialStore();
        var target = Lumina.Network.CredentialStore.TargetFor(uri);
        Lumina.Network.Login? login = null;
        bool fresh = false, remember = true;
        // user:password@host inside the address is honoured once and then stripped, so it never reaches the list of recents
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            login = new Lumina.Network.Login(target, Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
            uri = new UriBuilder(uri) { UserName = "", Password = "" }.Uri; fresh = true;
        }
        login ??= store.Load(target);

        Lumina.Network.ProbeOutcome outcome;
        for (var attempt = 0; ; attempt++)
        {
            Status.Text = "Checking " + uri.Host;
            outcome = await Lumina.Network.StreamProbe.CheckAsync(uri, login, AppSettings.Get("network.proxy"), AppSettings.Bool("network.verifyTls"));
            Status.Text = "";
            if (outcome.CanPlay) break;
            if (outcome.Result is Lumina.Network.ProbeResult.NeedsLogin or Lumina.Network.ProbeResult.LoginRejected && attempt < 3)
            {
                var c = ConfirmDialog.SignIn(this, uri.Host, rejected: outcome.Result == Lumina.Network.ProbeResult.LoginRejected, knownUser: login?.User);
                if (c == null) return;
                login = new Lumina.Network.Login(target, c.User, c.Password); fresh = true; remember = c.Remember;
                continue;
            }
            ConfirmDialog.Tell(this, "Cannot open " + uri.Host, outcome.Message);
            return;
        }

        if (login != null && fresh && remember)
        {
            try { store.Save(target, login.User, login.Password); }
            catch (InvalidOperationException e) { Lumina.Diagnostics.Log.Error("Could not save a login", e); }
        }
        var clean = uri.ToString();
        try { Db.KvSet("recent_streams", RecentFiles.Serialize(RecentFiles.Add(RecentStreams(), clean))); }
        catch (Microsoft.Data.Sqlite.SqliteException e) { Lumina.Diagnostics.Log.Error("Could not remember a stream", e); }

        _streamAuth = uri.Scheme is "http" or "https" ? Lumina.Network.StreamProbe.AuthorizationFor(login) : null;
        _streamLoadUrl = login != null && uri.Scheme == "ftp"
            ? new UriBuilder(uri) { UserName = Uri.EscapeDataString(login.User), Password = Uri.EscapeDataString(login.Password) }.Uri.ToString()
            : null;
        Play(clean, null);
    }

    void PushControls()
    {
        try { _mpv?.Command("script-message", "lumina-controls", AppSettings.Get("interface.controls")); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void EditController()
    {
        var panel = new StackPanel();
        void Save(List<string> order, HashSet<string> on)
        {
            var text = ControllerLayout.Format(order.Where(on.Contains));
            AppSettings.Set("interface.controls", ControllerLayout.IsDefault(text) ? "" : text);
            _settings?.Refresh("interface.controls");
            PushControls();
        }
        void Build()
        {
            panel.Children.Clear();
            var stored = ControllerLayout.Parse(Config.Raw("interface.controls"));
            var order = stored.Concat(ControllerLayout.Default.Where(d => !stored.Contains(d))).ToList();   // hidden ones stay listed, after the shown
            var on = stored.ToHashSet();
            var reset = new Button { Content = "Reset to standard", Style = (Style)FindResource("Lumina.Btn.Secondary"), Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += (_, _) => { AppSettings.Set("interface.controls", ""); _settings?.Refresh("interface.controls"); PushControls(); Build(); };
            panel.Children.Add(reset);
            for (int i = 0; i < order.Count; i++)
            {
                var id = order[i]; var label = ControllerLayout.Known.First(k => k.Id == id).Label; var at = i;
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var show = new CheckBox { Content = label, IsChecked = on.Contains(id), IsEnabled = id != "gear", HorizontalAlignment = HorizontalAlignment.Left };
                System.Windows.Automation.AutomationProperties.SetName(show, "Show " + label);
                var up = new Button { Content = "Left", Style = (Style)FindResource("Lumina.Btn.Secondary"), MinHeight = 30, Margin = new Thickness(6, 0, 0, 0), IsEnabled = i > 0 };
                var down = new Button { Content = "Right", Style = (Style)FindResource("Lumina.Btn.Secondary"), MinHeight = 30, Margin = new Thickness(6, 0, 0, 0), IsEnabled = i < order.Count - 1 };
                System.Windows.Automation.AutomationProperties.SetName(up, "Move " + label + " left");
                System.Windows.Automation.AutomationProperties.SetName(down, "Move " + label + " right");
                DockPanel.SetDock(down, Dock.Right); DockPanel.SetDock(up, Dock.Right);
                row.Children.Add(down); row.Children.Add(up); row.Children.Add(show);
                show.Click += (_, _) => { if (show.IsChecked == true) on.Add(id); else on.Remove(id); Save(order, on); Build(); };
                up.Click += (_, _) => { (order[at - 1], order[at]) = (order[at], order[at - 1]); Save(order, on); Build(); };
                down.Click += (_, _) => { (order[at + 1], order[at]) = (order[at], order[at + 1]); Save(order, on); Build(); };
                panel.Children.Add(row);
            }
            panel.Children.Add(new TextBlock { Text = "Order is left to right on the controller. The gear always stays so Settings is one click away. Press H while playing to hide all controls.", Style = (Style)FindResource("Lumina.Text.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
        }
        Build();
        ConfirmDialog.ShowContent(this, "Customise the controller", panel, 560);
    }

    void ShowProfiles()
    {
        var list = new ListBox { MaxHeight = 260, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(list, "Profiles");
        var note = new TextBlock { Style = (Style)FindResource("Lumina.Text.Muted"), Margin = new Thickness(2, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
        Button B(string text) => new() { Content = text, Style = (Style)FindResource("Lumina.Btn.Secondary"), Margin = new Thickness(0, 0, 10, 8) };
        var apply = B("Apply"); var save = B("Save current as..."); var del = B("Delete"); var export = B("Export"); var import = B("Import"); var undo = B("Undo last apply");
        void Fill()
        {
            list.ItemsSource = Profiles.All.ToList();
            undo.IsEnabled = Profiles.UndoName() != null;
            undo.Content = undo.IsEnabled ? $"Undo {Profiles.UndoName()}" : "Undo last apply";
        }
        string? Selected() => list.SelectedItem as string;
        void AfterChange(IReadOnlyList<string> keys)
        {
            _settings?.Reload();
            ApplyAudioFx();
            note.Text = keys.Count == 0 ? "Nothing changed." : "Applied. Picture, subtitle and playback changes start with the next video.";
        }
        apply.Click += (_, _) =>
        {
            if (Selected() is not { } n) { note.Text = "Pick a profile first."; return; }
            AfterChange(Profiles.Apply(n)); Fill();
        };
        undo.Click += (_, _) => { if (Profiles.Undo()) AfterChange(new[] { "undo" }); Fill(); note.Text = "Put back the way it was."; };
        save.Click += (_, _) =>
        {
            var name = ConfirmDialog.Prompt(this, "Save current settings as a profile", "Picture, sound, subtitle and playback settings that differ from the defaults are saved.", "Save", "Profile name");
            if (name == null) return;
            try { Profiles.Save(name); note.Text = $"Saved {name.Trim()}."; } catch (ArgumentException e) { note.Text = e.Message; }
            Fill();
        };
        del.Click += (_, _) =>
        {
            if (Selected() is not { } n) return;
            if (Profiles.BuiltIn.Contains(n)) { note.Text = "Built-in profiles stay."; return; }
            Profiles.Delete(n); Fill();
        };
        export.Click += (_, _) =>
        {
            if (Selected() is not { } n) { note.Text = "Pick a profile first."; return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Lumina profile (*.json)|*.json", FileName = n + ".json" };
            if (dlg.ShowDialog(this) != true) return;
            try { File.WriteAllText(dlg.FileName, Profiles.Export(n)); note.Text = "Exported."; } catch (IOException e) { note.Text = "Could not write the file: " + e.Message; }
        };
        import.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Lumina profile (*.json)|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            try { note.Text = $"Imported {Profiles.Import(File.ReadAllText(dlg.FileName))}."; } catch (Exception e) when (e is IOException or FormatException) { note.Text = e.Message; }
            Fill();
        };
        Fill();
        var row = new WrapPanel { Children = { apply, save, del, export, import, undo } };
        ConfirmDialog.ShowContent(this, "Profiles", new StackPanel { Children = { list, note, row } }, 620);
    }

    void ShowLogins()
    {
        var store = new Lumina.Network.CredentialStore();
        var list = new ListBox { MaxHeight = 260, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(list, "Saved logins");
        void Fill() => list.ItemsSource = store.List().Select(l => $"{l.Target}    {l.User}").ToList();
        Fill();
        var empty = new TextBlock { Text = "No saved logins.", Style = (Style)FindResource("Lumina.Text.Muted"), Margin = new Thickness(2, 6, 0, 10) };
        var forget = new Button { Content = "Forget selected", Style = (Style)FindResource("Lumina.Btn.Secondary") };
        var add = new Button { Content = "Add login", Style = (Style)FindResource("Lumina.Btn.Secondary"), Margin = new Thickness(10, 0, 0, 0) };
        forget.Click += (_, _) =>
        {
            if (list.SelectedItem is not string row) return;
            var target = row.Split("    ")[0];
            store.Delete(target);
            if (Lumina.Network.CredentialStore.ShareServer(target) is { } server) Lumina.Network.CredentialStore.DeleteForWindowsShare(server);
            Fill();
        };
        add.Click += (_, _) =>
        {
            var addr = ConfirmDialog.Prompt(this, "Add a login", "The server address, for example https://nas.local:8443, ftp://files.example.com or \\\\nas for a Windows file share.", "Next", "Server address");
            if (addr == null) return;
            string target; string host;
            if (Lumina.Network.CredentialStore.ShareServer(addr) is { } server) { target = @"\\" + server; host = server; }
            else if (Uri.TryCreate(addr, UriKind.Absolute, out var u) && StreamSchemes.Contains(u.Scheme)) { target = Lumina.Network.CredentialStore.TargetFor(u); host = u.Host; }
            else { ConfirmDialog.Tell(this, "That is not a server address", "Start it with http://, https://, ftp:// or \\\\ for a Windows share."); return; }
            var c = ConfirmDialog.SignIn(this, host, rejected: false);
            if (c == null) return;
            try
            {
                store.Save(target, c.User, c.Password);
                if (Lumina.Network.CredentialStore.ShareServer(target) is { } sv) Lumina.Network.CredentialStore.SaveForWindowsShare(sv, c.User, c.Password);
            }
            catch (InvalidOperationException e) { ConfirmDialog.Tell(this, "Could not save the login", e.Message); }
            Fill();
        };
        var panel = new StackPanel { Children = { list, empty, new StackPanel { Orientation = Orientation.Horizontal, Children = { forget, add } } } };
        ConfirmDialog.ShowContent(this, "Saved logins", panel, 620);
    }

    // ================= updates =================

    static Uri? UpdateFeedUri()
    {
        var text = Environment.GetEnvironmentVariable("LUMINA_UPDATE_FEED") is { Length: > 0 } e ? e : AppSettings.Get("updates.feedUrl");
        return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) ? u : null;
    }

    bool _updating;

    /// <summary>Ask the update server, tell the user, and (only if they agree) download, verify and run the installer.</summary>
    async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (_updating) return;
        var feed = UpdateFeedUri();
        if (feed == null)
        {
            if (userAsked) ConfirmDialog.Tell(this, "No update address", "Paste the address of the update file in Settings, Advanced, under Update address.");
            return;
        }
        _updating = true;
        try
        {
            Status.Text = userAsked ? "Checking for updates" : "";
            var r = await Lumina.Updates.UpdateCheck.CheckAsync(feed, AppInfo.VersionText);
            Status.Text = "";
            if (!userAsked)
            {
                try { Db.KvSet("update_last_check", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()); } catch (Microsoft.Data.Sqlite.SqliteException) { }
                if (r.Status == Lumina.Updates.UpdateStatus.Available) Status.Text = $"Lumina {r.Info!.Version} is available. Settings, Advanced.";
                return;
            }
            if (r.Status != Lumina.Updates.UpdateStatus.Available) { ConfirmDialog.Tell(this, r.Status == Lumina.Updates.UpdateStatus.UpToDate ? "Lumina is up to date" : "Could not check for updates", r.Message); return; }

            var info = r.Info!;
            var notes = string.IsNullOrWhiteSpace(info.Notes) ? "" : Environment.NewLine + Environment.NewLine + info.Notes;
            if (!ConfirmDialog.Ask(this, $"Lumina {info.Version} is available", $"You have {AppInfo.VersionText}. Lumina downloads it, checks it against its checksum, then closes and installs it.{notes}", "Download and install")) return;

            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LuminaUpdate");
            try
            {
                var progress = new Progress<double>(p => Status.Text = $"Downloading update {(int)(p * 100)}%");
                var file = await Lumina.Updates.UpdateCheck.DownloadAsync(info, folder, progress);
                Status.Text = "";
                Lumina.Diagnostics.Log.Info("Update downloaded and verified: " + System.IO.Path.GetFileName(file));
                if (Environment.GetEnvironmentVariable("LUMINA_UPDATE_NO_LAUNCH") == "1") { ConfirmDialog.Tell(this, "Update ready", "Downloaded and verified. (Not started: test mode.)"); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, Lumina.Updates.UpdateCheck.InstallerArguments) { UseShellExecute = true });
                Close();                                                  // the installer replaces our files, so we must not be running
            }
            catch (Lumina.Updates.UpdateException e) { Status.Text = ""; ConfirmDialog.Tell(this, "The update was not installed", e.Message); }
            catch (Exception e) when (e is System.IO.IOException or System.ComponentModel.Win32Exception) { Status.Text = ""; ConfirmDialog.Tell(this, "The update was not installed", e.Message); }
        }
        finally { _updating = false; }
    }

    void MaybeCheckOnStart()
    {
        if (!AppSettings.Bool("updates.onStart") || UpdateFeedUri() == null) return;
        // once a day is plenty
        if (long.TryParse(Db.KvGet("update_last_check"), out var last) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last < 86400) return;
        _ = CheckForUpdatesAsync(userAsked: false);
    }

    // ================= custom keys =================

    static Dictionary<string, string> KeyOverrides() => Shortcuts.ParseOverrides(Config.Raw("shortcuts"));

    void SaveKeyOverrides(Dictionary<string, string> previous, Dictionary<string, string> now)
    {
        AppSettings.Set("shortcuts", Shortcuts.SerializeOverrides(now));
        ApplyKeysLive(previous, now);
        _settings?.Reload();
    }

    /// <summary>Rebind the running player at once so the change does not wait for the next video.</summary>
    void ApplyKeysLive(IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> now)
    {
        if (_mpv is not { } m) return;
        try
        {
            var live = new HashSet<string>(Shortcuts.Actions.SelectMany(a => Shortcuts.KeysFor(a, now)), StringComparer.Ordinal);
            foreach (var k in previous.Values) if (!live.Contains(k)) m.Command("keybind", k, "ignore");                    // a key that was custom and is not any more
            foreach (var a in Shortcuts.Actions) foreach (var k in Shortcuts.KeysFor(a, now)) m.Command("keybind", k, a.Command.Contains("lumina-skip") ? "repeatable " + a.Command : a.Command);
            foreach (var a in Shortcuts.Actions.Where(a => now.ContainsKey(a.Id)))                                         // defaults a custom key left behind
                foreach (var d in a.DefaultKeys) if (!live.Contains(d)) m.Command("keybind", d, "ignore");
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
    }

    void EditKeys()
    {
        var panel = new StackPanel();
        void Build()
        {
            panel.Children.Clear();
            var ov = KeyOverrides();
            var all = new Button { Content = "Reset all keys", Style = (Style)FindResource("Lumina.Btn.Secondary"), Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = ov.Count > 0 };
            all.Click += (_, _) => { SaveKeyOverrides(KeyOverrides(), new Dictionary<string, string>()); Build(); };
            panel.Children.Add(all);
            foreach (var a in Shortcuts.Actions)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var change = new Button { Content = "Change", Style = (Style)FindResource("Lumina.Btn.Secondary"), MinHeight = 32, Margin = new Thickness(8, 0, 0, 0) };
                System.Windows.Automation.AutomationProperties.SetName(change, "Change key for " + a.Label);
                var reset = new Button { Content = "Reset", Style = (Style)FindResource("Lumina.Btn.Link"), Margin = new Thickness(6, 0, 0, 0), Visibility = ov.ContainsKey(a.Id) ? Visibility.Visible : Visibility.Hidden };
                System.Windows.Automation.AutomationProperties.SetName(reset, "Reset key for " + a.Label);
                var chip = new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("Lumina.Brush.SurfaceHover"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 3), MinWidth = 90,
                    Child = new TextBlock { Text = string.Join(", ", Shortcuts.KeysFor(a, ov).Select(Shortcuts.Display)), Style = (Style)FindResource("Lumina.Text.Label"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center },
                };
                DockPanel.SetDock(reset, Dock.Right); DockPanel.SetDock(change, Dock.Right); DockPanel.SetDock(chip, Dock.Right);
                row.Children.Add(reset); row.Children.Add(change); row.Children.Add(chip);
                row.Children.Add(new TextBlock { Text = a.Label, Style = (Style)FindResource("Lumina.Text"), VerticalAlignment = VerticalAlignment.Center });
                var id = a.Id;
                change.Click += (_, _) =>
                {
                    var key = ConfirmDialog.CaptureKey(this, a.Label);
                    if (key == null) return;
                    var before = KeyOverrides();
                    var holder = Shortcuts.UsersOf(key, before, except: id).FirstOrDefault();
                    if (holder != null && !ConfirmDialog.Ask(this, $"{Shortcuts.Display(key)} is already used", $"{holder.Label} uses that key. Swap, so {holder.Label} gets {Shortcuts.Display(Shortcuts.KeysFor(Shortcuts.Find(id)!, before)[0])}?", "Swap")) return;
                    SaveKeyOverrides(before, Shortcuts.Assign(before, id, key, out _));
                    Build();
                };
                reset.Click += (_, _) => { var before = KeyOverrides(); SaveKeyOverrides(before, Shortcuts.Reset(before, id)); Build(); };
                panel.Children.Add(row);
            }
        }
        Build();
        ConfirmDialog.ShowContent(this, "Customise keys", new ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 600);
    }

    void ShowAbout()
    {
        string player = "mpv";
        try
        {
            var dll = System.IO.Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll");
            if (File.Exists(dll)) player = "mpv " + (System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "");
        }
        catch (IOException) { }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var i = 0;
        foreach (var (label, value) in AppInfo.Describe(player.Trim()))
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Style = (Style)FindResource("Lumina.Text.Muted"), Margin = new Thickness(0, 4, 12, 4) };
            var v = new TextBlock { Text = value, Style = (Style)FindResource("Lumina.Text"), Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(l, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            g.Children.Add(l); g.Children.Add(v);
            i++;
        }
        ConfirmDialog.ShowContent(this, "About Lumina", g, 640);
    }

    void ShowShortcuts()
    {
        var custom = Shortcuts.ParseOverrides(Config.Raw("shortcuts"));
        var rows = Shortcuts.Actions.Select(a => (Key: string.Join(", ", Shortcuts.KeysFor(a, custom).Select(Shortcuts.Display)), Does: a.Label))
            .Concat(new[] { (Key: "Mouse wheel", Does: "Volume or seek (Settings)"), (Key: "Double-click", Does: "Fullscreen (Settings)"), (Key: "Media keys", Does: "Play, pause, next, previous"), (Key: "Ctrl+U", Does: "Open a network stream") }).ToArray();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var half = (rows.Length + 1) / 2;
        for (var r = 0; r < half; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < rows.Length; i++)
        {
            var line = new DockPanel { Margin = new Thickness(0, 5, 18, 5) };
            var chip = new Border
            {
                Background = (Brush)FindResource("Lumina.Brush.SurfaceHover"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 3, 9, 3), MinWidth = 40,
                Child = new TextBlock { Text = rows[i].Key, Style = (Style)FindResource("Lumina.Text.Label"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center },
            };
            DockPanel.SetDock(chip, Dock.Right);
            line.Children.Add(chip);
            line.Children.Add(new TextBlock { Text = rows[i].Does, Style = (Style)FindResource("Lumina.Text.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetRow(line, i % half);
            Grid.SetColumn(line, i / half);
            g.Children.Add(line);
        }
        ConfirmDialog.ShowContent(this, "Keyboard shortcuts", g, 760);
    }

    void PlaybackFailed(Mpv m, string path, int error)
    {
        if (_mpv != m) return;                                    // an old player's last words
        Lumina.Diagnostics.Log.Info($"Playback failed (mpv error {error}) for {(path.Contains("://") ? new Uri(path).Host : path)}");
        var name = path.Contains("://") ? new Uri(path).Host : System.IO.Path.GetFileName(path);
        Stop();
        ConfirmDialog.Tell(this, "Could not play " + name, Mpv.FailureText(error));
    }

    void SendFullscreenState()
    {
        try { _mpv?.Command("script-message", "lumina-fullscreen-state", _fs.On ? "yes" : "no"); }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { }
    }

    // Remember the volume the user left it at, when the setting says to.
    void SaveVolume()
    {
        if (_mpv == null || AppSettings.Get("audio.startVolume") != "remember") return;
        try { if (_mpv.Get("volume") is { Length: > 0 } v) AppSettings.Set("audio.lastVolume", v); }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or IOException) { }
    }

    void SaveWindow()
    {
        if (!AppSettings.Bool("interface.rememberWindow") || _fs.On) return;
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        try
        {
            Config.Save(new Dictionary<string, string>
            {
                ["window"] = FormattableString.Invariant($"{b.X:0},{b.Y:0},{b.Width:0},{b.Height:0},{(WindowState == WindowState.Maximized ? 1 : 0)}"),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void RestoreWindow()
    {
        if (!AppSettings.Bool("interface.rememberWindow")) return;
        var p = Config.Raw("window").Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (p.Length != 5 || !p.Take(4).All(x => double.TryParse(x, System.Globalization.NumberStyles.Float, inv, out _))) return;
        double N(int i) => double.Parse(p[i], inv);
        var r = new Rect(N(0), N(1), Math.Max(N(2), MinWidth), Math.Max(N(3), MinHeight));
        // Ignore a saved spot that is now off every screen (monitor unplugged).
        if (!r.IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = r.X; Top = r.Y; Width = r.Width; Height = r.Height;
        if (p[4] == "1") WindowState = WindowState.Maximized;
    }

    void SendKey(string name)
    {
        try { _mpv?.Command("keypress", name); }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { } // player closing
    }

    /// <summary>
    /// While a video plays the title bar (minimize, maximize, close) gets its own strip above the picture: the video window would cover it,
    /// and would also swallow clicks meant for the player's Back button. Out of fullscreen and not playing the pages run up to the top edge.
    /// </summary>
    void UpdateTitleRow()
    {
        bool strip = Stage.IsVisible && Bar.IsVisible;
        Bar.HorizontalAlignment = Stage.IsVisible ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Bar.Width = Stage.IsVisible ? double.NaN : 170;
        DragStrip.Visibility = Stage.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        TitleRow.Height = new GridLength(strip ? 32 : 0);
        if (strip) Bar.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Lumina.Brush.Bg");
        else Bar.Background = System.Windows.Media.Brushes.Transparent;
    }

    // ---- touch on the video ----
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct TouchPoint { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd, ref TouchPoint p);
    bool _touchDown;

    /// <summary>
    /// A finger on the video comes to the main window only as the mouse messages Windows makes from it, and WPF discards those. Pass them to the player instead,
    /// so a tap presses the controls and a drag scrubs the seek bar and the volume.
    /// </summary>
    IntPtr TouchHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is not (0x200 or 0x201 or 0x202) || _mpv == null || Stage.Visibility != Visibility.Visible) return IntPtr.Zero;
        if (((long)GetMessageExtraInfo() & 0xFFFFFF00) != 0xFF515700) return IntPtr.Zero;      // the mark Windows puts on mouse messages made from touch or a pen
        var pt = new TouchPoint { X = (short)((long)lParam & 0xFFFF), Y = (short)(((long)lParam >> 16) & 0xFFFF) };
        ClientToScreen(hwnd, ref pt);
        var origin = Stage.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        int x = pt.X - (int)origin.X, y = pt.Y - (int)origin.Y;
        bool inside = x >= 0 && y >= 0 && x < Stage.ActualWidth * dpi.DpiScaleX && y < Stage.ActualHeight * dpi.DpiScaleY;
        if (msg == 0x201) { if (!inside) return IntPtr.Zero; _touchDown = true; _mouse.Touch("down", x, y); }
        else if (msg == 0x200) { if (_touchDown || inside) _mouse.Touch("move", x, y); }
        else if (_touchDown) { _touchDown = false; _mouse.Touch("up", x, y); }
        return IntPtr.Zero;
    }

    /// <summary>
    /// The frosted-glass sidebar needs the window itself to blur what is behind it (Acrylic) and a clear window background. Off while a video plays:
    /// the picture hides it anyway, and the blur would only cost graphics time.
    /// </summary>
    void ApplyTranslucency()
    {
        bool on = AppSettings.Bool("interface.translucency") && Stage.Visibility != Visibility.Visible;
        // (not the window's WindowBackdropType property: changing it while running makes WPF-UI rebuild the window chrome and it throws)
        if (on) { Background = System.Windows.Media.Brushes.Transparent; Wpf.Ui.Controls.WindowBackdrop.ApplyBackdrop(this, Wpf.Ui.Controls.WindowBackdropType.Acrylic); }
        else { Wpf.Ui.Controls.WindowBackdrop.RemoveBackdrop(this); SetResourceReference(BackgroundProperty, "Lumina.Brush.Bg"); }
    }

    void OnDragStrip(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && ResizeMode != ResizeMode.NoResize) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    /// <summary>Tear down the player without touching the page the user sees.</summary>
    void StopPlayback()
    {
        if (_fs.On) _fs.Exit(); // the library UI has to be reachable again
        Stage.Cursor = null;
        _session?.Stop();
        _session = null;
        SaveVolume();
        StopThumbnails();
        StopMirror();
        FinishRecording();
        LeaveMiniplayer();
        _mpv?.Dispose();
        _mpv = null;
        Stage.Child = null;
        Stage.Visibility = Visibility.Collapsed;
        Bar.Title = "";
    }

    void Stop()
    {
        _queue.Clear(); // the user left the player: the queue was for this viewing
        StopPlayback();
        Refresh();
        // Back to the page playback started from, with fresh progress and watched marks.
        if (_detailsKey != null) ShowDetails(_detailsKey);
        else ShowPage(_pageBeforePlay, animate: false);
    }
}
