using System.Diagnostics;
using System.IO;

namespace Lumina.Windows;

public static class Explorer
{
    /// <summary>The argument that makes Explorer open the folder with this file highlighted.</summary>
    public static string SelectArgs(string path) => "/select,\"" + path + "\"";

    public static bool CanReveal(string path) => !string.IsNullOrWhiteSpace(path) && !path.Contains("://") && File.Exists(path);

    public static void Reveal(string path)
    {
        if (!CanReveal(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", SelectArgs(path)) { UseShellExecute = true });
    }
}
