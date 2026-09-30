using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver;

public sealed record PairingPrompt(
    string SasCode,
    string AndroidName,
    SessionId AndroidDeviceId);

public sealed class ReceiverPairingServer : IAsyncDisposable
{
    public const int PairingPort = 37072;
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(60);

    private readonly SessionId _windowsDeviceId;
    private readonly WindowsTrustedPeerStore _trustedPeers;
    private readonly string _receiverName;
    private readonly object _gate = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private TaskCompletionSource<bool>? _localDecision;

    public ReceiverPairingServer(
        SessionId windowsDeviceId,
        WindowsTrustedPeerStore trustedPeers,
        string? receiverName = null)
    {
        _windowsDeviceId = windowsDeviceId;
        _trustedPeers = trustedPeers;
        _receiverName = string.IsNullOrWhiteSpace(receiverName)
            ? Environment.MachineName
            : receiverName;
    }

    public event Action<PairingPrompt>? PromptReady;
    public event Action<SessionId, string>? PairingCompleted;
    public event Action<string>? PairingFailed;

    public bool IsPairing
    {
        get
        {
            lock (_gate)
                return _listener is not null;
        }
    }

    public void StartPairing()
    {
        lock (_gate)
        {
            if (_listener is not null)
                return;

            _cts = new CancellationTokenSource(PairingTimeout);
            _listener = new TcpListener(IPAddress.Any, PairingPort);
            _listener.Start(backlog: 1);
            _runTask = RunAsync(_listener, _cts.Token);
        }
    }

    public void Confirm()
    {
        lock (_gate)
            _localDecision?.TrySetResult(true);
    }

    public void Cancel()
    {
        TaskCompletionSource<bool>? decision;

        lock (_gate)
        {
            decision = _localDecision;
            _cts?.Cancel();
        }

        decision?.TrySetResult(false);
    }

    public async ValueTask StopAsync()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? runTask;

        lock (_gate)
        {
            listener = _listener;
            cts = _cts;
            runTask = _runTask;
            _listener = null;
            _cts = null;
            _runTask = null;
            _localDecision?.TrySetResult(false);
            _localDecision = null;
        }

        if (cts is not null)
            await cts.CancelAsync();

        listener?.Stop();

        if (runTask is not null &&
            Task.CurrentId != runTask.Id)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
        }

        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task RunAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        try
        {
            using TcpClient client =
                await listener.AcceptTcpClientAsync(
                    cancellationToken);

            client.NoDelay = true;
            await ProcessClientAsync(
                client,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            PairingFailed?.Invoke("Pairing cancelled or timed out.");
        }
        catch (Exception exception) when (
            exception is IOException or
            SocketException or
            FormatException or
            CryptographicException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            PairingFailed?.Invoke(
                "Pairing failed: " + exception.Message);
        }
        finally
        {
            lock (_gate)
            {
                _listener?.Stop();
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _runTask = null;
                _localDecision = null;
            }
        }
    }

    private async Task ProcessClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        NetworkStream stream = client.GetStream();

        byte[] requestBytes =
            await ReadMessageAsync(
                stream,
                cancellationToken);

        PairingHelloPayload request =
            PairingCodec.DecodeRequest(requestBytes);

        using ECDiffieHellman key =
            PairingCrypto.CreateEphemeralKey();

        byte[] responseBytes =
            PairingCodec.EncodeResponse(
                new PairingHelloPayload(
                    _windowsDeviceId,
                    PairingCrypto.ExportPublicKey(key),
                    _receiverName));

        await WriteMessageAsync(
            stream,
            responseBytes,
            cancellationToken);

        byte[] transcriptHash =
            PairingCrypto.ComputeTranscriptHash(
                requestBytes,
                responseBytes);

        byte[] pairingRootKey =
            PairingCrypto.DerivePairingRootKey(
                key,
                request.PublicKeyDer,
                transcriptHash);

        try
        {
            string sas = PairingCrypto.ComputeSas(
                pairingRootKey,
                transcriptHash);

            var decision =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_gate)
                _localDecision = decision;

            PromptReady?.Invoke(
                new PairingPrompt(
                    sas,
                    request.DisplayName,
                    request.DeviceId));

            bool confirmed =
                await decision.Task.WaitAsync(
                    cancellationToken);

            if (!confirmed)
            {
                await WriteMessageAsync(
                    stream,
                    PairingCodec.EncodeCancel(1),
                    cancellationToken);
                return;
            }

            byte[] incomingBytes =
                await ReadMessageAsync(
                    stream,
                    cancellationToken);

            PairingMessageType incomingType =
                PairingCodec.PeekType(incomingBytes);

            if (incomingType == PairingMessageType.PairCancel)
                return;

            PairingConfirmPayload androidConfirm =
                PairingCodec.DecodeConfirm(incomingBytes);

            if (androidConfirm.Role != PairingRole.Android)
            {
                throw new UnauthorizedAccessException(
                    "Pairing confirmation role is invalid.");
            }

            byte[] expectedAndroidTag =
                PairingCrypto.ComputeConfirmationTag(
                    PairingRole.Android,
                    pairingRootKey,
                    transcriptHash);

            try
            {
                if (!PairingCrypto.VerifyTag(
                        expectedAndroidTag,
                        androidConfirm.Tag))
                {
                    throw new UnauthorizedAccessException(
                        "Android pairing confirmation failed.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    expectedAndroidTag);
                CryptographicOperations.ZeroMemory(
                    androidConfirm.Tag);
            }

            byte[] windowsTag =
                PairingCrypto.ComputeConfirmationTag(
                    PairingRole.Windows,
                    pairingRootKey,
                    transcriptHash);

            try
            {
                await WriteMessageAsync(
                    stream,
                    PairingCodec.EncodeConfirm(
                        new PairingConfirmPayload(
                            PairingRole.Windows,
                            windowsTag)),
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(windowsTag);
            }

            _trustedPeers.Save(
                request.DeviceId,
                pairingRootKey);

            byte[] completeTag =
                PairingCrypto.ComputeCompletionTag(
                    pairingRootKey,
                    transcriptHash);

            try
            {
                await WriteMessageAsync(
                    stream,
                    PairingCodec.EncodeComplete(
                        completeTag),
                    cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    completeTag);
            }

            PairingCompleted?.Invoke(
                request.DeviceId,
                request.DisplayName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                pairingRootKey);
            CryptographicOperations.ZeroMemory(
                transcriptHash);
            CryptographicOperations.ZeroMemory(
                request.PublicKeyDer);
        }
    }

    private static async Task<byte[]> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[4];

        await stream.ReadExactlyAsync(
            prefix,
            cancellationToken);

        uint length =
            BinaryPrimitives.ReadUInt32LittleEndian(
                prefix);

        if (length is < PairingCodec.HeaderSize or
            > PairingCodec.MaximumPayloadSize)
        {
            throw new FormatException(
                "Pairing message length is invalid.");
        }

        byte[] payload = new byte[length];

        await stream.ReadExactlyAsync(
            payload,
            cancellationToken);

        return payload;
    }

    private static async Task WriteMessageAsync(
        Stream stream,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (payload.Length is < PairingCodec.HeaderSize or
            > PairingCodec.MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload));
        }

        byte[] prefix = new byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(
            prefix,
            checked((uint)payload.Length));

        await stream.WriteAsync(
            prefix,
            cancellationToken);

        await stream.WriteAsync(
            payload,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }
}
