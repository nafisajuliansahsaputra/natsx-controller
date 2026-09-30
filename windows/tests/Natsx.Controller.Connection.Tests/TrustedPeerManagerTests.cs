using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Connection.Tests;

public sealed class TrustedPeerManagerTests
{
    [Fact]
    public async Task PersistEstablishedPairingStoresRemoteTrustKey()
    {
        var store = new MemoryTrustStore();
        var manager = new TrustedPeerManager(store);
        PeerId peerId = PeerId.CreateRandom();
        byte[] key = Enumerable.Range(1, 32)
            .Select(static value => (byte)value)
            .ToArray();

        using var material =
            new PairingEstablishedMaterial(peerId, key);

        await manager.PersistEstablishedPairingAsync(material);

        byte[]? stored = await store.GetTrustKeyAsync(peerId);

        Assert.NotNull(stored);
        Assert.Equal(key, stored);
        Assert.True(await manager.IsTrustedAsync(peerId));
    }

    [Fact]
    public async Task ForgetRemovesTrustRelationship()
    {
        var store = new MemoryTrustStore();
        var manager = new TrustedPeerManager(store);
        PeerId peerId = PeerId.CreateRandom();
        byte[] key = RandomNumberGenerator.GetBytes(32);

        await store.SaveTrustKeyAsync(peerId, key);

        Assert.True(await manager.ForgetAsync(peerId));
        Assert.False(await manager.IsTrustedAsync(peerId));
    }

    private sealed class MemoryTrustStore : ITrustedPeerStore
    {
        private readonly Dictionary<PeerId, byte[]> _keys = new();

        public ValueTask SaveTrustKeyAsync(
            PeerId peerId,
            ReadOnlyMemory<byte> trustKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _keys[peerId] = trustKey.ToArray();
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetTrustKeyAsync(
            PeerId peerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(
                _keys.TryGetValue(peerId, out byte[]? key)
                    ? key.ToArray()
                    : null);
        }

        public ValueTask<bool> ContainsAsync(
            PeerId peerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_keys.ContainsKey(peerId));
        }

        public ValueTask<bool> RemoveAsync(
            PeerId peerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_keys.Remove(peerId, out byte[]? key))
            {
                return ValueTask.FromResult(false);
            }

            CryptographicOperations.ZeroMemory(key);
            return ValueTask.FromResult(true);
        }
    }
}
