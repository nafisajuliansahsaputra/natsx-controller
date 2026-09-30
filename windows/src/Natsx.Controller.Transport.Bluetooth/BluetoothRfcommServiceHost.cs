using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace Natsx.Controller.Transport.Bluetooth;

public sealed class BluetoothRfcommServiceHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private RfcommServiceProvider? _provider;
    private StreamSocketListener? _listener;
    private bool _disposed;

    public event EventHandler<BluetoothRfcommConnectionEventArgs>?
        ConnectionReceived;

    public bool IsRunning =>
        _provider is not null &&
        _listener is not null;

    public async ValueTask StartAsync(
        bool radioDiscoverable = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (IsRunning)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            RfcommServiceId serviceId =
                RfcommServiceId.FromUuid(
                    BluetoothRfcommConstants.ServiceUuid);

            RfcommServiceProvider provider =
                await RfcommServiceProvider.CreateAsync(serviceId);

            var listener = new StreamSocketListener();
            listener.ConnectionReceived += OnConnectionReceived;

            try
            {
                await listener.BindServiceNameAsync(
                    provider.ServiceId.AsString(),
                    SocketProtectionLevel
                        .BluetoothEncryptionAllowNullAuthentication);

                cancellationToken.ThrowIfCancellationRequested();

                provider.StartAdvertising(
                    listener,
                    radioDiscoverable);

                _provider = provider;
                _listener = listener;
            }
            catch
            {
                listener.ConnectionReceived -= OnConnectionReceived;
                listener.Dispose();
                provider.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask StopAsync()
    {
        await _lifecycleGate
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            RfcommServiceProvider? provider = _provider;
            StreamSocketListener? listener = _listener;

            _provider = null;
            _listener = null;

            if (provider is null &&
                listener is null)
            {
                return;
            }

            if (provider is not null)
            {
                try
                {
                    provider.StopAdvertising();
                }
                finally
                {
                    provider.Dispose();
                }
            }

            if (listener is not null)
            {
                listener.ConnectionReceived -= OnConnectionReceived;
                listener.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void OnConnectionReceived(
        StreamSocketListener sender,
        StreamSocketListenerConnectionReceivedEventArgs eventArgs)
    {
        EventHandler<BluetoothRfcommConnectionEventArgs>? handler =
            ConnectionReceived;

        if (handler is null)
        {
            eventArgs.Socket.Dispose();
            return;
        }

        handler(
            this,
            new BluetoothRfcommConnectionEventArgs(
                eventArgs.Socket));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
