namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingCryptoTests
{
    [Fact]
    public void PairingAndSessionCrypto_MatchesCanonicalVector()
    {
        PeerId receiver = PeerId.FromBytes(Range(0x00, 16));
        PeerId controller = PeerId.FromBytes(Range(0x10, 16));
        byte[] pairingSecret = Range(0x20, 32);
        byte[] clientNonce = Range(0x40, 16);
        byte[] serverNonce = Range(0x50, 16);

        byte[] requestProof = PairingCrypto.CreatePairRequestProof(
            pairingSecret,
            receiver,
            controller,
            clientNonce,
            1_790_800_000);

        byte[] trustKey = PairingCrypto.DeriveTrustKey(
            pairingSecret,
            receiver,
            controller,
            clientNonce,
            serverNonce);

        byte[] responseProof = PairingCrypto.CreatePairResponseProof(
            trustKey,
            receiver,
            controller,
            clientNonce,
            serverNonce);

        Assert.Equal(
            "bb630e42793bb36ff773d9de62e8058e",
            Convert.ToHexString(requestProof).ToLowerInvariant());
        Assert.Equal(
            "50a4b0b28b9254932f8cfdcbac11fff9bf7621e7767f9d09d7741f1b1cf78870",
            Convert.ToHexString(trustKey).ToLowerInvariant());
        Assert.Equal(
            "30d0d724bcdd18fdefcdfb2de30e086a",
            Convert.ToHexString(responseProof).ToLowerInvariant());

        SessionId sessionId = SessionId.FromBytes(Range(0x60, 16));
        byte[] authClientNonce = Range(0x70, 16);
        byte[] authServerNonce = Range(0x80, 16);

        byte[] challenge = PairingCrypto.CreateAuthChallengeProof(
            trustKey,
            sessionId,
            receiver,
            controller,
            authClientNonce);

        byte[] sessionKey = PairingCrypto.DeriveSessionKey(
            trustKey,
            sessionId,
            receiver,
            controller,
            authClientNonce,
            authServerNonce);

        byte[] authResponse = PairingCrypto.CreateAuthResponseProof(
            trustKey,
            sessionId,
            receiver,
            controller,
            authClientNonce,
            authServerNonce);

        byte[] readyProof = PairingCrypto.CreateSessionReadyProof(
            sessionKey,
            sessionId);

        Assert.Equal(
            "9698761f91a8188a24a57404f52017a7",
            Convert.ToHexString(challenge).ToLowerInvariant());
        Assert.Equal(
            "6137b4f1b50b2a01b5d011e2f5044927b8dd9cdb8360f6a667879e7323f0b436",
            Convert.ToHexString(sessionKey).ToLowerInvariant());
        Assert.Equal(
            "be8625f1f500634e4c53cc5ea7cfe004",
            Convert.ToHexString(authResponse).ToLowerInvariant());
        Assert.Equal(
            "f04f0d7ff1cd04b7658d7f72aa733deb",
            Convert.ToHexString(readyProof).ToLowerInvariant());
    }

    [Fact]
    public void FixedTimeEquals_RejectsModifiedProof()
    {
        byte[] expected = Range(0x00, PairingCrypto.ProofSize);
        byte[] actual = expected.ToArray();
        actual[^1] ^= 0x01;

        Assert.False(PairingCrypto.FixedTimeEquals(expected, actual));
    }

    private static byte[] Range(int start, int count) =>
        Enumerable.Range(start, count).Select(static value => checked((byte)value)).ToArray();
}
