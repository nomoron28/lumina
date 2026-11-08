namespace Lumina.Settings;

public enum Kind
{
    Toggle,      // ON / OFF pill
    Choice,      // dropdown
    Segmented,   // two to four choices side by side
    Slider,      // number with a track, ticks and a readout
    Text,        // single line
    Secret,      // single line with a reveal button
    Multiline,   // free text (extra mpv options)
    Color,       // hex value with a swatch
    Swatches,    // pick one of the listed colours
    Button,      // an action, not a stored value
}

public record Choice(string Value, string Label)
{
    // UI Automation (screen readers, tests) names a dropdown item by ToString().
    public override string ToString() => Label;
}

/// <summary>A sidebar entry on the settings page. Group is App, Media or System.</summary>
public record Category(string Id, string Group, string Title, string Heading, string Symbol);

/// <summary>
/// One row on the settings page. Key is the config.json key. Default is what applies when the key is absent.
/// Mpv turns the stored value into mpv option lines; it only runs for values the user has changed,
/// so the shipped mpv.conf stays the baseline.
/// </summary>
public sealed record SettingDef(
    string Key, string Category, string Section, string Label, Kind Kind, string Default,
    string? Help = null,
    IReadOnlyList<Choice>? Choices = null,
    double Min = 0, double Max = 100, double Step = 1, string Unit = "",
    Func<string, string[]>? Mpv = null,
    string? Placeholder = null,
    string? Action = null)
{
    public bool IsNumeric => Kind == Kind.Slider;
}
