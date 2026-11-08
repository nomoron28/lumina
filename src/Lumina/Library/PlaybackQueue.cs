using System.IO;

namespace Lumina.Library;

public enum RepeatMode { Off, All, One }

public sealed record QueueItem(string Path, string Title, long MediaId = 0);

/// <summary>
/// The list of things to play after one another. Pure logic: no mpv, no UI, so every edge is unit-tested.
/// Items keep their listed order; Shuffle only changes the order they are visited in.
/// </summary>
public sealed class PlaybackQueue
{
    readonly List<QueueItem> _items = new();
    List<int> _order = new();   // visiting order: indexes into _items
    int _pos = -1;               // position inside _order
    readonly Random _rng;

    public PlaybackQueue(Random? rng = null) => _rng = rng ?? new Random();

    public IReadOnlyList<QueueItem> Items => _items;
    public int Count => _items.Count;
    public bool Shuffle { get; private set; }
    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    /// <summary>Index in <see cref="Items"/> of what is playing, or -1.</summary>
    public int Current => _pos >= 0 && _pos < _order.Count ? _order[_pos] : -1;
    public QueueItem? CurrentItem => Current >= 0 ? _items[Current] : null;

    public bool HasNext => Count > 0 && (Repeat != RepeatMode.Off || _pos + 1 < _order.Count);
    public bool HasPrevious => Count > 0 && (Repeat != RepeatMode.Off || _pos > 0);

    /// <summary>Replace everything and start at <paramref name="start"/> (an index into <paramref name="items"/>).</summary>
    public void Load(IEnumerable<QueueItem> items, int start = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        Rebuild(_items.Count == 0 ? -1 : Math.Clamp(start, 0, _items.Count - 1));
    }

    public void Clear() => Load(Array.Empty<QueueItem>());

    public void Add(QueueItem item)
    {
        var cur = Current;
        _items.Add(item);
        // Appending never moves the playing item. Shuffled queues put the newcomer at a random later slot.
        _order.Insert(Shuffle && _pos >= 0 ? _rng.Next(_pos + 1, _order.Count + 1) : _order.Count, _items.Count - 1);
        _pos = cur < 0 ? 0 : _order.IndexOf(cur);
    }

    /// <summary>Put an item right after the current one in visiting order ("play next").</summary>
    public void PlayNext(QueueItem item)
    {
        if (_pos < 0) { Add(item); return; }
        var cur = Current;
        _items.Add(item);
        _order.Insert(_pos + 1, _items.Count - 1);
        _pos = _order.IndexOf(cur);
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var cur = Current;
        var wasCurrent = index == cur;
        var orderPos = _order.IndexOf(index);
        _items.RemoveAt(index);
        _order.RemoveAt(orderPos);
        for (var i = 0; i < _order.Count; i++) if (_order[i] > index) _order[i]--;
        if (_items.Count == 0) { _pos = -1; return; }
        if (wasCurrent) _pos = Math.Min(orderPos, _order.Count - 1); // whatever slid into its place
        else _pos = _order.IndexOf(cur > index ? cur - 1 : cur);
    }

    /// <summary>Move an item in the listed order; the playing item stays the playing item.</summary>
    public void Move(int from, int to)
    {
        if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count || from == to) return;
        var cur = Current;
        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        int Map(int i) => i == from ? to : from < to ? (i > from && i <= to ? i - 1 : i) : (i >= to && i < from ? i + 1 : i);
        for (var i = 0; i < _order.Count; i++) _order[i] = Map(_order[i]);
        _pos = cur < 0 ? -1 : _order.IndexOf(Map(cur));
    }

    public void SetShuffle(bool on)
    {
        if (on == Shuffle) return;
        Shuffle = on;
        Rebuild(Current);
    }

    /// <summary>Jump straight to an item (the user clicked it).</summary>
    public QueueItem? JumpTo(int index)
    {
        if (index < 0 || index >= _items.Count) return null;
        _pos = _order.IndexOf(index);
        return CurrentItem;
    }

    /// <summary>The item to play after the current one finished by itself. Repeat One replays; Off ends at the last item.</summary>
    public QueueItem? Advance()
    {
        if (Count == 0) return null;
        if (Repeat == RepeatMode.One) return CurrentItem;
        return Step(+1);
    }

    /// <summary>The user pressed Next: always moves on, even under Repeat One.</summary>
    public QueueItem? Next() => Count == 0 ? null : Step(+1);

    public QueueItem? Previous() => Count == 0 ? null : Step(-1);

    QueueItem? Step(int dir)
    {
        var p = _pos + dir;
        if (p >= _order.Count || p < 0)
        {
            if (Repeat == RepeatMode.Off) return null;
            if (dir > 0 && Shuffle)
            {
                var last = Current;
                Rebuild(-1); // a fresh shuffle for the next lap...
                if (_order.Count > 1 && _order[0] == last) (_order[0], _order[1]) = (_order[1], _order[0]); // ...that does not open with what just played
            }
            p = dir > 0 ? 0 : _order.Count - 1;
        }
        _pos = p;
        return CurrentItem;
    }

    void Rebuild(int keepCurrent)
    {
        _order = Enumerable.Range(0, _items.Count).ToList();
        if (Shuffle)
        {
            // Fisher-Yates over everything except the playing item, which stays first so shuffling never skips away from it.
            var rest = _order.Where(i => i != keepCurrent).ToList();
            for (var i = rest.Count - 1; i > 0; i--) { var j = _rng.Next(i + 1); (rest[i], rest[j]) = (rest[j], rest[i]); }
            _order = keepCurrent >= 0 ? new List<int> { keepCurrent }.Concat(rest).ToList() : rest;
            _pos = _order.Count == 0 ? -1 : keepCurrent >= 0 ? 0 : -1;
        }
        else _pos = keepCurrent;
    }

    // ---- building a queue from files and folders ----

    static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".m4v", ".mov", ".avi", ".ts", ".m2ts", ".mts", ".webm", ".wmv", ".flv", ".iso" };

    public static bool IsVideo(string path) => Video.Contains(System.IO.Path.GetExtension(path));

    /// <summary>"Ep2" sorts before "Ep10", unlike a plain string compare.</summary>
    public static readonly IComparer<string> Natural = Comparer<string>.Create(NaturalCompare);

    static int NaturalCompare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>
    /// Turn dropped or chosen paths into queue items: files as given, folders expanded (recursively) to their videos in natural order.
    /// Anything that is not a video or a folder is ignored.
    /// </summary>
    public static List<QueueItem> FromPaths(IEnumerable<string> paths)
    {
        var result = new List<QueueItem>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                var files = Directory.EnumerateFiles(p, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(IsVideo).OrderBy(f => f, Natural);
                result.AddRange(files.Select(f => new QueueItem(f, System.IO.Path.GetFileNameWithoutExtension(f))));
            }
            else if (File.Exists(p) && IsVideo(p)) result.Add(new QueueItem(p, System.IO.Path.GetFileNameWithoutExtension(p)));
        }
        return result;
    }
}
