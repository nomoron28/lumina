using System.IO;
using Lumina.Player;
using Xunit;

namespace Lumina.Tests;

public class ResumeTest
{
    // Needs libmpv-2.dll (third_party/libmpv) and ffmpeg-made test file; skips quietly when either is absent.
    [Fact]
    public void LoadWithStartSeeksThere()
    {
        var clip = Path.Combine(AppContext.BaseDirectory, "resume.mkv");
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"))) return;
        var ff = @"C:\ffmpeg\bin\ffmpeg.exe";
        if (!File.Exists(ff)) return;
        using (var p = System.Diagnostics.Process.Start(ff,
            $"-loglevel error -y -f lavfi -i testsrc=size=320x180:rate=25 -t 30 -c:v libx264 -preset ultrafast \"{clip}\""))
            p!.WaitForExit();

        using var m = new Mpv(IntPtr.Zero, AppContext.BaseDirectory, "vo=null\nao=null");
        m.Load(clip, 12.5);
        double pos = 0;
        for (int i = 0; i < 50 && pos < 12; i++)
        {
            Thread.Sleep(100);
            double.TryParse(m.Get("time-pos"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out pos);
        }
        Assert.InRange(pos, 12.4, 14.5);
    }
}
