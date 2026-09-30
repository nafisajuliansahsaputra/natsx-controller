using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiRealtimeReceiver : IAsyncDisposable
{
    public const int DefaultPort = 43860;
    public const int MaximumDatagramSize = 512;

    private readonly WifiTrustedSession _trustedSession;
    private readonly Channel<WifiGamepadDatagram> _latestState;
    private readonly TimeProvider _timeProvider;

    private UdpClient? _udpClient;
    private CancellationTokenSource? _runCancellation;
    private Task? _receiveLoop;

    private long _acceptedDatagrams;
    private long _rejectedDatagrams;
    private long _lastAcceptedTimestamp;

    public WifiRealtimeReceiver(
        WifiTrustedSession trustedSession,
        TimeProvider? timeProvider = null)
    {
        _trustedSession = trustedSession
            ?? throw new ArgumentNullException(nameof(trustedSession));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _latestState = Channel.CreateBounded<WifiGamepadDatagram>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = true,
                AllowSynchronousContinuations = false,
            });
    }

    public bool IsRunning => _receiveLoop is not null;

    public long AcceptedDatagrams => Interlocked.Read(ref _acceptedDatagrams);

    public long RejectedDatagrams => Interlocked.Read(ref _rejectedDatagrams);

    public TimeSpan Silence
    {
        get
        {
            long acceptedAt = Interlocked.Read(ref _lastAcceptedTimestamp);
            return acceptedAt == 0
                ? TimeSpan.MaxValue
                : _timeProvider.GetElapsedTime(acceptedAt, _timeProvider.GetTimestamp());
        }
    }

    public ChannelReader<WifiGamepadDatagram> States => _latestState.Reader;

    public Task StartAsync(
        IPEndPoint bindEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindEndPoint);

        if (_receiveLoop is not null)
        {
            throw new InvalidOperationException("Wi-Fi receiver is already running.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var udpClient = new UdpClient(bindEndPoint);
        var runCancellation = new CancellationTokenSource();

        _udpClient = udpClient;
        _runCancellation = runCancellation;
        _receiveLoop = ReceiveLoopAsync(udpClient, runCancellation.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? receiveLoop = _receiveLoop;
        CancellationTokenSource? cancellation = _runCancellation;
        UdpClient? udpClient = _udpClient;

        _receiveLoop = null;
        _runCancellation = null;
        _udpClient = null;

        if (receiveLoop is null)
        {
            return;
        }

        cancellation?.Cancel();
        udpClient?.Dispose();

        try
        {
            await receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _latestState.Writer.TryComplete();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveLoopAsync(
        UdpClient udpClient,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;

            try
            {
                result = await udpClient.ReceiveAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (result.Buffer.Length > MaximumDatagramSize)
            {
                Interlocked.Increment(ref _rejectedDatagrams);
                continue;
            }

            try
            {
                WifiGamepadDatagram state =
                    WifiRealtimeDatagramCodec.DecodeGamepadState(
                        result.Buffer,
                        _trustedSession);

                Interlocked.Increment(ref _acceptedDatagrams);
                Interlocked.Exchange(
                    ref _lastAcceptedTimestamp,
                    _timeProvider.GetTimestamp());

                _latestState.Writer.TryWrite(state);
            }
            catch (Exception exception) when (
                exception is FormatException or
                CryptographicException or
                ArgumentException)
            {
                Interlocked.Increment(ref _rejectedDatagrams);
            }
        }
    }
}
