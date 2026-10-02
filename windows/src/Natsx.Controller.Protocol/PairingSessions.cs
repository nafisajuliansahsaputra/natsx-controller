using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public sealed class PairingEstablishedMaterial : IDisposable
{
    private byte[]? _trustSecret;

    public PairingEstablishedMaterial(
        PeerId remotePeerId,
        ReadOnlySpan<byte> trustSecret)
    {
        if (trustSecret.Length !=
            PairingCrypto.DerivedKeySize)
        {
            throw new ArgumentException(
                $"Trust secret must be exactly {PairingCrypto.DerivedKeySize} bytes.",
                nameof(trustSecret));
        }

        RemotePeerId =
            remotePeerId;
        _trustSecret =
            trustSecret.ToArray();
    }

    public PeerId RemotePeerId { get; }

    public byte[] CopyTrustSecret()
    {
        byte[] secret =
            _trustSecret ??
            throw new ObjectDisposedException(
                nameof(PairingEstablishedMaterial));

        return secret.ToArray();
    }

    public void Dispose()
    {
        byte[]? secret =
            Interlocked.Exchange(
                ref _trustSecret,
                null);

        if (secret is not null)
        {
            CryptographicOperations.ZeroMemory(
                secret);
        }

        GC.SuppressFinalize(this);
    }
}

public sealed class PairingInitiatorSession : IDisposable
{
    private readonly PeerId _localPeerId;
    private readonly P256EphemeralKeyAgreement _keyAgreement =
        new();
    private readonly PairingOfferPayload _offer;

    private PairingResponsePayload? _response;
    private byte[]? _transcriptHash;
    private byte[]? _pairingKey;
    private byte[]? _trustSecret;
    private bool _localApproved;
    private bool _completed;
    private bool _disposed;

    public PairingInitiatorSession(
        PeerId localPeerId)
    {
        _localPeerId =
            localPeerId;

        _offer =
            new PairingOfferPayload(
                localPeerId,
                PairingPayloadCodec
                    .CreateNonce(),
                _keyAgreement
                    .ExportPublicKey());
    }

    public string? ComparisonCode { get; private set; }

    public PeerId? RemotePeerId =>
        _response?.WindowsPeerId;

    public PairingOfferPayload Offer
    {
        get
        {
            ThrowIfDisposed();

            return CopyOffer(
                _offer);
        }
    }

