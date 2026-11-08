namespace Lumina.Sync;

public record LocalState(bool Watched, double Position, long PlayedAt, double? Duration, long? UnwatchedAt = null);

/// <summary>What Trakt knows about one item: watched history and/or a paused playback position.</summary>
public record RemoteState(bool Watched, long? WatchedAt, double? ProgressPct, long? PausedAt);

public record MergeResult(bool Watched, double Position, long PlayedAt, bool Changed, bool Push);

/// <summary>
/// The two-way rule, kept pure so it can be tested:
/// watched is sticky and never cleared by a pull; otherwise the newest play wins for position;
/// anything watched locally that Trakt lacks is pushed.
/// </summary>
public static class TraktMerge
{
    public static MergeResult Merge(LocalState l, RemoteState? r)
    {
        r ??= new RemoteState(false, null, null, null);

        // The user marked it unwatched here after Trakt recorded the play: that decision outranks the old history entry.
        var remoteWatched = r.Watched && !(l.UnwatchedAt is { } uw && (r.WatchedAt ?? 0) <= uw);
        r = r with { Watched = remoteWatched };

        var watched = l.Watched || r.Watched;
        var playedAt = l.PlayedAt;
        var pos = l.Position;

        if (r.Watched && r.WatchedAt is { } wa) playedAt = Math.Max(playedAt, wa);

        // Trakt only stores a percentage, so it needs a known local duration to become seconds.
        if (!watched && r.ProgressPct is { } pct && r.PausedAt is { } pa && pa > l.PlayedAt && l.Duration is > 0)
        {
            pos = pct / 100 * l.Duration.Value;
            playedAt = pa;
        }

        var changed = watched != l.Watched || pos != l.Position || playedAt != l.PlayedAt;
        return new MergeResult(watched, pos, playedAt, changed, Push: l.Watched && !r.Watched);
    }
}
