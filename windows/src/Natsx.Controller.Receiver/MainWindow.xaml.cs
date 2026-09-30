using System.Windows;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime = new();
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        _runtime.StatusChanged += OnRuntimeStatusChanged;
        LocalPeerValue.Text = _runtime.LocalPeerId.ToString();
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await _runtime.StartAsync();
        }
        catch (Exception exception)
        {
            RenderFault(exception);
        }
    }

    private async void OnClosed(
        object? sender,
        EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _runtime.StatusChanged -= OnRuntimeStatusChanged;

        try
        {
            await _runtime.DisposeAsync();
        }
        catch
        {
            // Window is already closing. Runtime cleanup is best-effort here;
            // gameplay state is neutralized before backend disposal.
        }
    }

    private void OnRuntimeStatusChanged(
        object? sender,
        ReceiverRuntimeStatus status)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(
                () => RenderStatus(status));
            return;
        }

        RenderStatus(status);
    }

    private void RenderStatus(
        ReceiverRuntimeStatus status)
    {
        StatusTitle.Text = status.State switch
        {
            ReceiverRuntimeState.Starting =>
                "Starting receiver",
            ReceiverRuntimeState.WaitingForController =>
                "Ready for controller",
            ReceiverRuntimeState.Connected =>
                "Controller connected",
            ReceiverRuntimeState.Stopping =>
                "Stopping",
            ReceiverRuntimeState.Faulted =>
                "Receiver error",
            _ =>
                "Receiver stopped",
        };

        StatusDetail.Text = status.Detail;

        VirtualControllerValue.Text =
            status.VirtualControllerReady
                ? "Xbox 360 compatible — ready"
                : "Not ready";

        ConnectionValue.Text =
            status.ConnectionState.ToString();

        RemotePeerValue.Text =
            status.RemotePeerId?.ToString() ?? "-";

        TransportValue.Text =
            status.ActiveTransport?.ToString() ?? "-";

        if (status.WifiHealth is { } wifi)
        {
            RttValue.Text =
                FormatMilliseconds(wifi.RoundTripTime);
            JitterValue.Text =
                FormatMilliseconds(wifi.Jitter);
            LossValue.Text =
                $"{wifi.PacketLossPercent:0.00}%";
        }
        else
        {
            RttValue.Text = "-";
            JitterValue.Text = "-";
            LossValue.Text = "-";
        }
    }

    private void RenderFault(Exception exception)
    {
        StatusTitle.Text = "Receiver failed to start";
        StatusDetail.Text = exception.Message;
        VirtualControllerValue.Text = "Not ready";
        ConnectionValue.Text = "Faulted";
    }

    private static string FormatMilliseconds(
        TimeSpan value)
    {
        if (value == TimeSpan.MaxValue)
        {
            return "-";
        }

        return $"{value.TotalMilliseconds:0.0} ms";
    }
}
