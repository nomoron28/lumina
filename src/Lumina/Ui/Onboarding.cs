using System.Windows;
using Lumina.Settings;

namespace Lumina;

/// <summary>The welcome shown on first start: the logo, one line and three steps. Skippable, and it can be shown again from Settings.</summary>
public static class Onboarding
{
    public sealed record Step(string Title, string Body);

    public static readonly IReadOnlyList<Step> Steps = new Step[]
    {
        new("Point Lumina at your videos", "Choose the folder, or the NAS share, where your films and shows live. Lumina only reads it: no file is ever moved, renamed or uploaded."),
        new("Let it dress the library", "Posters, summaries and cast come from TMDB, and subtitle search from OpenSubtitles. Both are free and optional: add your keys later in Settings, Accounts."),
        new("Make it yours", "Pick how Lumina looks. Everything else, including profiles like Movies, Anime and Night, and every keyboard shortcut, is in Settings."),
    };

    /// <summary>Show the welcome, then remember it was shown. Closing it any way counts as done. pickFolder asks for a folder, adds it and returns its path (null if cancelled).</summary>
    public static void Show(Window owner, Func<string?> pickFolder)
    {
        try { new WelcomeWindow(owner, pickFolder).ShowDialog(); }
        finally { AppSettings.Set("general.tour", "false"); }
    }
}
