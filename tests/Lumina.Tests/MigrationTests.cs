using System.IO;
using Lumina.Library;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumina.Tests;

[Collection("db")]
public class MigrationTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "lumina-mig-" + Guid.NewGuid().ToString("N"));
    string DbPath => Path.Combine(_dir, "lumina.db");
    readonly string? _old = Environment.GetEnvironmentVariable("LUMINA_DB");

    public MigrationTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("LUMINA_DB", DbPath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Environment.SetEnvironmentVariable("LUMINA_DB", _old);
        Directory.Delete(_dir, true);
    }

    static void Sql(string path, string sql)
    {
        using var c = new SqliteConnection("Data Source=" + path);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static double Scalar(string path, string sql)
    {
        using var c = new SqliteConnection("Data Source=" + path);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    // The database exactly as the first public build created it (no schema_version key). Never edit this string:
    // it is the proof that today's users' data survives every future change.
    const string V1 = """
        CREATE TABLE roots(id INTEGER PRIMARY KEY, path TEXT UNIQUE NOT NULL);
        CREATE TABLE media(id INTEGER PRIMARY KEY, path TEXT UNIQUE NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL,
          year INTEGER, season INTEGER, episode INTEGER, tmdb_id INTEGER, duration_sec REAL,
          size INTEGER NOT NULL, mtime INTEGER NOT NULL, added_at INTEGER NOT NULL);
        CREATE TABLE watch(media_id INTEGER PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
          position_sec REAL NOT NULL, watched INTEGER NOT NULL, played_at INTEGER NOT NULL, trakt_synced_at INTEGER);
        CREATE TABLE kv(key TEXT PRIMARY KEY, value TEXT);
        CREATE TABLE meta(kind TEXT NOT NULL, tmdb_id INTEGER NOT NULL, title TEXT NOT NULL, overview TEXT,
          poster_path TEXT, backdrop_path TEXT, rating REAL, year INTEGER, PRIMARY KEY(kind, tmdb_id));
        CREATE TABLE ep(tmdb_id INTEGER NOT NULL, season INTEGER NOT NULL, episode INTEGER NOT NULL,
          name TEXT, overview TEXT, still_path TEXT, PRIMARY KEY(tmdb_id, season, episode));
        INSERT INTO roots(path) VALUES('D:\media');
        INSERT INTO media(id,path,kind,title,size,mtime,added_at) VALUES(7,'D:\media\a.mkv','movie','A',1,2,3);
        INSERT INTO watch(media_id,position_sec,watched,played_at) VALUES(7,123.5,0,99);
        INSERT INTO kv(key,value) VALUES('trakt_token','keepme');
        """;

    [Fact]
    public void ADatabaseFromBeforeVersioningUpgradesIntact()
    {
        Sql(DbPath, V1);
        Db.Init();
        Assert.Equal(1 + Db.Migrations.Count, Db.SchemaVersion()); // the shipped steps ran
        Assert.Equal(123.5, Scalar(DbPath, "SELECT position_sec FROM watch WHERE media_id=7"));
        Assert.Equal("keepme", Db.KvGet("trakt_token"));
        Assert.NotEmpty(Directory.GetFiles(_dir, "*.bak-v1")); // and left a backup of the old file
    }

    [Fact]
    public void PendingStepsRunInOrderAfterABackupAndKeepTheData()
    {
        Sql(DbPath, V1);
        // raw v1 file: no Init, so only the steps given here run
        
        Db.Migrate(new[] { "ALTER TABLE media ADD COLUMN note TEXT;", "CREATE TABLE extra(id INTEGER PRIMARY KEY);" });
        Assert.Equal(3, Db.SchemaVersion());
        Assert.Equal(1, Scalar(DbPath, "SELECT COUNT(*) FROM pragma_table_info('media') WHERE name='note'"));
        Assert.Equal(1, Scalar(DbPath, "SELECT COUNT(*) FROM sqlite_master WHERE name='extra'"));
        var backup = Assert.Single(Directory.GetFiles(_dir, "*.bak-v1"));
        Assert.Equal(0, Scalar(backup, "SELECT COUNT(*) FROM pragma_table_info('media') WHERE name='note'")); // the copy is the old shape
        Assert.Equal(123.5, Scalar(backup, "SELECT position_sec FROM watch WHERE media_id=7"));
    }

    [Fact]
    public void AFailingStepRollsBackAndLeavesTheEarlierVersion()
    {
        Sql(DbPath, V1);
        // raw v1 file: no Init, so only the steps given here run
        
        var steps = new[] { "ALTER TABLE media ADD COLUMN note TEXT;", "THIS IS NOT SQL;" };
        Assert.ThrowsAny<SqliteException>(() => Db.Migrate(steps));
        Assert.Equal(2, Db.SchemaVersion());                 // step one stuck, step two did not
        Assert.Equal(123.5, Scalar(DbPath, "SELECT position_sec FROM watch WHERE media_id=7"));
    }

    [Fact]
    public void MigratingTwiceDoesNothingTheSecondTime()
    {
        Sql(DbPath, V1);
        // raw v1 file: no Init, so only the steps given here run
        
        var steps = new[] { "ALTER TABLE media ADD COLUMN note TEXT;" };
        Db.Migrate(steps);
        Db.Migrate(steps); // would throw "duplicate column" if it re-ran
        Assert.Equal(2, Db.SchemaVersion());
    }

    [Fact]
    public void AFreshDatabaseIsCreatedWithTheFullSchema()
    {
        Db.Init();
        Assert.Equal(1 + Db.Migrations.Count, Db.SchemaVersion());
    }

    [Fact]
    public void TheShippedMigrationsUpgradeTheFrozenV1DatabaseAndKeepEverything()
    {
        Sql(DbPath, V1);
        Db.Init();                                         // runs the real Migrations list
        Assert.Equal(1 + Db.Migrations.Count, Db.SchemaVersion());
        Assert.Equal(123.5, Scalar(DbPath, "SELECT position_sec FROM watch WHERE media_id=7"));
        Assert.Equal("keepme", Db.KvGet("trakt_token"));
        Assert.Single(Directory.GetFiles(_dir, "*.bak-v1"));
        Assert.Equal(1, Scalar(DbPath, "SELECT COUNT(*) FROM sqlite_master WHERE name='playlists'"));
    }

    [Fact]
    public void PlaylistsSaveReplaceRenameAndDelete()
    {
        Db.Init();
        var a = new[] { new QueueItem("a.mkv", "A"), new QueueItem("b.mkv", "B") };
        var id = Db.SavePlaylist("Weekend", a);
        Assert.Equal(new[] { "A", "B" }, Db.PlaylistItems(id).Select(x => x.Title));
        Assert.Equal(id, Db.SavePlaylist("weekend", new[] { new QueueItem("c.mkv", "C") })); // same name, any case: replaced, not duplicated
        Assert.Equal(new[] { "C" }, Db.PlaylistItems(id).Select(x => x.Title));
        var other = Db.SavePlaylist("Other", a);
        Assert.False(Db.RenamePlaylist(other, "WEEKEND"));     // taken
        Assert.True(Db.RenamePlaylist(other, "Friday"));
        Assert.Equal(new[] { "Friday", "Weekend" }, Db.Playlists().Select(p => p.Name));
        Assert.Equal(2, Db.Playlists().First(p => p.Name == "Friday").Count);
        Db.DeletePlaylist(id);
        Assert.Empty(Db.PlaylistItems(id));
        Assert.Single(Db.Playlists());
        Assert.Throws<ArgumentException>(() => Db.SavePlaylist("  ", a));
    }

    [Fact]
    public void PlaylistOrderAndOddCharactersSurviveARoundTrip()
    {
        Db.Init();
        var items = Enumerable.Range(0, 30).Select(i => new QueueItem($"D:/media/Ep{i}'s \"best\".mkv", $"Title {i} é中")).ToList();
        var id = Db.SavePlaylist("Long", items);
        Assert.Equal(items, Db.PlaylistItems(id));
    }
}
