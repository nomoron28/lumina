using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lumina.Library;

/// <summary>The PIN: four to eight digits, kept only as a salted hash (never the digits), compared in constant time.</summary>
public static class Pin
{
    public const int Iterations = 120_000;

    public static bool IsValid(string? pin) => pin is { Length: >= 4 and <= 8 } && pin.All(char.IsAsciiDigit);

    static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>What to store: a small JSON text with a fresh random salt.</summary>
    public static string Create(string pin, int iterations = Iterations)
    {
        if (!IsValid(pin)) throw new ArgumentException("A PIN is four to eight digits.", nameof(pin));
        var salt = RandomNumberGenerator.GetBytes(16);
        return JsonSerializer.Serialize(new Stored(1, Convert.ToBase64String(salt), Convert.ToBase64String(Derive(pin, salt, iterations)), iterations));
    }

    record Stored(int V, string Salt, string Hash, int Iter);

    /// <summary>False for a wrong PIN, for nonsense, and for a missing or damaged stored value. Never throws.</summary>
    public static bool Verify(string? pin, string? stored)
    {
        if (!IsValid(pin) || string.IsNullOrEmpty(stored)) return false;
        try
        {
            var s = JsonSerializer.Deserialize<Stored>(stored);
            if (s is null || s.V != 1 || s.Iter < 1000 || s.Iter > 5_000_000) return false;
            var expected = Convert.FromBase64String(s.Hash);
            return CryptographicOperations.FixedTimeEquals(Derive(pin!, Convert.FromBase64String(s.Salt), s.Iter), expected);
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException) { return false; }
    }
}

/// <summary>Is a file inside a locked folder? Paths are made canonical first, so spelling tricks do not get around it.</summary>
public static class LockedPaths
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint size);

    /// <summary>Absolute, with ".." and "." resolved, one kind of slash, no trailing slash, 8.3 short names expanded, lower case.</summary>
    public static string Normalize(string path)
    {
        var full = path.Trim();
        try
        {
            full = Path.GetFullPath(full.Replace('/', '\\'));
            if (full.Contains('~') && OperatingSystem.IsWindows())
            {
                var sb = new System.Text.StringBuilder(1024);
                var n = GetLongPathName(full, sb, (uint)sb.Capacity);     // only works for a path that exists; otherwise the text stays as it was
                if (n > 0 && n < sb.Capacity) full = sb.ToString();
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        return full.TrimEnd('\\', '/').ToLowerInvariant();
    }

    /// <summary>True when <paramref name="path"/> is the folder itself or anywhere below it. "Movies2" is not inside "Movies".</summary>
    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path); var r = Normalize(root);
        if (r.Length == 0) return false;
        return p == r || p.StartsWith(r + "\\", StringComparison.Ordinal);
    }

    public static bool IsLocked(string path, IEnumerable<string> lockedRoots) => lockedRoots.Any(r => IsUnder(path, r));

    /// <summary>For checking many files against the same folders: the folders are made canonical once.</summary>
    public static Func<string, bool> Matcher(IEnumerable<string> lockedRoots)
    {
        var roots = lockedRoots.Select(Normalize).Where(r => r.Length > 0).ToArray();
        if (roots.Length == 0) return _ => false;
        return path =>
        {
            var p = Normalize(path);
            return roots.Any(r => p == r || p.StartsWith(r + "\\", StringComparison.Ordinal));
        };
    }
}

/// <summary>Too many wrong PINs in a row lock the PIN prompt for a while. The clock is passed in, so it is testable.</summary>
public sealed class AttemptLimiter
{
    readonly int _max;
    readonly TimeSpan _lockout;
    int _failures;
    DateTime _until;

    public AttemptLimiter(int maxFailures = 5, TimeSpan? lockout = null) { _max = maxFailures; _lockout = lockout ?? TimeSpan.FromSeconds(30); }

    public bool Allowed(DateTime now) => now >= _until;

    public TimeSpan Wait(DateTime now) => now >= _until ? TimeSpan.Zero : _until - now;

    public void Failed(DateTime now)
    {
        if (now < _until) return;
        if (++_failures >= _max) { _until = now + _lockout; _failures = 0; }
    }

    public void Succeeded() { _failures = 0; _until = default; }
}

/// <summary>The PIN and the locked folders, in the database (never in config.json).</summary>
public static class LockStore
{
    const string PinKey = "parental_pin", FoldersKey = "parental_folders";

    public static bool HasPin => !string.IsNullOrEmpty(Db.KvGet(PinKey));

    public static void SetPin(string pin) => Db.KvSet(PinKey, Pin.Create(pin));

    public static bool CheckPin(string? pin) => Pin.Verify(pin, Db.KvGet(PinKey));

    /// <summary>Removing the PIN also unlocks every folder: a lock with no PIN could never be opened.</summary>
    public static void ClearPin() { Db.KvDelete(PinKey); Db.KvDelete(FoldersKey); }

    public static IReadOnlyList<string> Folders
    {
        get
        {
            try { return JsonSerializer.Deserialize<List<string>>(Db.KvGet(FoldersKey) ?? "[]") ?? new(); }
            catch (JsonException) { return new List<string>(); }
        }
    }

    public static bool IsFolderLocked(string root) => Folders.Any(f => LockedPaths.Normalize(f) == LockedPaths.Normalize(root));

    public static void SetFolderLocked(string root, bool locked)
    {
        var list = Folders.Where(f => LockedPaths.Normalize(f) != LockedPaths.Normalize(root)).ToList();
        if (locked) list.Add(root);
        Db.KvSet(FoldersKey, JsonSerializer.Serialize(list));
    }
}
