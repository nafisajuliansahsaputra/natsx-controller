using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class P256EphemeralKeyAgreementTests
{
    [Fact]
    public void BothPeersDeriveSameRawSharedSecret()
    {
        using var android = new P256EphemeralKeyAgreement();
        using var windows = new P256EphemeralKeyAgreement();

        byte[] androidPublic = android.ExportPublicKey();
        byte[] windowsPublic = windows.ExportPublicKey();
        byte[] androidSecret = android.DeriveSharedSecret(windowsPublic);
        byte[] windowsSecret = windows.DeriveSharedSecret(androidPublic);

        try
        {
            Assert.Equal(
                PairingCrypto.P256UncompressedPublicKeySize,
                androidPublic.Length);
            Assert.Equal(0x04, androidPublic[0]);
            Assert.Equal(PairingCrypto.DerivedKeySize, androidSecret.Length);
            Assert.Equal(androidSecret, windowsSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(androidSecret);
            CryptographicOperations.ZeroMemory(windowsSecret);
        }
    }
}
