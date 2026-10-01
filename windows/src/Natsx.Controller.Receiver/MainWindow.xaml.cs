using System.Windows;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime =
        new();

    private PeerId? _trustedControllerPeerId;

    public MainWindow()
    {
        InitializeComponent();

        _runtime.StatusChanged +=
            OnRuntimeStatusChanged;

        _runtime.PairingConfirmationChanged +=
            OnPairingConfirmationChanged;

        _runtime.DiagnosticsChanged +=
            OnDiagnosticsChanged;

        Loaded +=
            OnLoaded;

        Closed +=
            OnClosed;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            await _runtime.StartAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text =
                exception.Message;
        }
    }

    private void OnRuntimeStatusChanged(
        string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () =>
                    StatusText.Text =
                        status);
            return;
        }

        StatusText.Text =
            status;
    }

    private void OnDiagnosticsChanged(
        ReceiverDiagnosticsSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () =>
                    OnDiagnosticsChanged(
                        snapshot));
            return;
        }

        SmartAutoStateText.Text =
            snapshot.SmartAutoState;

        ActiveTransportText.Text =
            snapshot.ActiveTransport;

        BackupTransportsText.Text =
            snapshot.BackupTransports;

        VirtualControllerText.Text =
            snapshot.VirtualControllerStatus;

        _trustedControllerPeerId =
            snapshot.TrustedControllerPeerId;

        TrustedControllerText.Text =
            snapshot.TrustedControllerDisplay;

        TrustedControllerCountText.Text =
            $"{snapshot.TrustedControllerCount} trusted";

        ForgetControllerButton.IsEnabled =
            snapshot.TrustedControllerPeerId is not null;

        RoundTripTimeText.Text =
            FormatDuration(
                snapshot.RoundTripTime);

        JitterText.Text =
            FormatDuration(
                snapshot.Jitter);

        PacketLossText.Text =
            snapshot.PacketLossPercent is double loss
                ? $"{loss:0.0}%"
                : "—";

        InputRateText.Text =
            $"{snapshot.InputRateHz:0} Hz";

        ReconnectCountText.Text =
            snapshot.ReconnectCount
                .ToString();

        RecentHandoverText.Text =
            snapshot.RecentHandoverReason;
    }

    private static string FormatDuration(
        TimeSpan? duration)
    {
        return duration is TimeSpan value
            ? $"{value.TotalMilliseconds:0.0} ms"
            : "—";
    }

    private void OnPairingConfirmationChanged(
        PairingConfirmationPrompt? prompt)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () =>
                    OnPairingConfirmationChanged(
                        prompt));
            return;
        }

        if (prompt is null)
        {
            PairingPanel.Visibility =
                Visibility.Collapsed;
            PairingCodeText.Text =
                string.Empty;
            PairingPeerText.Text =
                string.Empty;
            return;
        }

        PairingCodeText.Text =
            prompt.ComparisonCode;

        PairingPeerText.Text =
            $"Android peer: {prompt.RemotePeerId}";

        PairingPanel.Visibility =
            Visibility.Visible;
    }

    private async void OnForgetControllerClicked(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_trustedControllerPeerId is not PeerId peerId)
        {
            return;
        }

        MessageBoxResult confirmation =
            MessageBox.Show(
                this,
                "Forget this Android controller? It will be disconnected immediately and must be paired again before it can control this PC.",
                "Forget trusted controller",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ForgetControllerButton.IsEnabled =
            false;

        try
        {
            bool forgotten =
                await _runtime
                    .ForgetTrustedControllerAsync(
                        peerId);

            if (!forgotten)
            {
                StatusText.Text =
                    "The selected controller is no longer in the trusted list.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text =
                $"Could not forget controller: {exception.Message}";
        }
    }

    private void OnPairingConfirmClicked(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _runtime.ResolvePairingConfirmation(
            true);
    }

    private void OnPairingRejectClicked(
        object sender,
        RoutedEventArgs eventArgs)
    {
        _runtime.ResolvePairingConfirmation(
            false);
    }

    private async void OnClosed(
        object? sender,
        EventArgs eventArgs)
    {
        _runtime.StatusChanged -=
            OnRuntimeStatusChanged;

        _runtime.PairingConfirmationChanged -=
            OnPairingConfirmationChanged;

        _runtime.DiagnosticsChanged -=
            OnDiagnosticsChanged;

        await _runtime
            .DisposeAsync();
    }
}
