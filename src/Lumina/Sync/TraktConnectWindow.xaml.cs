using System.Net.Http;
using System.Diagnostics;
using System.Windows;

namespace Lumina.Sync;

/// <summary>Shows the device code, opens trakt.tv/activate, and closes with DialogResult true once approved.</summary>
public partial class TraktConnectWindow : Window
{
    readonly CancellationTokenSource _cts = new();

    public TraktConnectWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Run();
        Closed += (_, _) => _cts.Cancel();
    }

    async Task Run()
    {
        try
        {
            var d = await Trakt.StartDeviceAuth();
            Prompt.Text = $"Enter this code at {d.Url}";
            Code.Text = d.UserCode;
            CodeBox.Visibility = Visibility.Visible;
            Note.Text = "Your browser should open on that page. Lumina connects as soon as you approve.";
            // Only ever open Trakt itself, whatever the response said.
            if (Uri.TryCreate(d.Url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host is "trakt.tv" or "www.trakt.tv")
                Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true });

            if (await Trakt.WaitForDeviceAuth(d, _cts.Token)) DialogResult = true;
            else if (!_cts.IsCancellationRequested) { Prompt.Text = "The code expired or was denied."; CodeBox.Visibility = Visibility.Collapsed; Note.Text = "Close this window and try again."; }
        }
        catch (TraktException e) { Prompt.Text = e.Message; Code.Text = ""; }
        catch (HttpRequestException) { Prompt.Text = "Trakt unreachable."; Code.Text = ""; }
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close();
}
