using System.Windows;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime =
        new();

    public MainWindow()
    {
        InitializeComponent();

        _runtime.StatusChanged +=
            OnRuntimeStatusChanged;

        _runtime.PairingConfirmationChanged +=
            OnPairingConfirmationChanged;

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

        await _runtime
            .DisposeAsync();
    }
}
