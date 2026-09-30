using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows;

public sealed record TrustedPeerRecord(
    PeerId PeerId,
    string? DisplayName,
    byte Capabilities,
    DateTimeOffset PairedAt,
    int PairingVersion = 1)
{
    public const int CurrentPairingVersion = 1;
}

public sealed class TrustedPeerMaterial : IDisposable
{
    public const int TrustSecretSize = 32;

    private readonly byte[] _trustSecret;
    private bool _disposed;

    public TrustedPeerMaterial(
        TrustedPeerRecord record,
        ReadOnlySpan<byte> trustSecret)
    {
        if (trustSecret.Length != TrustSecretSize)
        {
            throw new ArgumentException(
                $"Trust secret must be exactly {TrustSecretSize} bytes.",
                nameof(trustSecret));
        }

        Record = record;
        _trustSecret = trustSecret.ToArray();
    }

    public TrustedPeerRecord Record { get; }

    public byte[] CopyTrustSecret()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _trustSecret.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_trustSecret);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
