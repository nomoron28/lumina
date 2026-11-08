using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lumina.Diagnostics;

namespace Lumina.Windows;

/// <summary>
/// One Lumina per Windows session. The first launch owns a named mutex and listens on a named pipe; later launches send their
/// command line to it and exit, so double-clicking a video while Lumina is open plays it there.
/// LUMINA_INSTANCE gives tests their own name so they never meet a real session.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex? _mutex;
    readonly string _pipe;
    CancellationTokenSource? _cts;

    public bool IsFirst { get; }

    /// <summary>Raised on a background thread with the arguments another launch forwarded.</summary>
    public event Action<string[]>? Received;

    public static string Name => "Lumina." + (Environment.GetEnvironmentVariable("LUMINA_INSTANCE") ?? "main");

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    public SingleInstance(string? name = null)
    {
        name ??= Name;
        _pipe = name + ".pipe";
        _mutex = new Mutex(true, @"Local\" + name, out var created);
        IsFirst = created;
        if (!created) { _mutex.Dispose(); _mutex = null; }
    }

    public void Listen()
    {
        if (!IsFirst || _cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() => Serve(token)) { IsBackground = true, Name = "Lumina single instance" }.Start();
    }

    void Serve(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                var line = reader.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                string[]? args = null;
                try { args = JsonSerializer.Deserialize<string[]>(line ?? ""); }
                catch (JsonException) { Log.Info("Ignored a malformed message on the single-instance pipe"); }
                if (args != null)
                {
                    Received?.Invoke(args);
                    var w = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };
                    w.WriteLine("ok");
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException or UnauthorizedAccessException)
            {
                if (token.IsCancellationRequested) return;
                Log.Info("Single-instance pipe: " + e.Message);   // a client that vanished must not stop the listener
            }
        }
    }

    /// <summary>Hand the command line to the running instance. False if nobody answered, so the caller can run normally.</summary>
    public static bool Send(string[] args, string? name = null, int timeoutMs = 2500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", (name ?? Name) + ".pipe", PipeDirection.InOut);
            client.Connect(timeoutMs);
            AllowSetForegroundWindow(-1);    // let the running window come to the front for us
            var w = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            w.WriteLine(JsonSerializer.Serialize(args));
            using var r = new StreamReader(client, Encoding.UTF8);
            return r.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).GetAwaiter().GetResult() == "ok";
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex?.Dispose();
    }
}
