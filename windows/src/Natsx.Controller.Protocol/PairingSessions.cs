using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public sealed class PairingEstablishedMaterial : IDisposable
{
    private byte[]? _trustKey;

    public PairingEstablishedMaterial(
        PeerId remotePeerId,
        ReadOnlySpan<byte> trustKey)
    {
        if (trustKey.Length != PairingCrypto.TrustKeySize)
        {
            throw new ArgumentException(
                $"Trust key must be exactly {PairingCrypto.TrustKeySize} bytes.",
                nameof(trustKey));
        }

        RemotePeerId = remotePeerId;
        _trustKey = trustKey.ToArray();
    }

    public PeerId RemotePeerId { get; }

    public byte[] ExportTrustKey()
    {
        ObjectDisposedException.ThrowIf(_trustKey is null, this);
        return _trustKey.ToArray();
    }

    public void Dispose()
    {
        byte[]? trustKey = Interlocked.Exchange(ref _trustKey, null);
        if (trustKey is not null)
        {
            CryptographicOperations.ZeroMemory(trustKey);
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class PairingInitiatorSession : IDisposable
{
    private readonly PeerId _localPeerId;
    private readonly PairingKeyAgreement _keyAgreement;
    private readonly PairingOfferPayload _offer;

    private PairingResponsePayload? _response;
    private byte[]? _transcriptHash;
    private byte[]? _trustKey;
    private bool _approved;
    private bool _completed;
    private bool _disposed;

    public PairingInitiatorSession(PeerId localPeerId)
    {
        _localPeerId = localPeerId;
        _keyAgreement = new PairingKeyAgreement();

        _offer = new PairingOfferPayload(
            localPeerId,
            PairingExchangePayloadCodec.CreateNonce(),
            _keyAgreement.PublicKey.ToArray());
    }

    public PairingOfferPayload Offer
    {
        get
        {
            ThrowIfDisposed();
            return CopyOffer(_offer);
        }
    }

    public string? ComparisonCode { get; private set; }

    public PeerId? RemotePeerId => _response?.ResponderPeerId;

    public string AcceptResponse(PairingResponsePayload response)
    {
        ThrowIfDisposed();

        if (_response is not null)
        {
            throw new InvalidOperationException(
                "Pairing response has already been accepted.");
        }

        byte[] sharedSecret =
            _keyAgreement.DeriveSharedSecret(response.ResponderPublicKey);

        try
        {
            byte[] transcriptHash =
                PairingCrypto.ComputeTranscriptHash(_offer, response);

            byte[] trustKey =
                PairingCrypto.DeriveTrustKey(sharedSecret, transcriptHash);

            string code =
                PairingCrypto.DeriveSixDigitCode(sharedSecret, transcriptHash);

            _response = CopyResponse(response);
            _transcriptHash = transcriptHash;
            _trustKey = trustKey;
            ComparisonCode = code;
            return code;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public PairingConfirmPayload ApproveDisplayedCode()
    {
        ThrowIfDisposed();
        EnsureContextReady();

        if (_approved)
        {
            throw new InvalidOperationException(
                "Pairing code has already been approved.");
        }

        _approved = true;

        byte[] proof = PairingCrypto.CreateConfirmationProof(
            _trustKey!,
            _transcriptHash!,
            PairingRole.Initiator);

        return new PairingConfirmPayload(
            _localPeerId,
            PairingRole.Initiator,
            proof);
    }

    public PairingEstablishedMaterial AcceptRemoteConfirmation(
        PairingConfirmPayload confirmation)
    {
        ThrowIfDisposed();
        EnsureContextReady();

        if (!_approved)
        {
            throw new InvalidOperationException(
                "Local pairing code must be approved before completing pairing.");
        }

        if (_completed)
        {
            throw new InvalidOperationException("Pairing has already completed.");
        }

        PairingResponsePayload response = _response!.Value;

        if (confirmation.PeerId != response.ResponderPeerId ||
            confirmation.Role != PairingRole.Responder)
        {
            throw new CryptographicException(
                "Pairing confirmation identity or role does not match the responder.");
        }

        if (!PairingCrypto.VerifyConfirmationProof(
                _trustKey!,
                _transcriptHash!,
                PairingRole.Responder,
                confirmation.Proof))
        {
            throw new CryptographicException(
                "Responder pairing confirmation proof is invalid.");
        }

        _completed = true;
        return new PairingEstablishedMaterial(
            response.ResponderPeerId,
            _trustKey!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyAgreement.Dispose();
        ZeroAndClear(ref _transcriptHash);
        ZeroAndClear(ref _trustKey);

        if (_offer.InitiatorNonce is not null)
        {
            CryptographicOperations.ZeroMemory(_offer.InitiatorNonce);
        }

        if (_offer.InitiatorPublicKey is not null)
        {
            CryptographicOperations.ZeroMemory(_offer.InitiatorPublicKey);
        }

        if (_response is { } response)
        {
            CryptographicOperations.ZeroMemory(response.ResponderNonce);
            CryptographicOperations.ZeroMemory(response.ResponderPublicKey);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void EnsureContextReady()
    {
        if (_response is null ||
            _transcriptHash is null ||
            _trustKey is null ||
            ComparisonCode is null)
        {
            throw new InvalidOperationException(
                "Pairing response must be accepted before confirmation.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static PairingOfferPayload CopyOffer(PairingOfferPayload value) =>
        new(
            value.InitiatorPeerId,
            value.InitiatorNonce.ToArray(),
            value.InitiatorPublicKey.ToArray());

    private static PairingResponsePayload CopyResponse(PairingResponsePayload value) =>
        new(
            value.ResponderPeerId,
            value.ResponderNonce.ToArray(),
            value.ResponderPublicKey.ToArray());

    private static void ZeroAndClear(ref byte[]? value)
    {
        byte[]? bytes = Interlocked.Exchange(ref value, null);
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

public sealed class PairingResponderSession : IDisposable
{
    private readonly PeerId _localPeerId;
    private readonly PairingKeyAgreement _keyAgreement;
    private readonly PairingOfferPayload _offer;
    private readonly PairingResponsePayload _response;
    private byte[]? _transcriptHash;
    private byte[]? _trustKey;

    private bool _approved;
    private bool _completed;
    private bool _disposed;

    public PairingResponderSession(
        PeerId localPeerId,
        PairingOfferPayload offer)
    {
        _localPeerId = localPeerId;
        _offer = CopyOffer(offer);
        _keyAgreement = new PairingKeyAgreement();

        _response = new PairingResponsePayload(
            localPeerId,
            PairingExchangePayloadCodec.CreateNonce(),
            _keyAgreement.PublicKey.ToArray());

        byte[] sharedSecret =
            _keyAgreement.DeriveSharedSecret(_offer.InitiatorPublicKey);

        try
        {
            _transcriptHash =
                PairingCrypto.ComputeTranscriptHash(_offer, _response);

            _trustKey =
                PairingCrypto.DeriveTrustKey(
                    sharedSecret,
                    _transcriptHash);

            ComparisonCode =
                PairingCrypto.DeriveSixDigitCode(
                    sharedSecret,
                    _transcriptHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public PairingResponsePayload Response
    {
        get
        {
            ThrowIfDisposed();
            return CopyResponse(_response);
        }
    }

    public string ComparisonCode { get; }

    public PeerId RemotePeerId => _offer.InitiatorPeerId;

    public PairingConfirmPayload ApproveDisplayedCode()
    {
        ThrowIfDisposed();

        if (_approved)
        {
            throw new InvalidOperationException(
                "Pairing code has already been approved.");
        }

        _approved = true;

        byte[] proof = PairingCrypto.CreateConfirmationProof(
            _trustKey!,
            _transcriptHash!,
            PairingRole.Responder);

        return new PairingConfirmPayload(
            _localPeerId,
            PairingRole.Responder,
            proof);
    }

    public PairingEstablishedMaterial AcceptRemoteConfirmation(
        PairingConfirmPayload confirmation)
    {
        ThrowIfDisposed();

        if (!_approved)
        {
            throw new InvalidOperationException(
                "Local pairing code must be approved before completing pairing.");
        }

        if (_completed)
        {
            throw new InvalidOperationException("Pairing has already completed.");
        }

        if (confirmation.PeerId != _offer.InitiatorPeerId ||
            confirmation.Role != PairingRole.Initiator)
        {
            throw new CryptographicException(
                "Pairing confirmation identity or role does not match the initiator.");
        }

        if (!PairingCrypto.VerifyConfirmationProof(
                _trustKey!,
                _transcriptHash!,
                PairingRole.Initiator,
                confirmation.Proof))
        {
            throw new CryptographicException(
                "Initiator pairing confirmation proof is invalid.");
        }

        _completed = true;
        return new PairingEstablishedMaterial(
            _offer.InitiatorPeerId,
            _trustKey!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyAgreement.Dispose();
        ZeroAndClear(ref _transcriptHash);
        ZeroAndClear(ref _trustKey);

        CryptographicOperations.ZeroMemory(_offer.InitiatorNonce);
        CryptographicOperations.ZeroMemory(_offer.InitiatorPublicKey);
        CryptographicOperations.ZeroMemory(_response.ResponderNonce);
        CryptographicOperations.ZeroMemory(_response.ResponderPublicKey);

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static PairingOfferPayload CopyOffer(PairingOfferPayload value) =>
        new(
            value.InitiatorPeerId,
            value.InitiatorNonce.ToArray(),
            value.InitiatorPublicKey.ToArray());

    private static PairingResponsePayload CopyResponse(PairingResponsePayload value) =>
        new(
            value.ResponderPeerId,
            value.ResponderNonce.ToArray(),
            value.ResponderPublicKey.ToArray());

    private static void ZeroAndClear(ref byte[]? value)
    {
        byte[]? bytes = Interlocked.Exchange(ref value, null);
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
