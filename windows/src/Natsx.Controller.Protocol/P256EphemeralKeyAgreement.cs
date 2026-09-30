using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public sealed class P256EphemeralKeyAgreement : IDisposable
{
    private ECDiffieHellman? _keyAgreement =
        ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public byte[] ExportPublicKey()
    {
        ECDiffieHellman keyAgreement =
            _keyAgreement ??
            throw new ObjectDisposedException(nameof(P256EphemeralKeyAgreement));

        ECParameters parameters = keyAgreement.ExportParameters(false);
        byte[] x = parameters.Q.X
            ?? throw new CryptographicException("P-256 public key is missing X.");
        byte[] y = parameters.Q.Y
            ?? throw new CryptographicException("P-256 public key is missing Y.");

        if (x.Length != 32 || y.Length != 32)
        {
            throw new CryptographicException(
                "P-256 coordinates must be exactly 32 bytes.");
        }

        var encoded = new byte[PairingCrypto.P256UncompressedPublicKeySize];
        encoded[0] = 0x04;
        x.CopyTo(encoded, 1);
        y.CopyTo(encoded, 33);
        return encoded;
    }

    public byte[] DeriveSharedSecret(ReadOnlySpan<byte> remotePublicKey)
    {
        ECDiffieHellman keyAgreement =
            _keyAgreement ??
            throw new ObjectDisposedException(nameof(P256EphemeralKeyAgreement));

        ValidatePublicKey(remotePublicKey);

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = remotePublicKey.Slice(1, 32).ToArray(),
                Y = remotePublicKey.Slice(33, 32).ToArray(),
            },
        };

        using ECDiffieHellman remote = ECDiffieHellman.Create(parameters);
        byte[] secret =
            keyAgreement.DeriveRawSecretAgreement(remote.PublicKey);

        if (secret.Length != PairingCrypto.DerivedKeySize)
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new CryptographicException(
                "P-256 ECDH shared secret must be exactly 32 bytes.");
        }

        return secret;
    }

    public void Dispose()
    {
        _keyAgreement?.Dispose();
        _keyAgreement = null;
        GC.SuppressFinalize(this);
    }

    private static void ValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length != PairingCrypto.P256UncompressedPublicKeySize ||
            publicKey[0] != 0x04)
        {
            throw new ArgumentException(
                "P-256 public key must use 65-byte uncompressed SEC1 encoding.",
                nameof(publicKey));
        }
    }
}
