namespace Lumina.Library;

/// <summary>A banner on Home for one genre: its name, how many titles, a backdrop to tint, and a gradient from the genre's name.</summary>
public record GenreTile(string Kind, string Name, int Count, string? Backdrop, string From, string To)
{
    /// <summary>The card's accessible name and its label.</summary>
    public string Title => Name;
    public string CountText => Count == 1 ? "1 title" : $"{Count} titles";

    /// <summary>The tint laid over the backdrop: the genre's two colours, mostly opaque so white text always reads.</summary>
    public System.Windows.Media.Brush Overlay
    {
        get
        {
            static System.Windows.Media.Color C(string hex) =>
                System.Windows.Media.Color.FromArgb(0xCC, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
            var b = new System.Windows.Media.LinearGradientBrush(C(From), C(To), new System.Windows.Point(0, 0.2), new System.Windows.Point(1, 0.8));
            b.Freeze();
            return b;
        }
    }
}

/// <summary>The rows Home adds on top of the basic shelves: genres, top rated, favourites. Pure, so ordering and grouping are unit tested.</summary>
public static class HomeShelves
{
    // Gradient pairs (start, end) that keep white text readable (each end is dark enough, checked by a test).
    static readonly (string From, string To)[] Palette =
    {
        ("3F37C9", "E85D2C"),   // violet to orange
        ("1F3A5F", "4B7BA7"),   // navy to steel
        ("1B7F8C", "3C9D2F"),   // teal to green
        ("8E2C7A", "E0476B"),   // plum to rose
        ("1E5BB8", "2290B8"),   // blue to cyan
        ("A8431C", "B8771A"),   // rust to amber
        ("3A3F8F", "9C4DB5"),   // indigo to orchid
        ("2D6A4F", "4F9440"),   // forest to leaf
    };

    /// <summary>Same name, same colours, every time and on every machine.</summary>
    public static (string From, string To) Colours(string genre)
    {
        var h = 17;
        foreach (var c in genre.ToLowerInvariant()) h = h * 31 + c;
        return Palette[(h & 0x7FFFFFFF) % Palette.Length];
    }

    /// <summary>Genres of the given kind, most titles first, then by name. The backdrop comes from the best rated title that has one.</summary>
    public static List<GenreTile> Genres(IEnumerable<LibraryEntry> entries, string kind)
    {
        var of = entries.Where(e => e.Kind == kind).ToList();
        return of.SelectMany(e => e.Genres).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var with = of.Where(e => e.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)).ToList();
                var (from, to) = Colours(g);
                var back = with.Where(e => e.Backdrop != null).OrderByDescending(e => e.Rating ?? 0).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault()?.Backdrop;
                return new GenreTile(kind, g, with.Count, back, from, to);
            })
            .OrderByDescending(t => t.Count).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<LibraryEntry> InGenre(IEnumerable<LibraryEntry> entries, string kind, string genre) =>
        entries.Where(e => e.Kind == kind && e.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Best rated first; titles without a score are left out. Ties by title so the order never jumps.</summary>
    public static List<LibraryEntry> TopRated(IEnumerable<LibraryEntry> entries, string kind, int take = 20) =>
        entries.Where(e => e.Kind == kind && e.Rating is > 0).OrderByDescending(e => e.Rating).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).Take(take).ToList();

    /// <summary>The entries the user starred, newest star first. A favourite whose title left the library is simply not listed.</summary>
    public static List<LibraryEntry> Favourites(IEnumerable<LibraryEntry> entries, IReadOnlyList<(string Kind, long Id)> favourites)
    {
        var list = entries.ToList();
        var result = new List<LibraryEntry>();
        foreach (var (kind, id) in favourites)
            if (list.FirstOrDefault(e => e.Kind == kind && e.TmdbId == id && id > 0) is { } hit && !result.Contains(hit)) result.Add(hit);
        return result;
    }
}
