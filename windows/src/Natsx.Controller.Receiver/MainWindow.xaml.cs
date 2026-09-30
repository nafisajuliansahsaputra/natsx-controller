using System.Windows;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime;

    public MainWindow()
    {
        InitializeComponent();

        var identityStore = new ReceiverIdentityStore();
        var trustedPeers = new WindowsTrustedPeerStore();

        _runtime = new ReceiverRuntime(
            identityStore.GetOrCreate(),
            trustedPeers);

        _runtime.StatusChanged += OnRuntimeStatusChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _runtime.StartAsync();
        }
        catch (Exception exception)
        {
            ApplyStatus(
                new ReceiverRuntimeStatus(
                    Message: "Receiver startup failed: " + exception.Message,
                    VirtualControllerReady: false,
                    WifiListening: false,
                    SessionActive: false));
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _runtime.StatusChanged -= OnRuntimeStatusChanged;

        try
        {
            await _runtime.DisposeAsync();
        }
        catch
        {
            // Window is already closing. Runtime cleanup is best effort here.
        }
    }

    private void OnRuntimeStatusChanged(ReceiverRuntimeStatus status)
    {
        Dispatcher.InvokeAsync(() => ApplyStatus(status));
    }

    private void ApplyStatus(ReceiverRuntimeStatus status)
    {
        StatusText.Text = status.Message;
        VirtualControllerText.Text =
            status.VirtualControllerReady ? "Ready" : "Unavailable";
        WifiText.Text =
            status.WifiListening ? "Listening" : "Stopped";
        SessionText.Text =
            status.SessionActive ? "Active" : "Waiting";

        if (status.WifiHealth is { } health)
        {
            DiagnosticsText.Text =
                "RTT " + FormatMilliseconds(health.RoundTripTime) +
                "   Jitter " + FormatMilliseconds(health.Jitter) +
                "   Loss " + health.PacketLossPercent.ToString("0.0") + "%" +
                "   Health " + health.Grade;
        }
        else
        {
            DiagnosticsText.Text =
                "RTT --   Jitter --   Loss --   Health --";
        }
    }

    private static string FormatMilliseconds(TimeSpan value)
    {
        if (value == TimeSpan.Zero || value == TimeSpan.MaxValue)
            return "--";

        return value.TotalMilliseconds.ToString("0.0") + " ms";
    }
}
