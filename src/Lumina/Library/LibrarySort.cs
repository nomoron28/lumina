using Lumina.Settings;

namespace Lumina.Library;

public enum SortKey { Title, Added, Watched, Year, Rating }

public static class LibrarySort
{
    public static readonly Choice[] Choices =
    {
        new("title", "A to Z"), new("added", "Recently added"), new("watched", "Recently watched"),
        new("year", "Year, newest first"), new("rating", "Rating, highest first"),
    };

    public static SortKey Parse(string? value) => value switch
    {
        "added" => SortKey.Added, "watched" => SortKey.Watched, "year" => SortKey.Year, "rating" => SortKey.Rating, _ => SortKey.Title,
    };

    public static string Name(SortKey k) => k.ToString().ToLowerInvariant();

    /// <summary>
    /// Order entries. Every ordering falls back to the title, so equal items never shuffle between redraws.
    /// Entries missing the sort value (no year, never watched, no rating) go last.
    /// </summary>
    public static List<LibraryEntry> Apply(IEnumerable<LibraryEntry> entries, SortKey key)
    {
        var byTitle = StringComparer.CurrentCultureIgnoreCase;
        return key switch
        {
            SortKey.Added => entries.OrderByDescending(e => e.AddedAt).ThenBy(e => e.Title, byTitle).ToList(),
            SortKey.Watched => entries.OrderByDescending(e => e.PlayedAt > 0).ThenByDescending(e => e.PlayedAt).ThenBy(e => e.Title, byTitle).ToList(),
            SortKey.Year => entries.OrderByDescending(e => e.Year.HasValue).ThenByDescending(e => e.Year ?? 0).ThenBy(e => e.Title, byTitle).ToList(),
            SortKey.Rating => entries.OrderByDescending(e => e.Rating.HasValue).ThenByDescending(e => e.Rating ?? 0).ThenBy(e => e.Title, byTitle).ToList(),
            _ => entries.OrderBy(e => e.Title, byTitle).ToList(),
        };
    }
}
