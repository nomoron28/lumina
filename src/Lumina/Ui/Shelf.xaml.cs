using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lumina;

/// <summary>A titled horizontal row of cards: Continue watching, Recently added, and so on.</summary>
public partial class Shelf : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Shelf), new PropertyMetadata(""));

    public static readonly DependencyProperty ShowSeeAllProperty =
        DependencyProperty.Register(nameof(ShowSeeAll), typeof(bool), typeof(Shelf), new PropertyMetadata(true, (d, e) =>
            ((Shelf)d).SeeAllBtn.Visibility = e.NewValue is true ? Visibility.Visible : Visibility.Collapsed));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool ShowSeeAll { get => (bool)GetValue(ShowSeeAllProperty); set => SetValue(ShowSeeAllProperty, value); }

    public DataTemplate? CardTemplate { get => List.ItemTemplate; set => List.ItemTemplate = value; }

    /// <summary>Raised with the data item the user opened.</summary>
    public event Action<object>? ItemInvoked;
    public event Action? SeeAllClicked;

    public Shelf() => InitializeComponent();

    /// <summary>Sets the items; an empty list collapses the whole shelf so Home never shows a heading over nothing.</summary>
    public void SetItems(IEnumerable items)
    {
        var list = items.Cast<object>().ToList();
        List.ItemsSource = list;
        Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void OnSeeAll(object sender, RoutedEventArgs e) => SeeAllClicked?.Invoke();

    void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(List, d) is ListBoxItem { DataContext: { } item })
            ItemInvoked?.Invoke(item);
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && List.SelectedItem is { } item) { ItemInvoked?.Invoke(item); e.Handled = true; }
    }

    // One screenful minus a card, so the last card you saw stays visible as an anchor.
    void Page(int direction)
    {
        if (ScrollForward.FindScroller(List) is not { } sv) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset + direction * Math.Max(200, sv.ViewportWidth - 180));
    }

    void OnLeft(object sender, RoutedEventArgs e) => Page(-1);
    void OnRight(object sender, RoutedEventArgs e) => Page(1);
}
