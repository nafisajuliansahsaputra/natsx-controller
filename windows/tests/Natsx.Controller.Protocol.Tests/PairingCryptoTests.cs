using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingCryptoTests
{
    [Fact]
    public void CanonicalDerivationsMatchV1Vector()
    {
        PeerId androidPeerId = PeerId.FromBytes(Range(0x00, 16));
        PeerId windowsPeerId = PeerId.FromBytes(Range(0x10, 16));
        byte[] androidNonce = Range(0x20, 32);
        byte[] windowsNonce = Range(0x40, 32);
        byte[] androidPublicKey = PublicKey(0x60);
        byte[] windowsPublicKey = PublicKey(0xA0);
        byte[] sharedSecret = Range(0xC0, 32);
        SessionId sessionId = SessionId.FromBytes(Range(0xE0, 16));

        byte[] transcriptHash =
            PairingCrypto.ComputePairingTranscriptHash(
                androidPeerId,
                windowsPeerId,
                androidNonce,
                windowsNonce,
                androidPublicKey,
                windowsPublicKey);

        Assert.Equal(
            "ae3a44b49aa7a34878ed64efa63cfb5d3a06b041515fe3fb77a93f14377a6bb0",
            Convert.ToHexString(transcriptHash).ToLowerInvariant());

        byte[] pairingKey =
            PairingCrypto.DerivePairingKey(
                sharedSecret,
                transcriptHash);

        Assert.Equal(
            "8a12e9de1c2df8d5e83d7ab02effffc2d543d52af0da156fd912138dc8c791cb",
            Convert.ToHexString(pairingKey).ToLowerInvariant());

        Assert.Equal(
            "432656",
            PairingCrypto.DeriveSixDigitSas(
                pairingKey,
                transcriptHash));

        byte[] pairingResponseProof =
            PairingCrypto.ComputePairingResponseProof(
                pairingKey,
                transcriptHash,
                sessionId);

        Assert.Equal(
            "69168c6fb8ab71e31e4cabd994c045261bbba267427408bfc3ad11a9e445dc6b",
            Convert.ToHexString(pairingResponseProof).ToLowerInvariant());

        byte[] trustSecret =
            PairingCrypto.DeriveTrustSecret(
                pairingKey,
                transcriptHash);

        Assert.Equal(
            "014850a457b18a4a55758249ade00423852dfb44a6c4a9a8bc422a28379ede4f",
            Convert.ToHexString(trustSecret).ToLowerInvariant());

    }

    private static byte[] Range(
        int start,
        int count)
    {
        return Enumerable.Range(start, count)
            .Select(static value => unchecked((byte)value))
            .ToArray();
    }

    private static byte[] PublicKey(int start)
    {
        return new byte[] { 0x04 }
            .Concat(Range(start, 64))
            .ToArray();
    }
}
