using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public sealed class FirstPairingResult : IDisposable
{
    private readonly byte[] _trustSecret;
    private bool _disposed;

    public FirstPairingResult(
        PeerId remotePeerId,
        TransportCapabilities capabilities,
        ReadOnlySpan<byte> trustSecret)
    {
        if (trustSecret.Length != PairingCrypto.DerivedKeySize)
        {
            throw new ArgumentException("Trust secret must be exactly 32 bytes.", nameof(trustSecret));
        }

        RemotePeerId = remotePeerId;
        Capabilities = capabilities;
        _trustSecret = trustSecret.ToArray();
    }

    public PeerId RemotePeerId { get; }

    public TransportCapabilities Capabilities { get; }

    public byte[] CopyTrustSecret()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _trustSecret.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_trustSecret);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public readonly record struct WindowsPairingResponse(
    byte[] FrameBytes,
    string Sas,
    PeerId AndroidPeerId,
    SessionId SessionId);

public readonly record struct WindowsPairingCompletion(
    byte[] FrameBytes,
    FirstPairingResult Result);

public sealed class AndroidFirstPairingSession : IDisposable
{
    private readonly PeerId _localPeerId;
    private readonly TransportCapabilities _capabilities;
    private readonly P256EphemeralKeyAgreement _keyAgreement = new();
    private readonly byte[] _nonce = RandomNumberGenerator.GetBytes(PairingCrypto.NonceSize);
    private readonly byte[] _publicKey;

    private PeerId _windowsPeerId;
    private TransportCapabilities _windowsCapabilities;
    private SessionId _sessionId;
    private byte[]? _transcriptHash;
    private byte[]? _pairingKey;
    private bool _responseAccepted;
    private bool _confirmCreated;
    private bool _disposed;

    public AndroidFirstPairingSession(
        PeerId localPeerId,
        TransportCapabilities capabilities)
    {
        if (localPeerId == PeerId.Zero)
        {
            throw new ArgumentException("Android Peer ID must be non-zero.", nameof(localPeerId));
        }

        _localPeerId = localPeerId;
        _capabilities = capabilities;
        _publicKey = _keyAgreement.ExportPublicKey();
    }

