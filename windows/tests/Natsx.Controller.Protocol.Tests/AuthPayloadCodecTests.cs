using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class AuthPayloadCodecTests
{
    [Fact]
    public void FirstPairingChallengeRoundTrip()
    {
        var payload = new AuthChallengePayload(
            AuthenticationMode.FirstPairing,
            PeerId.FromBytes(Range(1, 16)),
            Range(32, 32),
            SessionId.FromBytes(Range(64, 16)),
            PublicKey(96));

        byte[] encoded = AuthPayloadCodec.EncodeChallenge(payload);
        AuthChallengePayload decoded = AuthPayloadCodec.DecodeChallenge(encoded);

        Assert.Equal(AuthPayloadCodec.ChallengePayloadSize, encoded.Length);
        Assert.Equal(payload.Mode, decoded.Mode);
        Assert.Equal(payload.SenderPeerId, decoded.SenderPeerId);
        Assert.Equal(payload.Nonce, decoded.Nonce);
        Assert.Equal(payload.SessionId, decoded.SessionId);
        Assert.Equal(payload.EphemeralPublicKey, decoded.EphemeralPublicKey);
    }

    [Fact]
    public void TrustedReconnectResponseRequiresZeroPublicKey()
    {
        var payload = new AuthResponsePayload(
            AuthenticationMode.TrustedReconnect,
            PeerId.FromBytes(Range(1, 16)),
            Range(32, 32),
            SessionId.FromBytes(Range(64, 16)),
            new byte[PairingCrypto.P256UncompressedPublicKeySize],
            Range(128, 32));

        byte[] encoded = AuthPayloadCodec.EncodeResponse(payload);
        AuthResponsePayload decoded = AuthPayloadCodec.DecodeResponse(encoded);

        Assert.Equal(AuthPayloadCodec.ResponsePayloadSize, encoded.Length);
        Assert.Equal(payload.Mode, decoded.Mode);
        Assert.Equal(payload.Proof, decoded.Proof);
        Assert.All(decoded.EphemeralPublicKey, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void TrustedReconnectRejectsNonZeroPublicKeyField()
    {
        byte[] nonZero = new byte[PairingCrypto.P256UncompressedPublicKeySize];
        nonZero[0] = 1;

        var payload = new AuthChallengePayload(
            AuthenticationMode.TrustedReconnect,
            PeerId.FromBytes(Range(1, 16)),
            Range(32, 32),
            SessionId.FromBytes(Range(64, 16)),
            nonZero);

        Assert.Throws<ArgumentException>(
            () => AuthPayloadCodec.EncodeChallenge(payload));
    }

    private static byte[] Range(int start, int count) =>
        Enumerable.Range(start, count)
            .Select(static value => unchecked((byte)value))
            .ToArray();

    private static byte[] PublicKey(int start) =>
        new byte[] { 0x04 }
            .Concat(Range(start, 64))
            .ToArray();
}
