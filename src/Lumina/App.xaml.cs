using System.IO;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Lumina;


public partial class App : Application
{
    /// <summary>What this launch was asked to open. Read by the main window.</summary>
    public static Lumina.Windows.LaunchArgs Launch { get; private set; } = Lumina.Windows.LaunchArgs.None;

    Lumina.Windows.SingleInstance? _instance;

    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }

    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF-UI draws a few controls itself (title bar buttons, password fields). Give it Lumina's accent, not Windows blue.
        Lumina.Themes.ThemeManager.Apply();
        SmoothScroll.Install();
        Lumina.Themes.ThemeManager.WatchSystem();
        Lumina.Diagnostics.Log.Install(this);
        Lumina.Ui.AppFonts.EnsureOnDisk();      // the player's controller reads the typeface from a folder
        Launch = Lumina.Windows.LaunchArgs.Parse(e.Args, Environment.CurrentDirectory);
        if (Launch.Action != null) { Shutdown(Lumina.Windows.Maintenance.Run(Launch.Action)); return; }
        if (Lumina.Settings.AppSettings.Bool("general.singleInstance"))
        {
            _instance = new Lumina.Windows.SingleInstance();
            if (!_instance.IsFirst)
            {
                if (Lumina.Windows.SingleInstance.Send(Launch.ToArgs())) { Shutdown(0); return; }
                Lumina.Diagnostics.Log.Info("Another Lumina holds the instance lock but did not answer; starting a second window");
                _instance.Dispose(); _instance = null;
            }
            else
            {
                _instance.Received += forwarded => Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow w) w.OpenFromLaunch(Lumina.Windows.LaunchArgs.Parse(forwarded));
                });
                _instance.Listen();
            }
        }
        // a short splash while the library loads; not when Windows starts Lumina quietly at sign-in
        if (!Launch.Background)
        {
            try { new SplashScreen("Assets/Brand/splash.png").Show(autoClose: true, topMost: false); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { Lumina.Diagnostics.Log.Error("Could not show the splash", ex); }
        }
        base.OnStartup(e);
    }
}