    public byte[] CreateHelloFrame(ulong monotonicTimestampMicros)
    {
        ThrowIfDisposed();

        return ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.PairingHello,
                FrameFlags.None,
                SessionId.Zero,
                0,
                monotonicTimestampMicros,
                PairingPayloadCodec.EncodeHello(
                    new PairingHelloPayload(
                        _localPeerId,
                        _capabilities,
                        _nonce.ToArray(),
                        _publicKey.ToArray()))));
    }

    public string AcceptResponseFrame(ReadOnlySpan<byte> frameBytes)
    {
        ThrowIfDisposed();
        if (_responseAccepted)
        {
            throw new InvalidOperationException("Pairing response was already accepted.");
        }

        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes);

        if (frame.MessageType != MessageType.PairingResponse ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId == SessionId.Zero)
        {
            throw new CryptographicException("Invalid PAIRING_RESPONSE envelope.");
        }

        PairingResponsePayload response =
            PairingPayloadCodec.DecodeResponse(frame.Payload);

        byte[] sharedSecret = _keyAgreement.DeriveSharedSecret(response.PublicKey);
        byte[] transcriptHash = PairingCrypto.ComputePairingTranscriptHash(
            _localPeerId,
            response.WindowsPeerId,
            _nonce,
            response.Nonce,
            _publicKey,
            response.PublicKey);
        byte[] pairingKey = PairingCrypto.DerivePairingKey(
            sharedSecret,
            transcriptHash);
        byte[] expectedProof = PairingCrypto.ComputePairingResponseProof(
            pairingKey,
            transcriptHash,
            frame.SessionId);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    expectedProof,
                    response.ResponseProof))
            {
                throw new CryptographicException("PAIRING_RESPONSE proof is invalid.");
            }

            _windowsPeerId = response.WindowsPeerId;
            _windowsCapabilities = response.Capabilities;
            _sessionId = frame.SessionId;
            _transcriptHash = transcriptHash;
            _pairingKey = pairingKey;
            transcriptHash = null!;
            pairingKey = null!;
            _responseAccepted = true;

            return PairingCrypto.DeriveSixDigitSas(
                _pairingKey,
                _transcriptHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(expectedProof);
            if (transcriptHash is not null)
            {
                CryptographicOperations.ZeroMemory(transcriptHash);
            }
            if (pairingKey is not null)
            {
                CryptographicOperations.ZeroMemory(pairingKey);
            }
            CryptographicOperations.ZeroMemory(response.Nonce);
            CryptographicOperations.ZeroMemory(response.PublicKey);
            CryptographicOperations.ZeroMemory(response.ResponseProof);
        }
    }

    public byte[] CreateConfirmFrame(
        bool userConfirmedSas,
        ulong monotonicTimestampMicros)
    {
        ThrowIfDisposed();
        EnsureResponseAccepted();

        if (!userConfirmedSas)
        {
            throw new OperationCanceledException("Android user rejected the pairing SAS.");
        }

        if (_confirmCreated)
        {
            throw new InvalidOperationException("PAIRING_CONFIRM was already created.");
        }

        byte[] proof = PairingCrypto.ComputePairingConfirmationProof(
            PairingConfirmationRole.Android,
            _pairingKey!,
            _transcriptHash!,
            _sessionId);

        try
        {
            _confirmCreated = true;
            return ProtocolFrameCodec.Encode(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.PairingConfirm,
                    FrameFlags.None,
                    _sessionId,
                    0,
                    monotonicTimestampMicros,
                    PairingPayloadCodec.EncodeConfirmation(
                        new PairingConfirmationPayload(
                            _localPeerId,
                            proof))));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(proof);
        }
    }

    public FirstPairingResult AcceptCompleteFrame(ReadOnlySpan<byte> frameBytes)
    {
        ThrowIfDisposed();
        EnsureResponseAccepted();

        if (!_confirmCreated)
        {
            throw new InvalidOperationException("Android user confirmation is required first.");
        }

        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes);
        if (frame.MessageType != MessageType.PairingComplete ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId != _sessionId)
        {
            throw new CryptographicException("Invalid PAIRING_COMPLETE envelope.");
        }

        PairingConfirmationPayload complete =
            PairingPayloadCodec.DecodeConfirmation(frame.Payload);

        if (complete.PeerId != _windowsPeerId ||
            !PairingCrypto.VerifyPairingConfirmationProof(
                PairingConfirmationRole.Windows,
                _pairingKey!,
                _transcriptHash!,
                _sessionId,
                complete.Proof))
        {
            CryptographicOperations.ZeroMemory(complete.Proof);
            throw new CryptographicException("Windows pairing confirmation is invalid.");
        }

        CryptographicOperations.ZeroMemory(complete.Proof);

        byte[] trustSecret = PairingCrypto.DeriveTrustSecret(
            _pairingKey!,
            _transcriptHash!);

        try
        {
            return new FirstPairingResult(
                _windowsPeerId,
                _windowsCapabilities,
                trustSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(trustSecret);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _keyAgreement.Dispose();
        CryptographicOperations.ZeroMemory(_nonce);
        CryptographicOperations.ZeroMemory(_publicKey);
        ZeroAndClear(ref _transcriptHash);
        ZeroAndClear(ref _pairingKey);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void EnsureResponseAccepted()
    {
        if (!_responseAccepted)
        {
            throw new InvalidOperationException("PAIRING_RESPONSE must be accepted first.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ZeroAndClear(ref byte[]? value)
    {
        if (value is null) return;
        CryptographicOperations.ZeroMemory(value);
        value = null;
    }
}

public sealed class WindowsFirstPairingSession : IDisposable
{
    private readonly PeerId _localPeerId;
    private readonly TransportCapabilities _capabilities;
    private readonly P256EphemeralKeyAgreement _keyAgreement = new();
    private readonly byte[] _nonce = RandomNumberGenerator.GetBytes(PairingCrypto.NonceSize);
    private readonly byte[] _publicKey;

    private PeerId _androidPeerId;
    private TransportCapabilities _androidCapabilities;
    private SessionId _sessionId;
    private byte[]? _transcriptHash;
    private byte[]? _pairingKey;
    private bool _helloAccepted;
    private bool _disposed;

    public WindowsFirstPairingSession(
        PeerId localPeerId,
        TransportCapabilities capabilities)
    {
        if (localPeerId == PeerId.Zero)
        {
            throw new ArgumentException("Windows Peer ID must be non-zero.", nameof(localPeerId));
        }

        _localPeerId = localPeerId;
        _capabilities = capabilities;
        _publicKey = _keyAgreement.ExportPublicKey();
    }

    public WindowsPairingResponse AcceptHelloFrame(
        ReadOnlySpan<byte> frameBytes,
        ulong monotonicTimestampMicros)
    {
        ThrowIfDisposed();
        if (_helloAccepted)
        {
            throw new InvalidOperationException("PAIRING_HELLO was already accepted.");
        }

        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes);
        if (frame.MessageType != MessageType.PairingHello ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId != SessionId.Zero)
        {
            throw new CryptographicException("Invalid PAIRING_HELLO envelope.");
        }

        PairingHelloPayload hello =
            PairingPayloadCodec.DecodeHello(frame.Payload);

        byte[] sharedSecret = _keyAgreement.DeriveSharedSecret(hello.PublicKey);
        byte[] transcriptHash = PairingCrypto.ComputePairingTranscriptHash(
            hello.AndroidPeerId,
            _localPeerId,
            hello.Nonce,
            _nonce,
            hello.PublicKey,
            _publicKey);
        byte[] pairingKey = PairingCrypto.DerivePairingKey(
            sharedSecret,
            transcriptHash);

        SessionId sessionId = SessionId.CreateRandom();
        byte[] responseProof = PairingCrypto.ComputePairingResponseProof(
            pairingKey,
            transcriptHash,
            sessionId);

        try
        {
            byte[] responseFrame = ProtocolFrameCodec.Encode(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.PairingResponse,
                    FrameFlags.None,
                    sessionId,
                    0,
                    monotonicTimestampMicros,
                    PairingPayloadCodec.EncodeResponse(
                        new PairingResponsePayload(
                            _localPeerId,
                            _capabilities,
                            _nonce.ToArray(),
                            _publicKey.ToArray(),
                            responseProof))));

            _androidPeerId = hello.AndroidPeerId;
            _androidCapabilities = hello.Capabilities;
            _sessionId = sessionId;
            _transcriptHash = transcriptHash;
            _pairingKey = pairingKey;
            transcriptHash = null!;
            pairingKey = null!;
            _helloAccepted = true;

            return new WindowsPairingResponse(
                responseFrame,
                PairingCrypto.DeriveSixDigitSas(
                    _pairingKey,
                    _transcriptHash),
                _androidPeerId,
                _sessionId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(responseProof);
            CryptographicOperations.ZeroMemory(hello.Nonce);
            CryptographicOperations.ZeroMemory(hello.PublicKey);
            if (transcriptHash is not null)
            {
                CryptographicOperations.ZeroMemory(transcriptHash);
            }
            if (pairingKey is not null)
            {
                CryptographicOperations.ZeroMemory(pairingKey);
            }
        }
    }

    public WindowsPairingCompletion AcceptConfirmFrame(
        ReadOnlySpan<byte> frameBytes,
        bool userConfirmedSas,
        ulong monotonicTimestampMicros)
    {
        ThrowIfDisposed();
        if (!_helloAccepted)
        {
            throw new InvalidOperationException("PAIRING_HELLO must be accepted first.");
        }

        if (!userConfirmedSas)
        {
            throw new OperationCanceledException("Windows user rejected the pairing SAS.");
        }

        ProtocolFrame frame = ProtocolFrameCodec.Decode(frameBytes);
        if (frame.MessageType != MessageType.PairingConfirm ||
            frame.Flags != FrameFlags.None ||
            frame.SessionId != _sessionId)
        {
            throw new CryptographicException("Invalid PAIRING_CONFIRM envelope.");
        }

        PairingConfirmationPayload confirm =
            PairingPayloadCodec.DecodeConfirmation(frame.Payload);

        if (confirm.PeerId != _androidPeerId ||
            !PairingCrypto.VerifyPairingConfirmationProof(
                PairingConfirmationRole.Android,
                _pairingKey!,
                _transcriptHash!,
                _sessionId,
                confirm.Proof))
        {
            CryptographicOperations.ZeroMemory(confirm.Proof);
            throw new CryptographicException("Android pairing confirmation is invalid.");
        }

        CryptographicOperations.ZeroMemory(confirm.Proof);

        byte[] windowsProof = PairingCrypto.ComputePairingConfirmationProof(
            PairingConfirmationRole.Windows,
            _pairingKey!,
            _transcriptHash!,
            _sessionId);
        byte[] trustSecret = PairingCrypto.DeriveTrustSecret(
            _pairingKey!,
            _transcriptHash!);

        try
        {
            byte[] completeFrame = ProtocolFrameCodec.Encode(
                new ProtocolFrame(
                    ProtocolVersion.Current,
                    MessageType.PairingComplete,
                    FrameFlags.None,
                    _sessionId,
                    0,
                    monotonicTimestampMicros,
                    PairingPayloadCodec.EncodeConfirmation(
                        new PairingConfirmationPayload(
                            _localPeerId,
                            windowsProof))));

            return new WindowsPairingCompletion(
                completeFrame,
                new FirstPairingResult(
                    _androidPeerId,
                    _androidCapabilities,
                    trustSecret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(windowsProof);
            CryptographicOperations.ZeroMemory(trustSecret);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _keyAgreement.Dispose();
        CryptographicOperations.ZeroMemory(_nonce);
        CryptographicOperations.ZeroMemory(_publicKey);
        ZeroAndClear(ref _transcriptHash);
        ZeroAndClear(ref _pairingKey);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ZeroAndClear(ref byte[]? value)
    {
        if (value is null) return;
        CryptographicOperations.ZeroMemory(value);
        value = null;
    }
}
