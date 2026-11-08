using System.Net;
using System.Net.Sockets;

namespace Lumina.Tests;

/// <summary>Local test servers on ports the operating system says are free, with a retry for the rare race between choosing and binding.</summary>
public static class TestNet
{
    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Start an HttpListener on a free port. Returns the listener and its base address.</summary>
    public static (HttpListener Listener, string Base) StartListener(string host = "localhost")
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = FreePort();
            var l = new HttpListener();
            var b = $"http://{host}:{port}/";
            l.Prefixes.Add(b);
            try { l.Start(); return (l, b); }
            catch (HttpListenerException) when (attempt < 10) { l.Close(); }   // someone took the port in between: pick another
        }
    }
}
