namespace Lumina.Tests;

/// <summary>Windows Credential Manager is a system service: a read right after a write can lag for a moment. Tests wait for it instead of assuming.</summary>
public static class Settle
{
    public static T Until<T>(Func<T> read, Func<T, bool> ok, int ms = 4000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        T v;
        while (!ok(v = read()) && DateTime.UtcNow < until) Thread.Sleep(40);
        return v;
    }
}
