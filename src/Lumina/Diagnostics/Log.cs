using System.IO;
using System.Windows;

namespace Lumina.Diagnostics;

/// <summary>
/// Plain-text log in %APPDATA%\Lumina\logs (LUMINA_LOG overrides the folder). Errors that would otherwise vanish
/// (UI thread exceptions, background tasks nobody awaited) end up here, and the smoke gate fails on any ERROR line.
/// </summary>
public static class Log
{
    static readonly object Gate = new();
    static DateTime _lastDialog = DateTime.MinValue;

    public static string Dir => Environment.GetEnvironmentVariable("LUMINA_LOG") is { Length: > 0 } d ? d : Path.Combine(
        Lumina.Windows.AppPaths.Data, "logs");

    public static string FilePath => Path.Combine(Dir, $"lumina-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? e = null) => Write("ERROR", e == null ? message : $"{message}\n{e}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // logging must never be the thing that fails
    }

    /// <summary>Number of ERROR lines in today's log. The smoke test asserts this stays 0.</summary>
    public static int ErrorCount()
    {
        try { return File.Exists(FilePath) ? File.ReadLines(FilePath).Count(l => l.Length > 13 && l.AsSpan(13).StartsWith("ERROR ")) : 0; }
        catch (IOException) { return 0; }
    }

    /// <summary>Delete logs older than two weeks.</summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var f in Directory.EnumerateFiles(Dir, "lumina-*.log").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)))
                File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>One entry point for every unhandled-error source. Returns true when the user should be told.</summary>
    public static bool Unhandled(string source, Exception e, bool fatal = false)
    {
        Error($"{(fatal ? "FATAL " : "")}{source}", e);
        if (fatal) return false;
        lock (Gate)
        {
            // A broken timer can throw every frame; one dialog per few seconds is plenty.
            if (DateTime.Now - _lastDialog < TimeSpan.FromSeconds(5)) return false;
            _lastDialog = DateTime.Now;
            return true;
        }
    }

    public static void Install(Application app)
    {
        Prune();
        Info($"Lumina started, {Environment.OSVersion}");
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true; // keep the app alive: a failed click should not lose the user's place
            if (Unhandled("UI thread", e.Exception))
                ConfirmDialog.Tell(app.MainWindow, "Something went wrong", $"{e.Exception.Message}\n\nDetails were saved to the log in Settings, Advanced.");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Unhandled("AppDomain", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) => { Unhandled("Background task", e.Exception); e.SetObserved(); };
    }
}
