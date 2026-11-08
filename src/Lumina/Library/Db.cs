using System.IO;
using Lumina.Metadata;
using Microsoft.Data.Sqlite;

namespace Lumina.Library;

public record Found(string Path, long Size, long Mtime, Parsed P);

/// <summary>A TMDB match for a movie or show. Kind is movie or show.</summary>
public record Meta(string Kind, long TmdbId, string Title, string? Overview, string? PosterPath, string? BackdropPath, double Rating, int? Year);

public record Ep(int Number, string Name, string? Overview, string? StillPath, string? AirDate = null, int? Runtime = null, double? Rating = null);

/// <summary>One library file that has a TMDB id, with its local watch state, for Trakt sync.</summary>
public record LocalRow(long Id, string Kind, long TmdbId, int? Season, int? Episode, double Position, bool Watched, long PlayedAt, double? Duration, long? UnwatchedAt = null);

public class MediaItem
{
    public long Id { get; init; }
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "movie";
    public string Title { get; init; } = "";
    public int? Year { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public double Position { get; init; }
    public double? Duration { get; init; }
    public bool Watched { get; init; }
    public string? Poster { get; init; }
    public string? Overview { get; init; }
    public double? Rating { get; init; }
    public string? EpisodeName { get; init; }
    public long TmdbId { get; init; }
    public string? Backdrop { get; init; }
    public string? EpisodeOverview { get; init; }
    /// <summary>TMDB episode picture on disk, air date (yyyy-MM-dd), runtime in minutes and score, when known.</summary>
    public string? Still { get; init; }
    public string? AirDate { get; init; }
    public int? EpRuntime { get; init; }
    public double? EpRating { get; init; }
    /// <summary>Genre names of the title from TMDB (separated by | in the database).</summary>
    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();
    /// <summary>File size in bytes.</summary>
    public long Size { get; init; }
    public int? MetaYear { get; init; }
    public long AddedAt { get; init; }
    public long PlayedAt { get; init; }
    /// <summary>The title and year as parsed from the file name, before TMDB renamed it. Rematching works from these.</summary>
    public string FileTitle { get; init; } = "";

    public override string ToString() => RowTitle;   // screen readers announce an episode row by this
    public int? FileYear { get; init; }
    public string EpisodeNumber => Episode?.ToString() ?? "";
    public string EpisodeTitle => EpisodeName ?? System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The episode that follows this one in the same show, or null at the end of the list or for movies.</summary>
    public static MediaItem? NextEpisode(IEnumerable<MediaItem> all, MediaItem cur) => Neighbour(all, cur, 1);

    /// <summary>The episode before this one, or null at the start of the list or for movies.</summary>
    public static MediaItem? PreviousEpisode(IEnumerable<MediaItem> all, MediaItem cur) => Neighbour(all, cur, -1);

    static MediaItem? Neighbour(IEnumerable<MediaItem> all, MediaItem cur, int dir)
    {
        if (cur.Kind != "show") return null;
        bool Same(MediaItem m) => m.Kind == "show" && (cur.TmdbId > 0 ? m.TmdbId == cur.TmdbId : string.Equals(m.Title, cur.Title, StringComparison.OrdinalIgnoreCase));
        var later = all.Where(m => m.Id != cur.Id && Same(m))
            .Where(m => (m.Season ?? 0, m.Episode ?? 0).CompareTo((cur.Season ?? 0, cur.Episode ?? 0)) * dir > 0);
        return dir > 0
            ? later.OrderBy(m => m.Season ?? 0).ThenBy(m => m.Episode ?? 0).FirstOrDefault()
            : later.OrderByDescending(m => m.Season ?? 0).ThenByDescending(m => m.Episode ?? 0).FirstOrDefault();
    }

    public string SeasonLabel => Season is { } s ? (s == 0 ? "Specials" : $"Season {s}") : "Episodes";
    public string RowTitle => $"{Episode}. {EpisodeName ?? System.IO.Path.GetFileNameWithoutExtension(Path)}";
    public string WatchedMark => Watched ? "Watched" : "";
    public bool CanResume => !Watched && Position > 5;
    /// <summary>Never started and not watched: the card gets the accent corner.</summary>
    public bool IsFresh => !Watched && Position <= 5;
    public string PlayText => CanResume ? "Resume " + Clock(Position) : "Play";

    public static string Clock(double sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public string Sub => Kind == "show"
        ? $"S{Season:00}E{Episode:00}" + (EpisodeName is { Length: > 0 } n ? " · " + n : "")
        : Year?.ToString() ?? "";
    public double Percent => !Watched && Duration > 0 ? Math.Min(100, Position / Duration.Value * 100) : 0;
}

/// <summary>All SQLite access. One short-lived connection per call keeps it thread-safe.</summary>
public static class Db
{
    // LUMINA_DB lets tests point at a throwaway file.
    static string DbFile => Environment.GetEnvironmentVariable("LUMINA_DB") ?? System.IO.Path.Combine(
        Lumina.Windows.AppPaths.Data, "lumina.db");

    static SqliteConnection Open()
    {
        var c = new SqliteConnection("Data Source=" + DbFile);
        c.Open();
        return c;
    }

    static SqliteCommand Cmd(SqliteConnection c, string sql, params (string, object?)[] p)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd;
    }

    public static void Init()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DbFile)!);
        using var c = Open();
        Cmd(c, """
            CREATE TABLE IF NOT EXISTS roots(id INTEGER PRIMARY KEY, path TEXT UNIQUE NOT NULL);
            CREATE TABLE IF NOT EXISTS media(
              id INTEGER PRIMARY KEY, path TEXT UNIQUE NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL,
              year INTEGER, season INTEGER, episode INTEGER, tmdb_id INTEGER, duration_sec REAL,
              size INTEGER NOT NULL, mtime INTEGER NOT NULL, added_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS watch(
              media_id INTEGER PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
              position_sec REAL NOT NULL, watched INTEGER NOT NULL, played_at INTEGER NOT NULL, trakt_synced_at INTEGER);
            CREATE TABLE IF NOT EXISTS kv(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS meta(
              kind TEXT NOT NULL, tmdb_id INTEGER NOT NULL, title TEXT NOT NULL, overview TEXT,
              poster_path TEXT, backdrop_path TEXT, rating REAL, year INTEGER, PRIMARY KEY(kind, tmdb_id));
            CREATE TABLE IF NOT EXISTS ep(
              tmdb_id INTEGER NOT NULL, season INTEGER NOT NULL, episode INTEGER NOT NULL,
              name TEXT, overview TEXT, still_path TEXT, PRIMARY KEY(tmdb_id, season, episode));
            """).ExecuteNonQuery();
        Migrate(Migrations);
    }

    /// <summary>
    /// Schema changes after the v1 tables above. Each entry is one SQL script; entry i takes the database to version i+2.
    /// Only ever append. Never edit or reorder a released entry.
    /// </summary>
    public static readonly IReadOnlyList<string> Migrations = new List<string>
    {
        // v2: saved playlists
        """
        CREATE TABLE playlists(id INTEGER PRIMARY KEY, name TEXT UNIQUE NOT NULL COLLATE NOCASE, created_at INTEGER NOT NULL);
        CREATE TABLE playlist_items(
          playlist_id INTEGER NOT NULL REFERENCES playlists(id) ON DELETE CASCADE, pos INTEGER NOT NULL,
          path TEXT NOT NULL, title TEXT NOT NULL, PRIMARY KEY(playlist_id, pos));
        """,
        // v3: files the user hid from the library, and when something was marked unwatched (so a Trakt pull cannot undo it)
        """
        CREATE TABLE hidden(path TEXT PRIMARY KEY);
        ALTER TABLE watch ADD COLUMN unwatched_at INTEGER;
        """,
        // v4: runtime, certificate, genres, logo, IMDb id and scores on a title; air date, runtime and score on an episode; the cast and crew row; favourites
        """
        ALTER TABLE meta ADD COLUMN runtime INTEGER;
        ALTER TABLE meta ADD COLUMN certification TEXT;
        ALTER TABLE meta ADD COLUMN genres TEXT;
        ALTER TABLE meta ADD COLUMN tagline TEXT;
        ALTER TABLE meta ADD COLUMN logo_path TEXT;
        ALTER TABLE meta ADD COLUMN imdb_id TEXT;
        ALTER TABLE meta ADD COLUMN ratings TEXT;
        ALTER TABLE meta ADD COLUMN detail_at INTEGER;
        ALTER TABLE ep ADD COLUMN air_date TEXT;
        ALTER TABLE ep ADD COLUMN runtime INTEGER;
        ALTER TABLE ep ADD COLUMN rating REAL;
        CREATE TABLE credits(
          kind TEXT NOT NULL, tmdb_id INTEGER NOT NULL, pos INTEGER NOT NULL, person TEXT NOT NULL, role TEXT, photo_path TEXT,
          PRIMARY KEY(kind, tmdb_id, pos));
        CREATE TABLE favourite(kind TEXT NOT NULL, tmdb_id INTEGER NOT NULL, added_at INTEGER NOT NULL, PRIMARY KEY(kind, tmdb_id));
        """,
        // v5: bookmarks, named places in a file the user marked while watching
        """
        CREATE TABLE bookmarks(id INTEGER PRIMARY KEY, path TEXT NOT NULL, position_sec REAL NOT NULL, label TEXT NOT NULL, created_at INTEGER NOT NULL);
        CREATE INDEX bookmarks_path ON bookmarks(path);
        """,
        // v6: the user's own "this is a TV show / a movie" for a folder or a file, kept across rescans
        """
        CREATE TABLE kind_override(path TEXT PRIMARY KEY COLLATE NOCASE, kind TEXT NOT NULL);
        """,
    };

    public const string VersionKey = "schema_version";

    /// <summary>Current schema version (1 for a database made before versioning existed).</summary>
    public static int SchemaVersion() => int.TryParse(KvGet(VersionKey), out var v) ? v : 1;

    /// <summary>
    /// Apply pending steps, each in its own transaction, so a failing step leaves the previous version intact.
    /// A copy of the file is kept as lumina.db.bak-v{from} before the first step runs.
    /// </summary>
    public static void Migrate(IReadOnlyList<string> steps)
    {
        var from = SchemaVersion();
        if (from >= steps.Count + 1) return;
        var backup = DbFile + ".bak-v" + from;
        if (!File.Exists(backup))
        {
            SqliteConnection.ClearAllPools(); // release the file so the copy is complete
            File.Copy(DbFile, backup);
        }
        for (var v = from; v < steps.Count + 1; v++)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            var cmd = Cmd(c, steps[v - 1]);
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
            var set = Cmd(c, "INSERT OR REPLACE INTO kv(key,value) VALUES(@k,@v)", ("@k", VersionKey), ("@v", (v + 1).ToString()));
            set.Transaction = tx;
            set.ExecuteNonQuery();
            tx.Commit();
        }
    }

    public static List<string> Roots()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT path FROM roots ORDER BY path").ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    public static void AddRoot(string path)
    {
        using var c = Open();
        Cmd(c, "INSERT OR IGNORE INTO roots(path) VALUES(@p)", ("@p", path)).ExecuteNonQuery();
    }

    /// <summary>Folder prefix with a trailing separator, so a root named Movies does not also match Movies2.</summary>
    public static string Prefix(string root) =>
        root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

    /// <summary>Remove a library folder and the entries (and watch state) found only under it. Returns entries removed.</summary>
    public static int RemoveRoot(string root)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Cmd(c, "DELETE FROM roots WHERE path=@p", ("@p", root)).ExecuteNonQuery();
        var others = new List<string>();
        using (var r = Cmd(c, "SELECT path FROM roots").ExecuteReader())
            while (r.Read()) others.Add(Prefix(r.GetString(0)));

        var doomed = new List<long>();
        using (var r = Cmd(c, "SELECT id,path FROM media WHERE substr(path,1,length(@r))=@r", ("@r", Prefix(root))).ExecuteReader())
            while (r.Read())
                // A file that also sits under another remaining folder stays in the library.
                if (!others.Any(o => r.GetString(1).StartsWith(o, StringComparison.OrdinalIgnoreCase))) doomed.Add(r.GetInt64(0));

        foreach (var id in doomed)
        {
            Cmd(c, "DELETE FROM watch WHERE media_id=@i", ("@i", id)).ExecuteNonQuery();
            Cmd(c, "DELETE FROM media WHERE id=@i", ("@i", id)).ExecuteNonQuery();
        }
        tx.Commit();
        return doomed.Count;
    }

    /// <summary>Make media rows under root match found; rows with unchanged size and mtime are untouched. Returns rows added or changed.</summary>
    public static int SyncRoot(string root, List<Found> found, bool reparse = false)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var have = new Dictionary<string, (long id, long size, long mtime)>();
        using (var r = Cmd(c, "SELECT id,path,size,mtime FROM media WHERE substr(path,1,length(@r))=@r", ("@r", Prefix(root))).ExecuteReader())
            while (r.Read()) have[r.GetString(1)] = (r.GetInt64(0), r.GetInt64(2), r.GetInt64(3));

        int touched = 0;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var r = Cmd(c, "SELECT path FROM hidden").ExecuteReader())
            while (r.Read()) hidden.Add(r.GetString(0));
        foreach (var f in found)
        {
            if (hidden.Contains(f.Path)) continue;   // "have" keeps it, so a stale row for it is cleaned up below
            if (have.Remove(f.Path, out var old) && !reparse && old.size == f.Size && old.mtime == f.Mtime) continue;
            touched++;
            Cmd(c, """
                INSERT INTO media(path,kind,title,year,season,episode,size,mtime,added_at)
                VALUES(@path,@k,@t,@y,@s,@e,@size,@m,@now)
                ON CONFLICT(path) DO UPDATE SET tmdb_id=CASE WHEN title<>@t OR kind<>@k THEN NULL ELSE tmdb_id END,kind=@k,title=@t,year=@y,season=@s,episode=@e,size=@size,mtime=@m
                """,
                ("@path", f.Path), ("@k", f.P.Kind), ("@t", f.P.Title), ("@y", f.P.Year), ("@s", f.P.Season),
                ("@e", f.P.Episode), ("@size", f.Size), ("@m", f.Mtime), ("@now", now)).ExecuteNonQuery();
        }
        // Whatever is left was not found on disk any more.
        foreach (var gone in have.Values)
        {
            Cmd(c, "DELETE FROM watch WHERE media_id=@i", ("@i", gone.id)).ExecuteNonQuery();
            Cmd(c, "DELETE FROM media WHERE id=@i", ("@i", gone.id)).ExecuteNonQuery();
        }
        tx.Commit();
        return touched;
    }

    public static List<MediaItem> List()
    {
        using var c = Open();
        using var r = Cmd(c, """
            SELECT m.id,m.path,m.kind,COALESCE(t.title,m.title),m.year,m.season,m.episode,m.duration_sec,
                   COALESCE(w.position_sec,0),COALESCE(w.watched,0),t.poster_path,t.overview,t.rating,e.name,COALESCE(m.tmdb_id,0),t.backdrop_path,e.overview,t.year,m.added_at,COALESCE(w.played_at,0),m.title,m.year,e.still_path,e.air_date,e.runtime,e.rating,t.genres,m.size
            FROM media m
            LEFT JOIN watch w ON w.media_id=m.id
            LEFT JOIN meta t ON t.kind=m.kind AND t.tmdb_id=m.tmdb_id
            LEFT JOIN ep e ON e.tmdb_id=m.tmdb_id AND e.season=m.season AND e.episode=m.episode
            ORDER BY m.kind, lower(m.title), m.season, m.episode
            """).ExecuteReader();
        var l = new List<MediaItem>();
        while (r.Read())
            l.Add(new MediaItem
            {
                Id = r.GetInt64(0), Path = r.GetString(1), Kind = r.GetString(2), Title = r.GetString(3),
                Year = r.IsDBNull(4) ? null : r.GetInt32(4), Season = r.IsDBNull(5) ? null : r.GetInt32(5),
                Episode = r.IsDBNull(6) ? null : r.GetInt32(6), Duration = r.IsDBNull(7) ? null : r.GetDouble(7),
                Position = r.GetDouble(8), Watched = r.GetInt32(9) != 0,
                Poster = r.IsDBNull(10) ? null : PosterFile(r.GetString(10)),
                Overview = r.IsDBNull(11) ? null : r.GetString(11),
                Rating = r.IsDBNull(12) ? null : r.GetDouble(12),
                EpisodeName = r.IsDBNull(13) ? null : r.GetString(13),
                TmdbId = r.GetInt64(14),
                Backdrop = r.IsDBNull(15) ? null : PosterFile(r.GetString(15)),
                EpisodeOverview = r.IsDBNull(16) ? null : r.GetString(16),
                MetaYear = r.IsDBNull(17) ? null : r.GetInt32(17),
                AddedAt = r.GetInt64(18),
                PlayedAt = r.GetInt64(19),
                FileTitle = r.GetString(20),
                FileYear = r.IsDBNull(21) ? null : r.GetInt32(21),
                Still = r.IsDBNull(22) ? null : PosterFile(r.GetString(22)),
                AirDate = r.IsDBNull(23) ? null : r.GetString(23),
                EpRuntime = r.IsDBNull(24) ? null : r.GetInt32(24),
                EpRating = r.IsDBNull(25) ? null : r.GetDouble(25),
                Genres = r.IsDBNull(26) ? Array.Empty<string>() : r.GetString(26).Split('|', StringSplitOptions.RemoveEmptyEntries),
                Size = r.GetInt64(27),
            });
        return l;
    }

    /// <summary>Local cache path of a TMDB poster, or null if not downloaded yet.</summary>
    public static string ArtDir => Environment.GetEnvironmentVariable("LUMINA_ART") ?? System.IO.Path.Combine(
        Lumina.Windows.AppPaths.Cache, "art");

    public static string ArtPath(string posterPath) => System.IO.Path.Combine(ArtDir, posterPath.TrimStart('/'));

    static string? PosterFile(string posterPath) => File.Exists(ArtPath(posterPath)) ? ArtPath(posterPath) : null;

    /// <summary>Distinct titles never searched on TMDB (tmdb_id NULL). 0 means searched with no match.</summary>
    public static List<(string Kind, string Title, int? Year)> PendingTitles()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT DISTINCT kind,title,year FROM media WHERE tmdb_id IS NULL ORDER BY kind,title").ExecuteReader();
        var l = new List<(string, string, int?)>();
        while (r.Read()) l.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2)));
        return l;
    }

    public static void SetMatch(string kind, string title, int? year, Meta? hit)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (hit != null)
            Cmd(c, """
                INSERT OR REPLACE INTO meta(kind,tmdb_id,title,overview,poster_path,backdrop_path,rating,year)
                VALUES(@k,@id,@t,@o,@p,@b,@r,@y)
                """, ("@k", hit.Kind), ("@id", hit.TmdbId), ("@t", hit.Title), ("@o", hit.Overview),
                ("@p", hit.PosterPath), ("@b", hit.BackdropPath), ("@r", hit.Rating), ("@y", hit.Year)).ExecuteNonQuery();
        Cmd(c, "UPDATE media SET tmdb_id=@id WHERE kind=@k AND title=@t AND year IS @y AND tmdb_id IS NULL",
            ("@id", hit?.TmdbId ?? 0), ("@k", kind), ("@t", title), ("@y", year)).ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>(show tmdb id, season) pairs with at least one local episode lacking an ep row.</summary>
    public static List<(long Id, int Season)> SeasonsNeedingEpisodes()
    {
        using var c = Open();
        using var r = Cmd(c, """
            SELECT DISTINCT m.tmdb_id, m.season FROM media m
            WHERE m.kind='show' AND m.tmdb_id>0 AND m.season IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM ep e WHERE e.tmdb_id=m.tmdb_id AND e.season=m.season AND e.episode=m.episode)
            """).ExecuteReader();
        var l = new List<(long, int)>();
        while (r.Read()) l.Add((r.GetInt64(0), r.GetInt32(1)));
        return l;
    }

    public static void SetSeason(long tmdbId, int season, List<Ep> eps)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var e in eps)
            Cmd(c, "INSERT OR REPLACE INTO ep(tmdb_id,season,episode,name,overview,still_path,air_date,runtime,rating) VALUES(@i,@s,@n,@name,@o,@st,@ad,@rt,@r)",
                ("@i", tmdbId), ("@s", season), ("@n", e.Number), ("@name", e.Name), ("@o", e.Overview), ("@st", e.StillPath),
                ("@ad", e.AirDate), ("@rt", e.Runtime), ("@r", e.Rating)).ExecuteNonQuery();
        // Local episodes TMDB does not know get a blank row so this season is not refetched every run.
        Cmd(c, "INSERT OR IGNORE INTO ep(tmdb_id,season,episode) SELECT tmdb_id,season,episode FROM media WHERE kind='show' AND tmdb_id=@i AND season=@s",
            ("@i", tmdbId), ("@s", season)).ExecuteNonQuery();
        tx.Commit();
    }

    // ---------- title details, scores, favourites (schema v4) ----------

    /// <summary>
    /// Asks for details, scores and episode pictures again on the next scan. Matches, favourites and watch progress are not touched,
    /// and what is already stored stays until the new answer replaces it.
    /// </summary>
    public static void RequeueMetadata()
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Cmd(c, "UPDATE meta SET detail_at=NULL, ratings=NULL").ExecuteNonQuery();
        Cmd(c, "DELETE FROM ep WHERE name IS NULL AND overview IS NULL AND still_path IS NULL").ExecuteNonQuery();   // blank rows only mean "TMDB did not know it last time"
        tx.Commit();
    }

    public static List<(string Kind, long Id)> NeedingDetail()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT kind,tmdb_id FROM meta WHERE detail_at IS NULL AND tmdb_id>0").ExecuteReader();
        var l = new List<(string, long)>();
        while (r.Read()) l.Add((r.GetString(0), r.GetInt64(1)));
        return l;
    }

    /// <summary>Stores the detail and replaces the title's cast row. An empty Detail marks a title TMDB has nothing more for, so it is not asked again.</summary>
    public static void SetDetail(string kind, long id, Detail d)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        Cmd(c, "UPDATE meta SET runtime=@rt,certification=@cert,genres=@g,tagline=@t,logo_path=@l,imdb_id=@i,detail_at=@now WHERE kind=@k AND tmdb_id=@id",
            ("@rt", d.Runtime), ("@cert", d.Certification), ("@g", d.Genres.Count == 0 ? null : string.Join("|", d.Genres)), ("@t", d.Tagline),
            ("@l", d.Logo), ("@i", d.ImdbId), ("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds()), ("@k", kind), ("@id", id)).ExecuteNonQuery();
        Cmd(c, "DELETE FROM credits WHERE kind=@k AND tmdb_id=@id", ("@k", kind), ("@id", id)).ExecuteNonQuery();
        for (int i = 0; i < d.Cast.Count; i++)
            Cmd(c, "INSERT INTO credits(kind,tmdb_id,pos,person,role,photo_path) VALUES(@k,@id,@p,@n,@r,@ph)",
                ("@k", kind), ("@id", id), ("@p", i), ("@n", d.Cast[i].Person), ("@r", d.Cast[i].Role), ("@ph", d.Cast[i].Photo)).ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>Null until details were fetched for the title.</summary>
    public static Detail? GetDetail(string kind, long id)
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT runtime,certification,genres,tagline,logo_path,imdb_id,detail_at FROM meta WHERE kind=@k AND tmdb_id=@id", ("@k", kind), ("@id", id)).ExecuteReader();
        if (!r.Read()) return null;
        // detail_at is empty both for "never fetched" and for "fetch again" (RequeueMetadata); data that is already stored must stay visible in the second case
        if (r.IsDBNull(6) && Enumerable.Range(0, 6).All(r.IsDBNull)) return null;
        int? runtime = r.IsDBNull(0) ? null : r.GetInt32(0);
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        var genres = S(2)?.Split('|', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var d = new Detail(runtime, S(1), genres, S(3), S(4), S(5), Array.Empty<Credit>());
        r.Close();
        var cast = new List<Credit>();
        using var cr = Cmd(c, "SELECT person,role,photo_path FROM credits WHERE kind=@k AND tmdb_id=@id ORDER BY pos", ("@k", kind), ("@id", id)).ExecuteReader();
        while (cr.Read()) cast.Add(new Credit(cr.GetString(0), cr.IsDBNull(1) ? "" : cr.GetString(1), cr.IsDBNull(2) ? null : cr.GetString(2)));
        return d with { Cast = cast };
    }

    public static List<(string Kind, long Id, string ImdbId)> NeedingRatings()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT kind,tmdb_id,imdb_id FROM meta WHERE imdb_id IS NOT NULL AND ratings IS NULL").ExecuteReader();
        var l = new List<(string, long, string)>();
        while (r.Read()) l.Add((r.GetString(0), r.GetInt64(1), r.GetString(2)));
        return l;
    }

    /// <summary>Stored as {} when OMDb has no scores, so the title is not asked about again.</summary>
    public static void SetRatings(string kind, long id, Ratings ratings)
    {
        using var c = Open();
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?> { ["imdb"] = ratings.Imdb, ["rt"] = ratings.RottenTomatoes }.Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value));
        Cmd(c, "UPDATE meta SET ratings=@r WHERE kind=@k AND tmdb_id=@id", ("@r", json), ("@k", kind), ("@id", id)).ExecuteNonQuery();
    }

    public static Ratings GetRatings(string kind, long id)
    {
        using var c = Open();
        var json = Cmd(c, "SELECT ratings FROM meta WHERE kind=@k AND tmdb_id=@id", ("@k", kind), ("@id", id)).ExecuteScalar() as string;
        if (string.IsNullOrEmpty(json)) return new Ratings(null, null);
        try
        {
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            return new Ratings(d.GetValueOrDefault("imdb"), d.GetValueOrDefault("rt"));
        }
        catch (System.Text.Json.JsonException) { return new Ratings(null, null); }
    }

    static List<string> Paths(string sql)
    {
        using var c = Open();
        using var r = Cmd(c, sql).ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    public static List<string> LogoPaths() => Paths("SELECT DISTINCT logo_path FROM meta WHERE logo_path IS NOT NULL");
    public static List<string> CastPhotoPaths() => Paths("SELECT DISTINCT photo_path FROM credits WHERE photo_path IS NOT NULL");
    public static List<string> StillPaths() => Paths("SELECT DISTINCT still_path FROM ep WHERE still_path IS NOT NULL");

    // ---------- bookmarks (schema v5) ----------

    public record Bookmark(long Id, double Position, string Label);

    /// <summary>The bookmarks of a file, in the order they come in the video.</summary>
    public static List<Bookmark> Bookmarks(string path)
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT id,position_sec,label FROM bookmarks WHERE path=@p ORDER BY position_sec, id", ("@p", path)).ExecuteReader();
        var l = new List<Bookmark>();
        while (r.Read()) l.Add(new Bookmark(r.GetInt64(0), r.GetDouble(1), r.GetString(2)));
        return l;
    }

    /// <summary>
    /// The same key adds a bookmark here and, pressed again at about the same spot, takes it away.
    /// Returns what happened and the bookmark list afterwards. A new one is labelled Bookmark N.
    /// </summary>
    public static (bool Added, List<Bookmark> All) ToggleBookmark(string path, double positionSec, double nearSec = 2.0)
    {
        positionSec = Math.Max(0, positionSec);
        using var c = Open();
        var near = Cmd(c, "SELECT id FROM bookmarks WHERE path=@p AND abs(position_sec-@t)<=@w ORDER BY abs(position_sec-@t) LIMIT 1", ("@p", path), ("@t", positionSec), ("@w", nearSec)).ExecuteScalar();
        bool added;
        if (near is long id) { Cmd(c, "DELETE FROM bookmarks WHERE id=@i", ("@i", id)).ExecuteNonQuery(); added = false; }
        else
        {
            var n = Convert.ToInt32(Cmd(c, "SELECT COUNT(*) FROM bookmarks WHERE path=@p", ("@p", path)).ExecuteScalar()) + 1;
            Cmd(c, "INSERT INTO bookmarks(path,position_sec,label,created_at) VALUES(@p,@t,@l,@c)",
                ("@p", path), ("@t", positionSec), ("@l", "Bookmark " + n), ("@c", DateTimeOffset.UtcNow.ToUnixTimeSeconds())).ExecuteNonQuery();
            added = true;
        }
        return (added, Bookmarks(path));
    }

    public static void ClearBookmarks(string path)
    {
        using var c = Open();
        Cmd(c, "DELETE FROM bookmarks WHERE path=@p", ("@p", path)).ExecuteNonQuery();
    }

    public static bool IsFavourite(string kind, long id)
    {
        using var c = Open();
        return Cmd(c, "SELECT 1 FROM favourite WHERE kind=@k AND tmdb_id=@id", ("@k", kind), ("@id", id)).ExecuteScalar() != null;
    }

    public static void SetFavourite(string kind, long id, bool on)
    {
        using var c = Open();
        if (on) Cmd(c, "INSERT OR IGNORE INTO favourite(kind,tmdb_id,added_at) VALUES(@k,@id,@t)", ("@k", kind), ("@id", id), ("@t", DateTimeOffset.UtcNow.ToUnixTimeSeconds())).ExecuteNonQuery();
        else Cmd(c, "DELETE FROM favourite WHERE kind=@k AND tmdb_id=@id", ("@k", kind), ("@id", id)).ExecuteNonQuery();
    }

    /// <summary>Newest first.</summary>
    public static List<(string Kind, long Id)> Favourites()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT kind,tmdb_id FROM favourite ORDER BY added_at DESC, rowid DESC").ExecuteReader();
        var l = new List<(string, long)>();
        while (r.Read()) l.Add((r.GetString(0), r.GetInt64(1)));
        return l;
    }

    /// <summary>
    /// Keeps the artwork folder under a size. Oldest files go first. Anything deleted that is still wanted is downloaded again by the next scan.
    /// </summary>
    public static int TrimArt(long maxBytes)
    {
        if (!Directory.Exists(ArtDir)) return 0;
        var files = new DirectoryInfo(ArtDir).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        var removed = 0;
        foreach (var f in files)
        {
            if (total <= maxBytes) break;
            try { total -= f.Length; f.Delete(); removed++; } catch (IOException) { }
        }
        return removed;
    }

    public static List<string> BackdropPaths()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT DISTINCT backdrop_path FROM meta WHERE backdrop_path IS NOT NULL").ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    public static List<string> PosterPaths()
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT DISTINCT poster_path FROM meta WHERE poster_path IS NOT NULL").ExecuteReader();
        var l = new List<string>();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    public static List<LocalRow> LocalWatchRows()
    {
        using var c = Open();
        using var r = Cmd(c, """
            SELECT m.id,m.kind,m.tmdb_id,m.season,m.episode,COALESCE(w.position_sec,0),COALESCE(w.watched,0),
                   COALESCE(w.played_at,0),m.duration_sec,w.unwatched_at
            FROM media m LEFT JOIN watch w ON w.media_id=m.id
            WHERE m.tmdb_id>0 AND (m.kind='movie' OR (m.season IS NOT NULL AND m.episode IS NOT NULL))
            """).ExecuteReader();
        var l = new List<LocalRow>();
        while (r.Read())
            l.Add(new LocalRow(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetInt32(4), r.GetDouble(5), r.GetInt32(6) != 0, r.GetInt64(7),
                r.IsDBNull(8) ? null : r.GetDouble(8), r.IsDBNull(9) ? null : r.GetInt64(9)));
        return l;
    }

    /// <summary>Write watch state with an explicit played_at (used by Trakt pull). watched stays sticky.</summary>
    public static void SetWatch(long mediaId, double pos, bool watched, long playedAt)
    {
        using var c = Open();
        Cmd(c, """
            INSERT INTO watch(media_id,position_sec,watched,played_at) VALUES(@i,@p,@w,@n)
            ON CONFLICT(media_id) DO UPDATE SET position_sec=@p, watched=MAX(watched,@w), played_at=@n
            """, ("@i", mediaId), ("@p", pos), ("@w", watched ? 1 : 0), ("@n", playedAt)).ExecuteNonQuery();
    }

    /// <summary>
    /// The user's own call on one or more items. Watched clears the resume point; unwatched also clears it and remembers
    /// when, so a later Trakt pull does not bring the watched mark back.
    /// </summary>
    public static void MarkWatched(IEnumerable<long> ids, bool watched)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var id in ids)
        {
            var cmd = Cmd(c, """
                INSERT INTO watch(media_id,position_sec,watched,played_at,unwatched_at) VALUES(@i,0,@w,@p,@u)
                ON CONFLICT(media_id) DO UPDATE SET position_sec=0, watched=@w, played_at=@p, unwatched_at=@u
                """, ("@i", id), ("@w", watched ? 1 : 0), ("@p", watched ? now : 0), ("@u", watched ? null : now));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Take files out of the library without touching them on disk. They stay out across rescans until shown again.</summary>
    public static int Hide(IEnumerable<string> paths)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var n = 0;
        foreach (var p in paths)
        {
            foreach (var sql in new[] { "INSERT OR IGNORE INTO hidden(path) VALUES(@p)", "DELETE FROM watch WHERE media_id IN (SELECT id FROM media WHERE path=@p COLLATE NOCASE)", "DELETE FROM media WHERE path=@p COLLATE NOCASE" })
            {
                var cmd = Cmd(c, sql, ("@p", p)); cmd.Transaction = tx; n += cmd.ExecuteNonQuery() > 0 && sql.StartsWith("DELETE FROM media") ? 1 : 0;
            }
        }
        tx.Commit();
        return n;
    }

    /// <summary>
    /// The user says these files or folders are TV episodes ("show") or films ("movie"). The scanner honours it from now on;
    /// the next scan reads every file again so the change shows at once.
    /// </summary>
    public static void SetKind(IEnumerable<string> paths, string kind)
    {
        if (kind is not ("show" or "movie")) throw new ArgumentException("kind must be show or movie", nameof(kind));
        using (var c = Open())
        using (var tx = c.BeginTransaction())
        {
            foreach (var p in paths)
            {
                var cmd = Cmd(c, "INSERT INTO kind_override(path,kind) VALUES(@p,@k) ON CONFLICT(path) DO UPDATE SET kind=@k", ("@p", p.TrimEnd('\\', '/')), ("@k", kind));
                cmd.Transaction = tx; cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        KvSet("parse_v", "");
    }

    /// <summary>Every "this is a show / movie" the user has set, by file or folder path.</summary>
    public static Dictionary<string, string> KindOverrides()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var c = Open();
        using var r = Cmd(c, "SELECT path, kind FROM kind_override").ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    public static int HiddenCount()
    {
        using var c = Open();
        return Convert.ToInt32(Cmd(c, "SELECT COUNT(*) FROM hidden").ExecuteScalar());
    }

    /// <summary>Forget every hidden file so the next scan brings them back.</summary>
    public static int UnhideAll()
    {
        using var c = Open();
        return Cmd(c, "DELETE FROM hidden").ExecuteNonQuery();
    }

    /// <summary>
    /// "Edit metadata": point these files at the TMDB title the user picked (its details are fetched on the next refresh),
    /// or at nothing (<paramref name="hit"/> null: the file name is the only information, and TMDB is not asked again).
    /// </summary>
    public static void AssignMatch(IEnumerable<long> mediaIds, Meta? hit)
    {
        var ids = mediaIds.ToList();
        if (ids.Count == 0) return;
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (hit != null)
        {
            // keep what is already known about a title that is picked again; only fill in what the search result carries
            var up = Cmd(c, """
                INSERT INTO meta(kind,tmdb_id,title,overview,poster_path,backdrop_path,rating,year)
                VALUES(@k,@id,@t,@o,@p,@b,@r,@y)
                ON CONFLICT(kind,tmdb_id) DO UPDATE SET title=@t, overview=COALESCE(@o,overview), poster_path=COALESCE(@p,poster_path), backdrop_path=COALESCE(@b,backdrop_path), rating=@r, year=COALESCE(@y,year)
                """, ("@k", hit.Kind), ("@id", hit.TmdbId), ("@t", hit.Title), ("@o", hit.Overview), ("@p", hit.PosterPath), ("@b", hit.BackdropPath), ("@r", hit.Rating), ("@y", hit.Year));
            up.Transaction = tx; up.ExecuteNonQuery();
        }
        foreach (var id in ids)
        {
            var u = Cmd(c, "UPDATE media SET tmdb_id=@t WHERE id=@i", ("@t", hit?.TmdbId ?? 0), ("@i", id));
            u.Transaction = tx; u.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Make TMDB look this title up again, optionally under a different name and year ("Fix match").</summary>
    public static void Rematch(string kind, string title, int? year, string? queryTitle = null, int? queryYear = null)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var alias = $"alias:{kind}|{title.ToLowerInvariant()}|{year}";
        if (queryTitle != null)
        {
            var set = Cmd(c, "INSERT OR REPLACE INTO kv(key,value) VALUES(@k,@v)", ("@k", alias), ("@v", System.Text.Json.JsonSerializer.Serialize(new[] { queryTitle, queryYear?.ToString() ?? "" })));
            set.Transaction = tx; set.ExecuteNonQuery();
        }
        else { var del = Cmd(c, "DELETE FROM kv WHERE key=@k", ("@k", alias)); del.Transaction = tx; del.ExecuteNonQuery(); }
        var clr = Cmd(c, "UPDATE media SET tmdb_id=NULL WHERE kind=@k AND title=@t AND year IS @y", ("@k", kind), ("@t", title), ("@y", year));
        clr.Transaction = tx; clr.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>The name and year TMDB should be asked about for this library title.</summary>
    public static (string Title, int? Year) SearchTerms(string kind, string title, int? year)
    {
        try
        {
            if (KvGet($"alias:{kind}|{title.ToLowerInvariant()}|{year}") is { } json
                && System.Text.Json.JsonSerializer.Deserialize<string[]>(json) is { Length: 2 } a && a[0].Length > 0)
                return (a[0], int.TryParse(a[1], out var y) ? y : null);
        }
        catch (System.Text.Json.JsonException) { }
        return (title, year);
    }

    /// <summary>Forget every position and watched flag. Files, artwork and Trakt tokens stay.</summary>
    public static void ClearWatch()
    {
        using var c = Open();
        Cmd(c, "DELETE FROM watch").ExecuteNonQuery();
    }

    // ---- playlists ----

    public record PlaylistInfo(long Id, string Name, int Count, long CreatedAt);

    /// <summary>Save the items under a name, replacing a playlist that already has that name (case-insensitive). Returns its id.</summary>
    public static long SavePlaylist(string name, IEnumerable<QueueItem> items)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("A playlist needs a name.", nameof(name));
        using var c = Open();
        using var tx = c.BeginTransaction();
        long id;
        using (var find = Cmd(c, "SELECT id FROM playlists WHERE name=@n", ("@n", name))) { find.Transaction = tx; id = find.ExecuteScalar() is long l ? l : 0; }
        if (id == 0)
        {
            var ins = Cmd(c, "INSERT INTO playlists(name,created_at) VALUES(@n,@t); SELECT last_insert_rowid();", ("@n", name), ("@t", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            ins.Transaction = tx;
            id = (long)ins.ExecuteScalar()!;
        }
        else { var del = Cmd(c, "DELETE FROM playlist_items WHERE playlist_id=@i", ("@i", id)); del.Transaction = tx; del.ExecuteNonQuery(); }
        var pos = 0;
        foreach (var it in items)
        {
            var row = Cmd(c, "INSERT INTO playlist_items(playlist_id,pos,path,title) VALUES(@i,@p,@a,@t)", ("@i", id), ("@p", pos++), ("@a", it.Path), ("@t", it.Title));
            row.Transaction = tx;
            row.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    /// <summary>Adds items to the end of an existing playlist. Returns how many were added.</summary>
    public static int AppendToPlaylist(long id, IEnumerable<QueueItem> items)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var max = Cmd(c, "SELECT COALESCE(MAX(pos),-1) FROM playlist_items WHERE playlist_id=@i", ("@i", id));
        max.Transaction = tx;
        var pos = Convert.ToInt32(max.ExecuteScalar()) + 1;
        var n = 0;
        foreach (var it in items)
        {
            var row = Cmd(c, "INSERT INTO playlist_items(playlist_id,pos,path,title) VALUES(@i,@p,@a,@t)", ("@i", id), ("@p", pos++), ("@a", it.Path), ("@t", it.Title));
            row.Transaction = tx;
            row.ExecuteNonQuery();
            n++;
        }
        tx.Commit();
        return n;
    }

    public static List<PlaylistInfo> Playlists()
    {
        using var c = Open();
        using var r = Cmd(c, """
            SELECT p.id, p.name, (SELECT COUNT(*) FROM playlist_items i WHERE i.playlist_id=p.id), p.created_at
            FROM playlists p ORDER BY p.name COLLATE NOCASE
            """).ExecuteReader();
        var l = new List<PlaylistInfo>();
        while (r.Read()) l.Add(new PlaylistInfo(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt64(3)));
        return l;
    }

    public static List<QueueItem> PlaylistItems(long id)
    {
        using var c = Open();
        using var r = Cmd(c, "SELECT path, title FROM playlist_items WHERE playlist_id=@i ORDER BY pos", ("@i", id)).ExecuteReader();
        var l = new List<QueueItem>();
        while (r.Read()) l.Add(new QueueItem(r.GetString(0), r.GetString(1)));
        return l;
    }

    /// <summary>False when another playlist already uses the name.</summary>
    public static bool RenamePlaylist(long id, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return false;
        using var c = Open();
        try { return Cmd(c, "UPDATE playlists SET name=@n WHERE id=@i", ("@n", name), ("@i", id)).ExecuteNonQuery() == 1; }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { return false; } // UNIQUE
    }

    public static void DeletePlaylist(long id)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var sql in new[] { "DELETE FROM playlist_items WHERE playlist_id=@i", "DELETE FROM playlists WHERE id=@i" })
        {
            var cmd = Cmd(c, sql, ("@i", id)); cmd.Transaction = tx; cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static void KvDelete(string key)
    {
        using var c = Open();
        Cmd(c, "DELETE FROM kv WHERE key=@k", ("@k", key)).ExecuteNonQuery();
    }

    public static string? KvGet(string key)
    {
        using var c = Open();
        return Cmd(c, "SELECT value FROM kv WHERE key=@k", ("@k", key)).ExecuteScalar() as string;
    }

    public static void KvSet(string key, string value)
    {
        using var c = Open();
        Cmd(c, "INSERT OR REPLACE INTO kv(key,value) VALUES(@k,@v)", ("@k", key), ("@v", value)).ExecuteNonQuery();
    }

    public static void SaveWatch(long mediaId, double pos, double? dur, bool watched)
    {
        using var c = Open();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (dur > 0) Cmd(c, "UPDATE media SET duration_sec=@d WHERE id=@i", ("@d", dur), ("@i", mediaId)).ExecuteNonQuery();
        // watched is sticky: a later partial replay never clears it.
        Cmd(c, """
            INSERT INTO watch(media_id,position_sec,watched,played_at) VALUES(@i,@p,@w,@n)
            ON CONFLICT(media_id) DO UPDATE SET position_sec=@p, watched=MAX(watched,@w), played_at=@n
            """, ("@i", mediaId), ("@p", pos), ("@w", watched ? 1 : 0), ("@n", now)).ExecuteNonQuery();
    }
}
