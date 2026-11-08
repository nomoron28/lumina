using System.IO;
using Lumina.Network;

namespace Lumina.Windows;

/// <summary>Window-less jobs for the installer: run, report success as an exit code, never throw.</summary>
public static class Maintenance
{
    public static int Run(string action)
    {
        try
        {
            switch (action)
            {
                case "register-types": FileAssociations.ForThisApp().Register(notifyShell: true); return 0;
                case "unregister-types": FileAssociations.ForThisApp().Unregister(notifyShell: true); return 0;
                case "forget-logins": ForgetLogins(new CredentialStore()); return 0;
                default: return 2;
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException)
        {
            Lumina.Diagnostics.Log.Error("Maintenance " + action + " failed", e);
            return 1;
        }
    }

    /// <summary>Remove every login Lumina saved, including the copies it gave Windows for file shares.</summary>
    public static int ForgetLogins(CredentialStore store)
    {
        var all = store.List();
        foreach (var l in all)
        {
            store.Delete(l.Target);
            if (CredentialStore.ShareServer(l.Target) is { } server) CredentialStore.DeleteForWindowsShare(server);
        }
        return all.Count;
    }
}
