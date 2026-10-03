using System.ComponentModel;
using System.IO;
using System.Windows;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime =
        new();
    private readonly ReceiverUserSettings _settings =
        ReceiverUserSettings.Load();
    private readonly ReceiverTrayIcon _trayIcon;

    private PeerId? _trustedControllerPeerId;
    private bool _settingsInitialized;
    private bool _isStarting;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        string version = typeof(MainWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .Single().InformationalVersion.Split('+')[0];
        Title = $"NATSX Controller Receiver {version}";

        _trayIcon =
            new ReceiverTrayIcon();

        _trayIcon.OpenRequested +=
            OnTrayOpenRequested;
        _trayIcon.ExitRequested +=
            OnTrayExitRequested;

        _runtime.StatusChanged +=
            OnRuntimeStatusChanged;

        _runtime.PairingConfirmationChanged +=
            OnPairingConfirmationChanged;

        _runtime.DiagnosticsChanged +=
            OnDiagnosticsChanged;

        StateChanged +=
            OnWindowStateChanged;

        Closing +=
            OnClosing;

        Closed +=
            OnClosed;

        ApplySettingsToUi();
    }

    public async Task StartReceiverAsync()
    {
        if (_runtime.IsStarted ||
            _isStarting)
        {
            return;
        }

        _isStarting =
            true;

        System.Windows.Automation.AutomationProperties.SetItemStatus(this, "Starting");

        RetryReceiverButton.IsEnabled =
            false;

        ErrorPanel.Visibility =
            Visibility.Collapsed;

        try
        {
            await _runtime.StartAsync();

            _trayIcon.UpdateStatus(
                "Receiver ready");

            StatusText.Text =
                "Receiver ready. Smart Auto is waiting for trusted controller input.";

            // Diagnostic messages can replace StatusText immediately. Expose
            // completion of the actual startup separately to accessibility
            // clients and the installed-Receiver launch verification.
            System.Windows.Automation.AutomationProperties.SetItemStatus(this, "Ready");
        }
        catch (Exception exception)
        {
            System.Windows.Automation.AutomationProperties.SetItemStatus(this, "Failed");
            string message =
                DescribeStartupFailure(
                    exception);

            StatusText.Text =
                message;

            ErrorText.Text =
                message;

            ErrorPanel.Visibility =
                Visibility.Visible;

            _trayIcon.ShowError(
                message);
        }
        finally
        {
            RetryReceiverButton.IsEnabled =
                true;

            _isStarting =
                false;
        }
    }

    private void ApplySettingsToUi()
    {
        _settingsInitialized =
            false;

        StartWithWindowsCheckBox.IsChecked =
            _settings.StartWithWindows;

        CloseToTrayCheckBox.IsChecked =
            _settings.CloseToTray;

        _settingsInitialized =
            true;
    }

    private void OnRuntimeStatusChanged(
        string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () =>
                    OnRuntimeStatusChanged(
                        status));
            return;
        }

        StatusText.Text =
            status;

        _trayIcon.UpdateStatus(
            status);
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

        _trayIcon.UpdateStatus(
            snapshot.ActiveTransport == "None"
                ? $"Smart Auto: {snapshot.SmartAutoState}"
                : $"Active: {snapshot.ActiveTransport}");
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

        ShowAndActivate();

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

        System.Windows.MessageBoxResult confirmation =
            System.Windows.MessageBox.Show(
                this,
                "Forget this Android controller? It will be disconnected immediately and must be paired again before it can control this PC.",
                "Forget trusted controller",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (confirmation !=
            System.Windows.MessageBoxResult.Yes)
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

    private async void OnRetryReceiverClicked(
        object sender,
        RoutedEventArgs eventArgs)
    {
        await StartReceiverAsync();
    }

    private void OnStartWithWindowsChanged(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_settingsInitialized)
        {
            return;
        }

        try
        {
            _settings.SetStartWithWindows(
                StartWithWindowsCheckBox.IsChecked ==
                true);
        }
        catch (Exception exception)
        {
            _settingsInitialized =
                false;
            StartWithWindowsCheckBox.IsChecked =
                _settings.StartWithWindows;
            _settingsInitialized =
                true;

            ShowSettingsError(
                $"Could not update Windows startup: {exception.Message}");
        }
    }

    private void OnCloseToTrayChanged(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (!_settingsInitialized)
        {
            return;
        }

        try
        {
            _settings.SetCloseToTray(
                CloseToTrayCheckBox.IsChecked ==
                true);
        }
        catch (Exception exception)
        {
            ShowSettingsError(
                $"Could not save tray preference: {exception.Message}");
        }
    }

    private void ShowSettingsError(
        string message)
    {
        ErrorText.Text =
            message;
        ErrorPanel.Visibility =
            Visibility.Visible;
    }

    private void OnWindowStateChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (WindowState !=
            WindowState.Minimized)
        {
            return;
        }

        Hide();
        WindowState =
            WindowState.Normal;
    }

    private void OnClosing(
        object? sender,
        CancelEventArgs eventArgs)
    {
        if (_exitRequested ||
            !_settings.CloseToTray)
        {
            return;
        }

        eventArgs.Cancel =
            true;

        Hide();
    }

    private void OnTrayOpenRequested()
    {
        Dispatcher.Invoke(
            ShowAndActivate);
    }

    private void OnTrayExitRequested()
    {
        Dispatcher.Invoke(
            ExitReceiver);
    }

    public void ExitReceiver()
    {
        _exitRequested = true;
        Close();
    }

    public void ShowAndActivate()
    {
        Show();

        if (WindowState ==
            WindowState.Minimized)
        {
            WindowState =
                WindowState.Normal;
        }

        Activate();
        Topmost =
            true;
        Topmost =
            false;
        Focus();
    }

    private static string DescribeStartupFailure(
        Exception exception)
    {
        if (exception is UnauthorizedAccessException ||
            exception is Win32Exception
            {
                NativeErrorCode: 5
            })
        {
            return "The virtual-controller driver could not be accessed. Run the NATSX installer or Receiver as Administrator once so the driver can be installed, then retry.";
        }

        if (exception is FileNotFoundException ||
            exception is DllNotFoundException ||
            exception is BadImageFormatException)
        {
            return "A required NATSX/HIDMaestro runtime component is missing or invalid. Repair or reinstall NATSX Controller, then retry.";
        }

        if (exception.Message.Contains(
                "HIDMaestro",
                StringComparison.OrdinalIgnoreCase))
        {
            return $"Virtual Xbox controller backend is unavailable: {exception.Message}";
        }

        return $"Receiver could not start: {exception.Message}";
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

        _trayIcon.OpenRequested -=
            OnTrayOpenRequested;
        _trayIcon.ExitRequested -=
            OnTrayExitRequested;

        _trayIcon.Dispose();

        await _runtime
            .DisposeAsync();

        System.Windows.Application.Current
            .Shutdown();
    }
}