    public string AcceptResponse(
        PairingResponsePayload response)
    {
        ThrowIfDisposed();

        if (_response is not null)
        {
            throw new InvalidOperationException(
                "Pairing response has already been accepted.");
        }

        byte[] sharedSecret =
            _keyAgreement
                .DeriveSharedSecret(
                    response.WindowsPublicKey);

        try
        {
            byte[] transcriptHash =
                PairingCrypto
                    .ComputePairingTranscriptHash(
                        _localPeerId,
                        response.WindowsPeerId,
                        _offer.AndroidNonce,
                        response.WindowsNonce,
                        _offer.AndroidPublicKey,
                        response.WindowsPublicKey);

            byte[] pairingKey =
                PairingCrypto
                    .DerivePairingKey(
                        sharedSecret,
                        transcriptHash);

            byte[] trustSecret =
                PairingCrypto
                    .DeriveTrustSecret(
                        pairingKey,
                        transcriptHash);

            string code =
                PairingCrypto
                    .DeriveSixDigitSas(
                        pairingKey,
                        transcriptHash);

            _response =
                CopyResponse(
                    response);
            _transcriptHash =
                transcriptHash;
            _pairingKey =
                pairingKey;
            _trustSecret =
                trustSecret;
            ComparisonCode =
                code;

            return code;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                sharedSecret);
        }
    }

    public PairingConfirmPayload ApproveDisplayedCode()
    {
        ThrowIfDisposed();
        EnsureContextReady();

        if (_localApproved)
        {
            throw new InvalidOperationException(
                "Pairing code has already been approved.");
        }

        _localApproved =
            true;

        byte[] proof =
            PairingCrypto
                .ComputePairingConfirmationProof(
                    _pairingKey!,
                    _transcriptHash!,
                    PairingRole.AndroidController);

        return new PairingConfirmPayload(
            _localPeerId,
            PairingRole.AndroidController,
            proof);
    }

    public PairingEstablishedMaterial AcceptRemoteConfirmation(
        PairingConfirmPayload confirmation)
    {
        ThrowIfDisposed();
        EnsureContextReady();

        if (!_localApproved)
        {
            throw new InvalidOperationException(
                "Local pairing code must be approved before completion.");
        }

        if (_completed)
        {
            throw new InvalidOperationException(
                "Pairing has already completed.");
        }

        PairingResponsePayload response =
            _response!.Value;

        if (confirmation.PeerId !=
                response.WindowsPeerId ||
            confirmation.Role !=
                PairingRole.WindowsReceiver)
        {
            throw new CryptographicException(
                "Pairing confirmation identity or role does not match the Windows responder.");
        }

        if (!PairingCrypto
                .VerifyPairingConfirmationProof(
                    _pairingKey!,
                    _transcriptHash!,
                    PairingRole.WindowsReceiver,
                    confirmation.Proof))
        {
            throw new CryptographicException(
                "Windows pairing confirmation proof is invalid.");
        }

        _completed =
            true;

        return new PairingEstablishedMaterial(
            response.WindowsPeerId,
            _trustSecret!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyAgreement.Dispose();

        Zero(
            _offer.AndroidNonce);
        Zero(
            _offer.AndroidPublicKey);

        if (_response is PairingResponsePayload response)
        {
            Zero(
                response.WindowsNonce);
            Zero(
                response.WindowsPublicKey);
        }

        ZeroAndClear(
            ref _transcriptHash);
        ZeroAndClear(
            ref _pairingKey);
        ZeroAndClear(
            ref _trustSecret);

        _response =
            null;
        _disposed =
            true;

        GC.SuppressFinalize(this);
    }

    private void EnsureContextReady()
    {
        if (_response is null ||
            _transcriptHash is null ||
            _pairingKey is null ||
            _trustSecret is null ||
            ComparisonCode is null)
        {
            throw new InvalidOperationException(
                "Pairing response must be accepted before confirmation.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private static PairingOfferPayload CopyOffer(
        PairingOfferPayload value) =>
        new(
            value.AndroidPeerId,
            value.AndroidNonce.ToArray(),
            value.AndroidPublicKey.ToArray());

    private static PairingResponsePayload CopyResponse(
        PairingResponsePayload value) =>
        new(
            value.WindowsPeerId,
            value.WindowsNonce.ToArray(),
            value.WindowsPublicKey.ToArray());

    private static void Zero(
        byte[] value) =>
        CryptographicOperations.ZeroMemory(
            value);

    private static void ZeroAndClear(
        ref byte[]? value)
    {
        byte[]? current =
            Interlocked.Exchange(
                ref value,
                null);

        if (current is not null)
        {
            Zero(current);
        }
    }
}

public sealed class PairingResponderSession : IDisposable
{
    private readonly PeerId _localPeerId;
    private readonly PairingOfferPayload _offer;
    private readonly P256EphemeralKeyAgreement _keyAgreement =
        new();
    private readonly PairingResponsePayload _response;

    private byte[]? _transcriptHash;
    private byte[]? _pairingKey;
    private byte[]? _trustSecret;
    private bool _localApproved;
    private bool _completed;
    private bool _disposed;

    public PairingResponderSession(
        PeerId localPeerId,
        PairingOfferPayload offer)
    {
        _localPeerId =
            localPeerId;
        _offer =
            CopyOffer(
                offer);

        _response =
            new PairingResponsePayload(
                localPeerId,
                PairingPayloadCodec
                    .CreateNonce(),
                _keyAgreement
                    .ExportPublicKey());

        byte[] sharedSecret =
            _keyAgreement
                .DeriveSharedSecret(
                    _offer.AndroidPublicKey);

        try
        {
            _transcriptHash =
                PairingCrypto
                    .ComputePairingTranscriptHash(
                        _offer.AndroidPeerId,
                        _localPeerId,
                        _offer.AndroidNonce,
                        _response.WindowsNonce,
                        _offer.AndroidPublicKey,
                        _response.WindowsPublicKey);

            _pairingKey =
                PairingCrypto
                    .DerivePairingKey(
                        sharedSecret,
                        _transcriptHash);

            _trustSecret =
                PairingCrypto
                    .DeriveTrustSecret(
                        _pairingKey,
                        _transcriptHash);

            ComparisonCode =
                PairingCrypto
                    .DeriveSixDigitSas(
                        _pairingKey,
                        _transcriptHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                sharedSecret);
        }
    }

    public string ComparisonCode { get; }

    public PeerId RemotePeerId =>
        _offer.AndroidPeerId;

    public PairingResponsePayload Response
    {
        get
        {
            ThrowIfDisposed();

            return CopyResponse(
                _response);
        }
    }

    public PairingConfirmPayload ApproveDisplayedCode()
    {
        ThrowIfDisposed();

        if (_localApproved)
        {
            throw new InvalidOperationException(
                "Pairing code has already been approved.");
        }

        _localApproved =
            true;

        byte[] proof =
            PairingCrypto
                .ComputePairingConfirmationProof(
                    _pairingKey!,
                    _transcriptHash!,
                    PairingRole.WindowsReceiver);

        return new PairingConfirmPayload(
            _localPeerId,
            PairingRole.WindowsReceiver,
            proof);
    }

    public PairingEstablishedMaterial AcceptRemoteConfirmation(
        PairingConfirmPayload confirmation)
    {
        ThrowIfDisposed();

        if (!_localApproved)
        {
            throw new InvalidOperationException(
                "Local pairing code must be approved before completion.");
        }

        if (_completed)
        {
            throw new InvalidOperationException(
                "Pairing has already completed.");
        }

        if (confirmation.PeerId !=
                _offer.AndroidPeerId ||
            confirmation.Role !=
                PairingRole.AndroidController)
        {
            throw new CryptographicException(
                "Pairing confirmation identity or role does not match the Android initiator.");
        }

        if (!PairingCrypto
                .VerifyPairingConfirmationProof(
                    _pairingKey!,
                    _transcriptHash!,
                    PairingRole.AndroidController,
                    confirmation.Proof))
        {
            throw new CryptographicException(
                "Android pairing confirmation proof is invalid.");
        }

        _completed =
            true;

        return new PairingEstablishedMaterial(
            _offer.AndroidPeerId,
            _trustSecret!);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keyAgreement.Dispose();

        Zero(
            _offer.AndroidNonce);
        Zero(
            _offer.AndroidPublicKey);
        Zero(
            _response.WindowsNonce);
        Zero(
            _response.WindowsPublicKey);

        ZeroAndClear(
            ref _transcriptHash);
        ZeroAndClear(
            ref _pairingKey);
        ZeroAndClear(
            ref _trustSecret);

        _disposed =
            true;

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private static PairingOfferPayload CopyOffer(
        PairingOfferPayload value) =>
        new(
            value.AndroidPeerId,
            value.AndroidNonce.ToArray(),
            value.AndroidPublicKey.ToArray());

    private static PairingResponsePayload CopyResponse(
        PairingResponsePayload value) =>
        new(
            value.WindowsPeerId,
            value.WindowsNonce.ToArray(),
            value.WindowsPublicKey.ToArray());

    private static void Zero(
        byte[] value) =>
        CryptographicOperations.ZeroMemory(
            value);

    private static void ZeroAndClear(
        ref byte[]? value)
    {
        byte[]? current =
            Interlocked.Exchange(
                ref value,
                null);

        if (current is not null)
        {
            Zero(current);
        }
    }
}
