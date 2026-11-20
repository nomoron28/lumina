namespace Lumina.Library;

/// <summary>A collection the user made, such as "Terminator" (issue #16). Refs say which titles are in it.</summary>
public sealed record Collection(long Id, string Name, List<string> Refs);

/// <summary>
/// How a collection holds on to a title: its TMDB id when it has one ("movie:tmdb:218"), otherwise a film's file path or a
/// show's name. A title still counts as a member after it gets matched later, because every way of naming it is checked.
/// </summary>
public static class Collections
{
    public static string Ref(LibraryEntry e) => Refs(e)[0];

    /// <summary>Every way a collection may have saved this title, the best first.</summary>
    public static List<string> Refs(LibraryEntry e)
    {
        var refs = new List<string>();
        if (e.TmdbId > 0) refs.Add($"{e.Kind}:tmdb:{e.TmdbId}");
        if (e.IsShow) refs.AddRange(e.Items.Select(i => "show:title:" + i.FileTitle.ToLowerInvariant()).Distinct());
        else refs.AddRange(e.Items.Select(i => "movie:path:" + i.Path));
        return refs;
    }

    public static bool Contains(Collection c, LibraryEntry e)
    {
        var held = new HashSet<string>(c.Refs, StringComparer.OrdinalIgnoreCase);
        return Refs(e).Any(held.Contains);
    }

    /// <summary>The titles in a collection, in release order, so a box set reads 1, 2, 3.</summary>
    public static List<LibraryEntry> Members(IEnumerable<LibraryEntry> entries, Collection c) =>
        entries.Where(e => Contains(c, e)).OrderBy(e => e.Year ?? int.MaxValue).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Home's Collections row: one tile per collection with something in it, its colours from its name.</summary>
    public static List<GenreTile> Tiles(IEnumerable<LibraryEntry> entries, IEnumerable<Collection> collections)
    {
        var all = entries.ToList();
        var tiles = new List<GenreTile>();
        foreach (var c in collections)
        {
            var members = Members(all, c);
            if (members.Count == 0) continue;
            var (from, to) = HomeShelves.Colours(c.Name);
            var back = members.Where(e => e.Backdrop != null).OrderByDescending(e => e.Rating ?? 0).FirstOrDefault()?.Backdrop;
            tiles.Add(new GenreTile("collection", c.Name, members.Count, back, from, to) { CollectionId = c.Id });
        }
        return tiles;
    }
}
