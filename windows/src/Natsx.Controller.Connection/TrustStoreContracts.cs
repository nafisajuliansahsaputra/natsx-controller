using Natsx.Controller.Protocol;

namespace Natsx.Controller.Connection;

public interface ITrustedPeerStore
{
    ValueTask SaveTrustKeyAsync(
        PeerId peerId,
        ReadOnlyMemory<byte> trustKey,
        CancellationToken cancellationToken = default);

    ValueTask<byte[]?> GetTrustKeyAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ContainsAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default);
}

public interface ILocalPeerIdentityStore
{
    ValueTask<PeerId> GetOrCreateAsync(
        CancellationToken cancellationToken = default);
}
