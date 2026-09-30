using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver;

public sealed class ReceiverIdentityStore
{
    private readonly string _identityPath;

    public ReceiverIdentityStore(string? rootDirectory = null)
    {
        string root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NATSX Controller");

        Directory.CreateDirectory(root);
        _identityPath = Path.Combine(root, "receiver-identity.bin");
    }

    public SessionId GetOrCreate()
    {
        if (File.Exists(_identityPath))
        {
            byte[] existing = File.ReadAllBytes(_identityPath);

            if (existing.Length != 16)
                throw new InvalidDataException("Stored receiver identity is invalid.");

            return SessionId.FromBytes(existing);
        }

        byte[] bytes;

        do
        {
            bytes = RandomNumberGenerator.GetBytes(16);
        }
        while (bytes.All(value => value == 0));

        string tempPath = _identityPath + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, _identityPath, overwrite: true);

        return SessionId.FromBytes(bytes);
    }
}
