using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows.Tests;

public sealed class FileTrustedPeerStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "natsx-trust-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PutGetListAndRemoveRoundTripProtectedSecret()
    {
        string path = Path.Combine(_root, "trusted.json");
        var store = new FileTrustedPeerStore(path, new TestProtector());
        PeerId peerId = PeerId.CreateRandom();
        byte[] secret = Enumerable.Range(0, TrustedPeerMaterial.TrustSecretSize)
            .Select(static value => (byte)value)
            .ToArray();

        var record = new TrustedPeerRecord(
            peerId,
            "NATSX Phone",
            capabilities: 0b111,
            PairedAt: DateTimeOffset.UtcNow);

        store.Put(record, secret);

        string persisted = File.ReadAllText(path);
        Assert.DoesNotContain(Convert.ToBase64String(secret), persisted);

        using TrustedPeerMaterial? material = store.Get(peerId);
        Assert.NotNull(material);
        Assert.Equal(record.PeerId, material!.Record.PeerId);
        Assert.Equal(record.DisplayName, material.Record.DisplayName);

        byte[] loaded = material.CopyTrustSecret();
        try
        {
            Assert.Equal(secret, loaded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(loaded);
        }

        Assert.Single(store.List());

        store.Remove(peerId);
        Assert.Null(store.Get(peerId));
        Assert.Empty(store.List());
    }

    [Fact]
    public void LocalPeerIdentityRemainsStable()
    {
        string path = Path.Combine(_root, "peer-id.txt");
        var store = new LocalPeerIdentityStore(path);

        PeerId first = store.GetOrCreate();
        PeerId second = store.GetOrCreate();

        Assert.Equal(first, second);
        Assert.NotEqual(PeerId.Zero, first);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestProtector : ITrustSecretProtector
    {
        private const byte Mask = 0xA5;

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            byte[] output = plaintext.ToArray();
            for (int index = 0; index < output.Length; index++)
            {
                output[index] ^= Mask;
            }

            return output;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            return Protect(protectedData);
        }
    }
}
