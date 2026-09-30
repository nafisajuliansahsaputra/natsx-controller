using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class TrustedSessionCryptoTests
{
    [Fact]
    public void SessionKeyAndProof_MatchSharedVector()
    {
        byte[] rootKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        byte[] challenge = Enumerable.Range(32, 32).Select(value => (byte)value).ToArray();

        SessionId androidDeviceId = SessionId.FromBytes(
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));
        SessionId windowsDeviceId = SessionId.FromBytes(
            Convert.FromHexString("FFEEDDCCBBAA99887766554433221100"));
        SessionId sessionId = SessionId.FromBytes(
            Convert.FromHexString("102132435465768798A9BACBDCEDFE0F"));

        byte[] sessionKey = TrustedSessionCrypto.DeriveSessionKey(
            rootKey,
            challenge,
            androidDeviceId,
            windowsDeviceId,
            sessionId);

        Assert.Equal(
            "6c6643338f73fab694ad625740b63c2887fb1fa2088d936c4223c68aa9edcb3f",
            Convert.ToHexString(sessionKey).ToLowerInvariant());

        byte[] androidHello = Convert.FromHexString(
            "00112233445566778899AABBCCDDEEFF01070D00000001010001000000000000");
        byte[] windowsHello = Convert.FromHexString(
            "FFEEDDCCBBAA9988776655443322110002070D00000001010001000000000000");

        byte[] proof = TrustedSessionCrypto.ComputeAndroidProof(
            sessionKey,
            androidHello,
            windowsHello,
            challenge,
            sessionId);

        Assert.Equal(
            "f087a2a2731e681b4d1a767396547c25",
            Convert.ToHexString(proof).ToLowerInvariant());

        Assert.True(TrustedSessionCrypto.VerifyAndroidProof(proof, proof));

        byte[] wrong = proof.ToArray();
        wrong[0] ^= 0x01;
        Assert.False(TrustedSessionCrypto.VerifyAndroidProof(proof, wrong));
    }
}
