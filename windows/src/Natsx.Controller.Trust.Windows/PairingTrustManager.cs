using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows;

public sealed class PairingTrustManager
{
    private readonly ITrustedPeerStore _trustedPeerStore;
    private readonly TrustedSessionRegistry _sessionRegistry;

    public PairingTrustManager(
        ITrustedPeerStore trustedPeerStore,
        TrustedSessionRegistry sessionRegistry)
    {
        _trustedPeerStore =
            trustedPeerStore ??
            throw new ArgumentNullException(nameof(trustedPeerStore));
        _sessionRegistry =
            sessionRegistry ??
            throw new ArgumentNullException(nameof(sessionRegistry));
    }

    public void Persist(
        FirstPairingResult result,
        string? displayName,
        DateTimeOffset pairedAt)
    {
        ArgumentNullException.ThrowIfNull(result);

        byte[] secret = result.CopyTrustSecret();

        try
        {
            _trustedPeerStore.Put(
                new TrustedPeerRecord(
                    result.RemotePeerId,
                    displayName,
                    (byte)result.Capabilities,
                    pairedAt,
                    TrustedPeerRecord.CurrentPairingVersion),
                secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public void Forget(PeerId peerId)
    {
        _sessionRegistry.Remove(peerId);
        _trustedPeerStore.Remove(peerId);
    }

    public void ResetAll()
    {
        _sessionRegistry.Clear();
        _trustedPeerStore.Clear();
    }
}
