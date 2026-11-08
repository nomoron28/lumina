namespace Lumina.Library;

/// <summary>One card in the library: a movie, or a whole show with its episodes.</summary>
public class LibraryEntry
{
    public string Key { get; init; } = "";
    public string Kind { get; init; } = "movie";
    public string Title { get; init; } = "";
    /// <summary>The TMDB id when the title was matched, otherwise 0.</summary>
    public long TmdbId { get; init; }
    public string? Poster { get; init; }
    public string? Backdrop { get; init; }
    public string? Overview { get; init; }
    public double? Rating { get; init; }
    public int? Year { get; init; }
    /// <summary>Episodes ordered by season then episode, or the single movie file.</summary>
    public List<MediaItem> Items { get; init; } = new();

    public bool IsShow => Kind == "show";

    /// <summary>Genre names, from the title's TMDB details.</summary>
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    /// <summary>Nothing of it has been started or watched: the card gets the accent corner.</summary>
    public bool IsFresh => Items.Count > 0 && Items.All(i => i.IsFresh);

    /// <summary>TMDB rating for the list view, empty when there is none.</summary>
    public string RatingText => Rating is { } r and > 0 ? $"TMDB {r:0.0}" : "";

    public long AddedAt => Items.Count == 0 ? 0 : Items.Max(i => i.AddedAt);
    public long PlayedAt => Items.Count == 0 ? 0 : Items.Max(i => i.PlayedAt);
    public bool AllWatched => Items.Count > 0 && Items.All(i => i.Watched);
    public bool AnyUnwatched => Items.Any(i => !i.Watched);
    public bool InProgress => Items.Any(i => i.CanResume);

    /// <summary>
    /// What "Continue watching" should open, or null if this entry does not belong there.
    /// A movie qualifies while partway through. A show qualifies once you have started it and episodes remain.
    /// </summary>
    public MediaItem? ContinueItem
    {
        get
        {
            if (Items.Count == 0) return null;
            if (!IsShow) return Items[0].CanResume ? Items[0] : null;
            var started = Items.Any(i => i.Watched || i.CanResume);
            if (!started) return null;
            var next = Next;
            return next.Watched ? null : next;
        }
    }

    /// <summary>Wide artwork for shelf cards: the backdrop if there is one, otherwise the poster.</summary>
    public string? HeroImage => Backdrop ?? Poster;

    public string HeroSub
    {
        get
        {
            var c = ContinueItem ?? (Items.Count > 0 ? Next : null);
            if (c == null) return "";
            return IsShow ? c.Sub : Year?.ToString() ?? "";
        }
    }

    public double HeroPercent => ContinueItem?.Percent ?? 0;

    /// <summary>Short facts for the details page, one chip each.</summary>
    public List<string> Chips => new[]
    {
        IsShow ? "TV show" : "Movie",
        Year?.ToString(),
        Rating is > 0 ? $"TMDB {Rating:0.0}" : null,
        IsShow ? Sub : null,
    }.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();

    public static List<LibraryEntry> ContinueWatching(IEnumerable<LibraryEntry> all, int take = 12) =>
        all.Where(e => e.ContinueItem != null).OrderByDescending(e => e.PlayedAt).Take(take).ToList();

    public static List<LibraryEntry> RecentlyAdded(IEnumerable<LibraryEntry> all, int take = 20) =>
        all.OrderByDescending(e => e.AddedAt).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).Take(take).ToList();

    public static List<LibraryEntry> Unwatched(IEnumerable<LibraryEntry> all, int take = int.MaxValue) =>
        all.Where(e => e.AnyUnwatched).Take(take).ToList();
    public int Seasons => Items.Select(i => i.Season).Distinct().Count();

    public string Sub => IsShow
        ? $"{Seasons} season{(Seasons == 1 ? "" : "s")} · {Items.Count} episode{(Items.Count == 1 ? "" : "s")}"
        : Year?.ToString() ?? "";

    /// <summary>Movie: how far through. Show: share of episodes watched.</summary>
    public double Percent => Items.Count == 0 ? 0 : IsShow ? Items.Count(i => i.Watched) * 100.0 / Items.Count : Items[0].Percent;

    /// <summary>The line under the title on the details page.</summary>
    public string Meta => string.Join("  ·  ", new[]
    {
        IsShow ? "TV show" : "Movie",
        Year?.ToString(),
        Rating is > 0 ? $"TMDB {Rating:0.0}" : null,
        IsShow ? Sub : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    /// <summary>
    /// What Play should start. A show continues the episode you are partway through; otherwise
    /// the first unwatched episode after the last one you finished; otherwise the first unwatched; otherwise episode one.
    /// </summary>
    public MediaItem Next
    {
        get
        {
            if (!IsShow) return Items[0];
            var inProgress = Items.FirstOrDefault(i => i.CanResume);
            if (inProgress != null) return inProgress;
            var lastWatched = Items.FindLastIndex(i => i.Watched);
            return Items.Skip(lastWatched + 1).FirstOrDefault(i => !i.Watched)
                ?? Items.FirstOrDefault(i => !i.Watched)
                ?? Items[0];
        }
    }

    public string PlayLabel
    {
        get
        {
            var n = Next;
            if (!IsShow) return n.PlayText;
            var code = $"S{n.Season:00}E{n.Episode:00}";
            return n.CanResume ? $"Resume {code} ({MediaItem.Clock(n.Position)})" : $"Play {code}";
        }
    }

    public static List<LibraryEntry> Build(IEnumerable<MediaItem> items)
    {
        var entries = new List<LibraryEntry>();
        foreach (var g in items.Where(i => i.Kind == "movie").Select(i => new[] { i }).Concat(
                     items.Where(i => i.Kind == "show").GroupBy(i => i.TmdbId > 0 ? "t" + i.TmdbId : "n" + i.Title.ToLowerInvariant()).Select(g => g.ToArray())))
        {
            var first = g[0];
            var ordered = g.OrderBy(i => i.Season ?? 0).ThenBy(i => i.Episode ?? 0).ToList();
            entries.Add(new LibraryEntry
            {
                Key = first.Kind == "movie" ? "movie:" + first.Id : "show:" + (first.TmdbId > 0 ? first.TmdbId.ToString() : first.Title.ToLowerInvariant()),
                Kind = first.Kind,
                Title = first.Title,
                TmdbId = first.TmdbId,
                Poster = g.Select(i => i.Poster).FirstOrDefault(p => p != null),
                Backdrop = g.Select(i => i.Backdrop).FirstOrDefault(p => p != null),
                Overview = g.Select(i => i.Overview).FirstOrDefault(p => p != null),
                Rating = g.Select(i => i.Rating).FirstOrDefault(p => p != null),
                Year = first.Kind == "movie" ? first.Year ?? first.MetaYear : first.MetaYear,
                Genres = g.Select(i => i.Genres).FirstOrDefault(x => x.Count > 0) ?? Array.Empty<string>(),
                Items = ordered,
            });
        }
        return entries.OrderBy(e => e.Kind, StringComparer.Ordinal).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
