using System.IO;
using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver;

public sealed class WindowsTrustedPeerStore
{
    private readonly string _trustDirectory;

    public WindowsTrustedPeerStore(string? rootDirectory = null)
    {
        string root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NATSX Controller");

        _trustDirectory = Path.Combine(root, "trust");
        Directory.CreateDirectory(_trustDirectory);
    }

    public TrustedPeerCredentials? TryGet(SessionId deviceId)
    {
        string path = GetPath(deviceId);

        if (!File.Exists(path))
            return null;

        byte[] protectedBytes = File.ReadAllBytes(path);
        byte[] rootKey = WindowsDataProtection.Unprotect(protectedBytes);

        if (rootKey.Length != TrustedSessionCrypto.PairingRootKeySize)
        {
            CryptographicOperations.ZeroMemory(rootKey);
            throw new InvalidDataException("Trusted peer root key is invalid.");
        }

        return new TrustedPeerCredentials(deviceId, rootKey);
    }

    public void Save(SessionId deviceId, ReadOnlySpan<byte> pairingRootKey)
    {
        if (deviceId == SessionId.Zero)
            throw new ArgumentException("Trusted peer Device ID cannot be zero.", nameof(deviceId));

        if (pairingRootKey.Length != TrustedSessionCrypto.PairingRootKeySize)
        {
            throw new ArgumentException(
                "Pairing root key must be exactly 32 bytes.",
                nameof(pairingRootKey));
        }

        byte[] protectedBytes = WindowsDataProtection.Protect(pairingRootKey);
        string path = GetPath(deviceId);
        string tempPath = path + ".tmp";

        File.WriteAllBytes(tempPath, protectedBytes);
        File.Move(tempPath, path, overwrite: true);
        CryptographicOperations.ZeroMemory(protectedBytes);
    }

    public bool Forget(SessionId deviceId)
    {
        string path = GetPath(deviceId);

        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    private string GetPath(SessionId deviceId)
    {
        Span<byte> bytes = stackalloc byte[16];
        deviceId.WriteBytes(bytes);
        string id = Convert.ToHexString(bytes).ToLowerInvariant();
        return Path.Combine(_trustDirectory, id + ".bin");
    }
}
