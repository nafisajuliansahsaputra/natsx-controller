using System.Security.Cryptography;
using Natsx.Controller.Connection;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver.Security;

public sealed class WindowsProtectedTrustStore : ITrustedPeerStore
{
    private const int TrustKeySize = 32;
    private static readonly byte[] OptionalEntropy =
        "NATSX Controller Trust Store v1"u8.ToArray();

    private readonly string _directory;

    public WindowsProtectedTrustStore(string? baseDirectory = null)
    {
        _directory = Path.Combine(
            baseDirectory ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NATSX Controller",
            "trust");
    }

    public async ValueTask SaveTrustKeyAsync(
        PeerId peerId,
        ReadOnlyMemory<byte> trustKey,
        CancellationToken cancellationToken = default)
    {
        if (trustKey.Length != TrustKeySize)
        {
            throw new ArgumentException(
                $"Trust key must be exactly {TrustKeySize} bytes.",
                nameof(trustKey));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);

        byte[] plain = trustKey.ToArray();
        byte[] protectedBytes;

        try
        {
            protectedBytes = ProtectedData.Protect(
                plain,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        string path = GetPath(peerId);
        string temporaryPath = path + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                protectedBytes,
                cancellationToken).ConfigureAwait(false);

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async ValueTask<byte[]?> GetTrustKeyAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default)
    {
        string path = GetPath(peerId);

        if (!File.Exists(path))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(
            path,
            cancellationToken).ConfigureAwait(false);

        try
        {
            byte[] plain = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);

            if (plain.Length != TrustKeySize)
            {
                CryptographicOperations.ZeroMemory(plain);
                throw new CryptographicException(
                    "Stored trust key has an invalid length.");
            }

            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    public ValueTask<bool> ContainsAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(File.Exists(GetPath(peerId)));
    }

    public ValueTask<bool> RemoveAsync(
        PeerId peerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = GetPath(peerId);
        if (!File.Exists(path))
        {
            return ValueTask.FromResult(false);
        }

        File.Delete(path);
        return ValueTask.FromResult(true);
    }

    private string GetPath(PeerId peerId) =>
        Path.Combine(_directory, peerId + ".bin");
}

public sealed class WindowsLocalPeerIdentityStore : ILocalPeerIdentityStore
{
    private readonly string _identityPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsLocalPeerIdentityStore(string? baseDirectory = null)
    {
        string directory = Path.Combine(
            baseDirectory ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NATSX Controller");

        _identityPath = Path.Combine(directory, "receiver-peer-id.bin");
    }

    public async ValueTask<PeerId> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(_identityPath))
            {
                byte[] existing = await File.ReadAllBytesAsync(
                    _identityPath,
                    cancellationToken).ConfigureAwait(false);

                if (existing.Length != PeerId.Size)
                {
                    throw new InvalidDataException(
                        "Stored receiver Peer ID has an invalid length.");
                }

                return PeerId.FromBytes(existing);
            }

            PeerId peerId = PeerId.CreateRandom();
            Span<byte> bytes = stackalloc byte[PeerId.Size];
            peerId.WriteBytes(bytes);

            Directory.CreateDirectory(
                Path.GetDirectoryName(_identityPath)!);

            await File.WriteAllBytesAsync(
                _identityPath,
                bytes.ToArray(),
                cancellationToken).ConfigureAwait(false);

            return peerId;
        }
        finally
        {
            _gate.Release();
        }
    }
}
