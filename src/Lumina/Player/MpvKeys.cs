using System.Windows.Input;

namespace Lumina.Player;

/// <summary>
/// Maps WPF key events to mpv key names for the keypress command.
/// Printable characters come from TextInput (so keyboard layouts just work); everything else from KeyDown.
/// </summary>
public static class MpvKeys
{
    static readonly Dictionary<Key, string> Named = new()
    {
        [Key.Left] = "LEFT", [Key.Right] = "RIGHT", [Key.Up] = "UP", [Key.Down] = "DOWN",
        [Key.Enter] = "ENTER", [Key.Escape] = "ESC", [Key.Back] = "BS", [Key.Delete] = "DEL", [Key.Insert] = "INS",
        [Key.Home] = "HOME", [Key.End] = "END", [Key.PageUp] = "PGUP", [Key.PageDown] = "PGDWN", [Key.Tab] = "TAB",
        [Key.MediaPlayPause] = "PLAYPAUSE", [Key.MediaStop] = "STOP", [Key.MediaNextTrack] = "NEXT", [Key.MediaPreviousTrack] = "PREV",
        [Key.VolumeUp] = "VOLUME_UP", [Key.VolumeDown] = "VOLUME_DOWN", [Key.VolumeMute] = "MUTE",
    };

    /// <summary>mpv name for a non-printable key or a Ctrl+letter shortcut; null when TextInput should handle it.</summary>
    public static string? Name(Key key, ModifierKeys mods)
    {
        // Alt combinations belong to Windows (Alt+F4, Alt+Space).
        if (mods.HasFlag(ModifierKeys.Alt) || mods.HasFlag(ModifierKeys.Windows)) return null;

        string? baseName = null;
        if (Named.TryGetValue(key, out var n)) baseName = n;
        else if (key is >= Key.F1 and <= Key.F12) baseName = "F" + (key - Key.F1 + 1);
        else if (mods.HasFlag(ModifierKeys.Control) && key is >= Key.A and <= Key.Z) baseName = ((char)('a' + (key - Key.A))).ToString();
        else if (mods.HasFlag(ModifierKeys.Control) && key is >= Key.D0 and <= Key.D9) baseName = ((char)('0' + (key - Key.D0))).ToString();
        if (baseName == null) return null;

        var prefix = (mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "");
        return prefix + baseName;
    }

    /// <summary>mpv name for a typed character, or null for control characters and anything that is not one character.</summary>
    public static string? Char(string? text)
    {
        if (text is not { Length: 1 } || char.IsControl(text[0]) || char.IsSurrogate(text[0])) return null;
        return text[0] switch { ' ' => "SPACE", '#' => "SHARP", var c => c.ToString() };
    }
}
