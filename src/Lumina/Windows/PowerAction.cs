using System.Diagnostics;
using System.IO;

namespace Lumina.Windows;

/// <summary>What happens to the PC when the sleep timer ends. Nothing runs here without the user having chosen it in Settings.</summary>
public static class PowerAction
{
    public static readonly string[] Kinds = { "pause", "stop", "sleep", "shutdown" };

    /// <summary>The program and arguments for kinds that touch the PC; null for the ones that stay inside Lumina.</summary>
    public static (string File, string Args)? CommandFor(string kind) => kind switch
    {
        // sleep, not hibernate, and not forced: apps that object can say so
        "sleep" => ("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0"),
        // a full minute of Windows' own warning, so someone who comes back can cancel it (shutdown /a)
        "shutdown" => ("shutdown.exe", "/s /t 60 /c \"Lumina sleep timer: shutting down in one minute. Run shutdown /a to cancel.\""),
        _ => null,
    };

    /// <summary>
    /// Runs the command for a PC-level kind. LUMINA_POWER_LOG makes it write the command to that file instead, so tests never sleep or shut down a real PC.
    /// Returns true when something was run (or logged).
    /// </summary>
    public static bool Run(string kind)
    {
        if (CommandFor(kind) is not { } cmd) return false;
        if (Environment.GetEnvironmentVariable("LUMINA_POWER_LOG") is { Length: > 0 } log)
        {
            File.AppendAllText(log, $"{kind}|{cmd.File}|{cmd.Args}{Environment.NewLine}");
            return true;
        }
        Process.Start(new ProcessStartInfo(cmd.File, cmd.Args) { UseShellExecute = false, CreateNoWindow = true });
        return true;
    }
}
