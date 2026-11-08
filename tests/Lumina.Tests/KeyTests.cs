using System.IO;
using System.Windows.Input;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class MpvKeysTests
{
    [Theory]
    [InlineData(Key.Left, ModifierKeys.None, "LEFT")]
    [InlineData(Key.Left, ModifierKeys.Shift, "Shift+LEFT")]
    [InlineData(Key.Right, ModifierKeys.Control, "Ctrl+RIGHT")]
    [InlineData(Key.Enter, ModifierKeys.None, "ENTER")]
    [InlineData(Key.Escape, ModifierKeys.None, "ESC")]
    [InlineData(Key.PageDown, ModifierKeys.None, "PGDWN")]
    [InlineData(Key.F1, ModifierKeys.None, "F1")]
    [InlineData(Key.F12, ModifierKeys.None, "F12")]
    [InlineData(Key.MediaPlayPause, ModifierKeys.None, "PLAYPAUSE")]
    [InlineData(Key.VolumeUp, ModifierKeys.None, "VOLUME_UP")]
    [InlineData(Key.S, ModifierKeys.Control, "Ctrl+s")]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+s")]
    [InlineData(Key.D5, ModifierKeys.Control, "Ctrl+5")]
    public void NamesKeysMpvUnderstands(Key key, ModifierKeys mods, string expected) =>
        Assert.Equal(expected, MpvKeys.Name(key, mods));

    [Theory]
    [InlineData(Key.B, ModifierKeys.None)]          // plain letters come from TextInput, not KeyDown
    [InlineData(Key.B, ModifierKeys.Shift)]
    [InlineData(Key.Space, ModifierKeys.None)]
    [InlineData(Key.F4, ModifierKeys.Alt)]          // Alt+F4 stays with Windows
    [InlineData(Key.Left, ModifierKeys.Windows)]
    [InlineData(Key.LeftShift, ModifierKeys.Shift)] // a bare modifier is not a key press
    public void LeavesTheRestAlone(Key key, ModifierKeys mods) => Assert.Null(MpvKeys.Name(key, mods));

    [Theory]
    [InlineData("b", "b")]
    [InlineData("B", "B")]
    [InlineData(" ", "SPACE")]
    [InlineData("#", "SHARP")]
    [InlineData("]", "]")]
    [InlineData("é", "é")]
    public void NamesTypedCharacters(string text, string expected) => Assert.Equal(expected, MpvKeys.Char(text));

    [Theory]
    [InlineData("\r")]
    [InlineData("\b")]
    [InlineData("\u001b")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ab")]
    public void IgnoresControlCharactersAndMultiCharInput(string? text) => Assert.Null(MpvKeys.Char(text));
}

[Collection("db")]
public class KeyForwardingTests
{
    // Real libmpv: a forwarded B must reach Lumina through input.conf, and a forwarded q must quit the player.
    [Fact]
    public void ForwardedBFiresSubtitleMessageAndQQuits()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"))) return;
        var ff = @"C:\ffmpeg\bin\ffmpeg.exe";
        if (!File.Exists(ff)) return;

        var dir = Path.Combine(Path.GetTempPath(), "lumina-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var clip = Path.Combine(dir, "clip.mkv");
        using (var proc = System.Diagnostics.Process.Start(ff,
            $"-loglevel error -y -f lavfi -i testsrc=size=320x180:rate=25 -t 10 -c:v libx264 -preset ultrafast \"{clip}\""))
            proc!.WaitForExit();
        try
        {
            using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null");
            string[]? msg = null;
            var loaded = new ManualResetEventSlim();
            var ended = new ManualResetEventSlim();
            m.ClientMessage += a => msg = a;
            m.FileLoaded += () => loaded.Set();
            m.Ended += why => { if (why == "shutdown") ended.Set(); };
            m.Load(clip);
            Assert.True(loaded.Wait(5000));

            m.Command("keypress", "Ctrl+b");
            for (int i = 0; i < 50 && msg == null; i++) Thread.Sleep(100);
            Assert.Equal(new[] { "lumina-subs" }, msg);

            m.Command("keypress", MpvKeys.Char("q")!);
            Assert.True(ended.Wait(5000), "q did not quit mpv");
        }
        finally { Directory.Delete(dir, true); }
    }
}
