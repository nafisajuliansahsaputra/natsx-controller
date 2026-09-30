using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Connection;

public sealed class TrustedPeerManager
{
    private readonly ITrustedPeerStore _store;

    public TrustedPeerManager(ITrustedPeerStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask PersistEstablishedPairingAsync(
        PairingEstablishedMaterial material,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        byte[] trustKey = material.ExportTrustKey();

        try
        {
            await _store.SaveTrustKeyAsync(
                material.RemotePeerId,
                trustKey,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(trustKey);
        }
    }

    public ValueTask<bool> ForgetAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default) =>
        _store.RemoveAsync(peerId, cancellationToken);

    public ValueTask<bool> IsTrustedAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default) =>
        _store.ContainsAsync(peerId, cancellationToken);
}
