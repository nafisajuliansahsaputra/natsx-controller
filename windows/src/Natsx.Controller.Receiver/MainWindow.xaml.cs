using System.Windows;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime;
    private readonly ReceiverPairingServer _pairingServer;

    public MainWindow()
    {
        InitializeComponent();

        var identityStore = new ReceiverIdentityStore();
        var trustedPeers = new WindowsTrustedPeerStore();
        var windowsDeviceId = identityStore.GetOrCreate();

        _runtime = new ReceiverRuntime(
            windowsDeviceId,
            trustedPeers);

        _pairingServer = new ReceiverPairingServer(
            windowsDeviceId,
            trustedPeers);

        _runtime.StatusChanged += OnRuntimeStatusChanged;
        _pairingServer.PromptReady += OnPairingPromptReady;
        _pairingServer.PairingCompleted += OnPairingCompleted;
        _pairingServer.PairingFailed += OnPairingFailed;

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
        _pairingServer.PromptReady -= OnPairingPromptReady;
        _pairingServer.PairingCompleted -= OnPairingCompleted;
        _pairingServer.PairingFailed -= OnPairingFailed;

        try
        {
            await _pairingServer.DisposeAsync();
        }
        catch
        {
        }

        try
        {
            await _runtime.DisposeAsync();
        }
        catch
        {
            // Window is already closing. Runtime cleanup is best effort here.
        }
    }

    private void StartPairingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _pairingServer.StartPairing();

            StartPairingButton.IsEnabled = false;
            ConfirmPairingButton.IsEnabled = false;
            CancelPairingButton.IsEnabled = true;
            PairingCodeText.Text = "------";
            PairingInfoText.Text =
                "Waiting for the Android phone on the local network…";
        }
        catch (Exception exception)
        {
            ResetPairingUi(
                "Could not start pairing: " + exception.Message);
        }
    }

    private void ConfirmPairingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        ConfirmPairingButton.IsEnabled = false;
        PairingInfoText.Text =
            "Code confirmed on this PC. Waiting for the phone…";
        _pairingServer.Confirm();
    }

    private void CancelPairingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _pairingServer.Cancel();
        ResetPairingUi("Pairing cancelled.");
    }

    private void OnPairingPromptReady(PairingPrompt prompt)
    {
        Dispatcher.InvokeAsync(() =>
        {
            PairingCodeText.Text = prompt.SasCode;
            PairingInfoText.Text =
                "Phone: " +
                (string.IsNullOrWhiteSpace(prompt.AndroidName)
                    ? "Android device"
                    : prompt.AndroidName) +
                ". Confirm only if this exact code is shown on the phone.";

            ConfirmPairingButton.IsEnabled = true;
            CancelPairingButton.IsEnabled = true;
        });
    }

    private void OnPairingCompleted(
        Natsx.Controller.Protocol.SessionId deviceId,
        string androidName)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ResetPairingUi(
                "Paired successfully with " +
                (string.IsNullOrWhiteSpace(androidName)
                    ? "Android device."
                    : androidName + ".") +
                " The controller can now reconnect automatically.");
        });
    }

    private void OnPairingFailed(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ResetPairingUi(message);
        });
    }

    private void ResetPairingUi(string message)
    {
        PairingInfoText.Text = message;
        PairingCodeText.Text = "------";
        StartPairingButton.IsEnabled = true;
        ConfirmPairingButton.IsEnabled = false;
        CancelPairingButton.IsEnabled = false;
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
