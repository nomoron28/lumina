using System.IO;
using System.Text.RegularExpressions;

namespace Lumina.Diagnostics;

/// <summary>
/// Settings, Advanced, Report a problem: a new GitHub issue with the version and Windows build filled in, and the end of
/// today's log ready to paste. Nothing is sent by Lumina; the person reads and submits the issue in their browser.
/// The log goes to the clipboard rather than the link, so the link stays short and nothing personal sits in a URL.
/// </summary>
public static partial class Report
{
    public const string NewIssue = "https://github.com/nomoron28/lumina/issues/new";

    /// <summary>The new-issue link: bug label, a short template with the version and Windows build.</summary>
    public static string IssueUrl(string version, string os) =>
        NewIssue + "?labels=bug&body=" + Uri.EscapeDataString(
            $"**What happened**\n\n\n**What you expected**\n\n\n**Steps to reproduce**\n1. \n\n" +
            $"**Lumina** {version} · **Windows** {os}\n\n" +
            "**Log** (paste here: Lumina copied the end of today's log, with file paths removed)\n\n");

    /// <summary>The last <paramref name="lines"/> lines of today's log (or the newest log), redacted. Empty when there is no log.</summary>
    public static string LogTail(int lines = 60)
    {
        var file = File.Exists(Log.FilePath) ? Log.FilePath
            : Directory.Exists(Log.Dir) ? Directory.GetFiles(Log.Dir, "lumina-*.log").OrderDescending().FirstOrDefault() : null;
        if (file == null) return "";
        string[] all;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);   // the app may be writing to it
            using var sr = new StreamReader(fs);
            all = sr.ReadToEnd().TrimEnd().Split('\n');
        }
        catch (IOException) { return ""; }
        return Redact(string.Join("\n", all.TakeLast(lines)).TrimEnd());
    }

    /// <summary>
    /// Takes out what could identify the person or their files: full paths (drive and network), the Windows user name,
    /// and anything that looks like a key, token or password in a query string or setting.
    /// </summary>
    public static string Redact(string text)
    {
        text = Secret().Replace(text, m => m.Groups[1].Value + "=<hidden>");
        text = UncPath().Replace(text, m => "<path>" + Path.GetExtension(m.Value.TrimEnd('.', ',', ')', ';', ':')));
        text = DrivePath().Replace(text, m => "<path>" + Path.GetExtension(m.Value.TrimEnd('.', ',', ')', ';', ':')));
        var user = Environment.UserName;
        if (user.Length > 2) text = Regex.Replace(text, Regex.Escape(user), "<user>", RegexOptions.IgnoreCase);
        return text;
    }

    // C:\Users\me\Videos\Some Film (2020).mkv: names have spaces, so a path runs to its file extension, or failing that
    // to the end of the line or a quote. Not "https:" (a letter before the drive letter).
    [GeneratedRegex(@"(?<![A-Za-z])[A-Za-z]:[\\/](?:[^""'\r\n<>|]*?\.[A-Za-z0-9]{2,5}(?=[\s""',;)]|$)|[^""'\r\n<>|]*)", RegexOptions.Multiline)]
    private static partial Regex DrivePath();

    [GeneratedRegex(@"\\\\[^\\\s""']+\\(?:[^""'\r\n<>|]*?\.[A-Za-z0-9]{2,5}(?=[\s""',;)]|$)|[^""'\r\n<>|]*)", RegexOptions.Multiline)]
    private static partial Regex UncPath();

    [GeneratedRegex(@"\b(api_?key|apikey|token|access_token|password|secret|client_secret)\s*[=:]\s*[^\s&""',]+", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();
}
