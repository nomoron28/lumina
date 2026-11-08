using System.Runtime.InteropServices;
using System.Text;

namespace Lumina.Network;

public sealed record Login(string Target, string User, string Password);

/// <summary>
/// Logins for servers that ask for one, kept in Windows Credential Manager so nothing secret ever sits in Lumina's own files.
/// Targets are namespaced ("Lumina:https://host:8080") and can only be read back by this Windows account.
/// </summary>
public sealed class CredentialStore
{
    public const string Prefix = "Lumina:";
    readonly string _prefix;

    /// <param name="prefix">Tests use their own prefix so they never touch a real login.</param>
    public CredentialStore(string? prefix = null) => _prefix = prefix ?? Environment.GetEnvironmentVariable("LUMINA_CRED_PREFIX") ?? Prefix;

    // CRED_TYPE_GENERIC, CRED_PERSIST_LOCAL_MACHINE
    const int Generic = 1, PersistLocal = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public int Flags, Type;
        public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWriteW(ref CREDENTIAL c, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredReadW(string target, int type, int flags, out IntPtr cred);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDeleteW(string target, int type, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredEnumerateW(string? filter, int flags, out int count, out IntPtr creds);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr buffer);

    /// <summary>The key a server's login is filed under: scheme, host and port, lower case, no path and no user info.</summary>
    public static string TargetFor(Uri uri) => $"{uri.Scheme}://{uri.Host}:{uri.Port}".ToLowerInvariant();

    string Full(string target) => _prefix + target;

    public void Save(string target, string user, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var c = new CREDENTIAL
            {
                Type = Generic, TargetName = Full(target), UserName = user, Persist = PersistLocal,
                CredentialBlobSize = blob.Length, CredentialBlob = handle.AddrOfPinnedObject(), Comment = "Saved by Lumina",
            };
            if (!CredWriteW(ref c, 0)) throw new InvalidOperationException("Windows would not save the login (error " + Marshal.GetLastWin32Error() + ")");
        }
        finally { Array.Clear(blob); handle.Free(); }
    }

    public Login? Load(string target)
    {
        if (!CredReadW(Full(target), Generic, 0, out var p)) return null;
        try { return Read(p); }
        finally { CredFree(p); }
    }

    static Login Read(IntPtr p)
    {
        var c = Marshal.PtrToStructure<CREDENTIAL>(p);
        var pass = c.CredentialBlobSize > 0 ? Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2) ?? "" : "";
        return new Login(c.TargetName, c.UserName ?? "", pass);
    }

    public bool Delete(string target) => CredDeleteW(Full(target), Generic, 0);

    /// <summary>Every login filed under this store's prefix. Passwords are included, so callers show only target and user.</summary>
    public List<Login> List()
    {
        var result = new List<Login>();
        if (!CredEnumerateW(_prefix + "*", 0, out var count, out var arr)) return result;     // none found is an error code, not an exception
        try
        {
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
                var l = Read(item);
                result.Add(l with { Target = l.Target[_prefix.Length..] });
            }
        }
        finally { CredFree(arr); }
        return result.OrderBy(l => l.Target, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // CRED_TYPE_DOMAIN_PASSWORD: what Windows itself consults when a program opens \\server\share
    const int DomainPassword = 2;

    /// <summary>Also give Windows the login for a file server, so any program (and Lumina's library folders) can open its shares.</summary>
    public static void SaveForWindowsShare(string server, string user, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var c = new CREDENTIAL { Type = DomainPassword, TargetName = server, UserName = user, Persist = PersistLocal, CredentialBlobSize = blob.Length, CredentialBlob = handle.AddrOfPinnedObject(), Comment = "Saved by Lumina" };
            if (!CredWriteW(ref c, 0)) throw new InvalidOperationException("Windows would not save the share login (error " + Marshal.GetLastWin32Error() + ")");
        }
        finally { Array.Clear(blob); handle.Free(); }
    }

    public static bool DeleteForWindowsShare(string server) => CredDeleteW(server, DomainPassword, 0);

    public static bool HasWindowsShareLogin(string server) { if (!CredReadW(server, DomainPassword, 0, out var p)) return false; CredFree(p); return true; }

    /// <summary>The server name in an address like \\nas\media, smb://nas/media or nas, or null if it is not a file-share address.</summary>
    public static string? ShareServer(string address)
    {
        address = address.Trim();
        if (address.StartsWith(@"\\")) { var rest = address[2..]; var i = rest.IndexOfAny(new[] { '\\', '/' }); return (i < 0 ? rest : rest[..i]).Trim() is { Length: > 0 } n ? n : null; }
        if (address.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(address, UriKind.Absolute, out var u)) return u.Host;
        return null;
    }

    public static string BasicHeader(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));
}
