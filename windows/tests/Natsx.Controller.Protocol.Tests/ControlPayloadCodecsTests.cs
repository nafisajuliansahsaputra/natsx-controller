using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class ControlPayloadCodecsTests
{
    [Theory]
    [InlineData(TransportPreferenceMode.Auto)]
    [InlineData(TransportPreferenceMode.Wifi)]
    [InlineData(TransportPreferenceMode.Bluetooth)]
    [InlineData(TransportPreferenceMode.UsbDirect)]
    public void TransportPreferenceRoundTrips(
        TransportPreferenceMode mode)
    {
        byte[] bytes =
            TransportPreferencePayloadCodec
                .Encode(
                    new TransportPreferencePayload(
                        mode));

        TransportPreferencePayload decoded =
            TransportPreferencePayloadCodec
                .Decode(bytes);

        Assert.Equal(
            mode,
            decoded.Mode);
        Assert.Equal(
            new byte[]
            {
                (byte)mode,
                0,
                0,
                0,
            },
            bytes);
    }

    [Fact]
    public void HeartbeatAckRoundTripsTimestamp()
    {
        const ulong timestamp = 0x0102030405060708UL;
        byte[] bytes = HeartbeatAckPayloadCodec.Encode(new(timestamp));
        HeartbeatAckPayload decoded = HeartbeatAckPayloadCodec.Decode(bytes);

        Assert.Equal(HeartbeatAckPayloadCodec.PayloadSize, bytes.Length);
        Assert.Equal(timestamp, decoded.EchoedTimestampMicros);
    }

    [Theory]
    [InlineData(ProtocolTransport.Wifi)]
    [InlineData(ProtocolTransport.Bluetooth)]
    [InlineData(ProtocolTransport.UsbDirect)]
    public void TransportReadyRoundTrips(ProtocolTransport transport)
    {
        byte[] bytes = TransportReadyPayloadCodec.Encode(new(transport));
        TransportReadyPayload decoded = TransportReadyPayloadCodec.Decode(bytes);

        Assert.Equal(transport, decoded.Transport);
        Assert.Equal(new byte[] { (byte)transport, 0, 0, 0 }, bytes);
    }

    [Fact]
    public void HandoverRoundTripsTransportAndSequence()
    {
        var expected = new HandoverPayload(
            ProtocolTransport.Bluetooth,
            0x89abcdef);

        byte[] bytes = HandoverPayloadCodec.Encode(expected);
        HandoverPayload decoded = HandoverPayloadCodec.Decode(bytes);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void RumbleRoundTrips()
    {
        var expected = new RumblePayload(17, 250);

        byte[] bytes = RumblePayloadCodec.Encode(expected);
        RumblePayload decoded = RumblePayloadCodec.Decode(bytes);

        Assert.Equal(expected, decoded);
        Assert.Equal(0, bytes[2]);
        Assert.Equal(0, bytes[3]);
    }

    [Theory]
    [InlineData(DisconnectReason.Normal)]
    [InlineData(DisconnectReason.AppStopping)]
    [InlineData(DisconnectReason.TransportClosing)]
    [InlineData(DisconnectReason.ProtocolError)]
    [InlineData(DisconnectReason.AuthenticationFailed)]
    public void DisconnectRoundTrips(DisconnectReason reason)
    {
        byte[] bytes = DisconnectPayloadCodec.Encode(new(reason));
        DisconnectPayload decoded = DisconnectPayloadCodec.Decode(bytes);

        Assert.Equal(reason, decoded.Reason);
    }

    [Fact]
    public void ReservedBytesAreRejected()
    {
        Assert.Throws<FormatException>(
            () => TransportPreferencePayloadCodec.Decode([1, 1, 0, 0]));

        Assert.Throws<FormatException>(
            () => TransportReadyPayloadCodec.Decode([1, 1, 0, 0]));

        Assert.Throws<FormatException>(
            () => HandoverPayloadCodec.Decode([1, 0, 1, 0, 0, 0, 0, 0]));

        Assert.Throws<FormatException>(
            () => RumblePayloadCodec.Decode([1, 2, 0, 1]));

        Assert.Throws<FormatException>(
            () => DisconnectPayloadCodec.Decode([0, 0, 1, 0]));
    }
}

public sealed class TrustedReconnectCryptoTests
{
    private static readonly byte[] TrustKey =
        Convert.FromHexString(
            "000102030405060708090a0b0c0d0e0f" +
            "101112131415161718191a1b1c1d1e1f");

    private static readonly SessionId SessionIdValue =
        SessionId.FromBytes(
            Convert.FromHexString("00112233445566778899aabbccddeeff"));

    private static readonly byte[] Challenge =
        Convert.FromHexString(
            "202122232425262728292a2b2c2d2e2f" +
            "303132333435363738393a3b3c3d3e3f");

    private static readonly PeerId Challenger =
        PeerId.FromBytes(Convert.FromHexString("404142434445464748494a4b4c4d4e4f"));

    private static readonly PeerId Responder =
        PeerId.FromBytes(Convert.FromHexString("505152535455565758595a5b5c5d5e5f"));

    [Fact]
    public void ChallengeAndResponsePayloadsRoundTrip()
    {
        var challengePayload = new AuthChallengePayload(
            Challenger,
            Responder,
            Challenge);

        AuthChallengePayload decodedChallenge =
            AuthChallengePayloadCodec.Decode(
                AuthChallengePayloadCodec.Encode(challengePayload));

        Assert.Equal(challengePayload.ChallengerPeerId, decodedChallenge.ChallengerPeerId);
        Assert.Equal(challengePayload.TargetPeerId, decodedChallenge.TargetPeerId);
        Assert.Equal(challengePayload.ChallengeNonce, decodedChallenge.ChallengeNonce);

        byte[] proof = TrustedReconnectCrypto.CreateProof(
            TrustKey,
            SessionIdValue,
            Challenge,
            Challenger,
            Responder);

        var responsePayload = new AuthResponsePayload(
            Responder,
            Challenger,
            proof);

        AuthResponsePayload decodedResponse =
            AuthResponsePayloadCodec.Decode(
                AuthResponsePayloadCodec.Encode(responsePayload));

        Assert.Equal(responsePayload.ResponderPeerId, decodedResponse.ResponderPeerId);
        Assert.Equal(responsePayload.ChallengerPeerId, decodedResponse.ChallengerPeerId);
        Assert.Equal(responsePayload.Proof, decodedResponse.Proof);
    }

    [Fact]
    public void CanonicalProofMatchesCrossLanguageVector()
    {
        byte[] proof = TrustedReconnectCrypto.CreateProof(
            TrustKey,
            SessionIdValue,
            Challenge,
            Challenger,
            Responder);

        Assert.Equal(
            "7c1d330d72acdce1dd6cdcb2d43d3238b45ed559b8b70c5f80126e984156fa4b",
            Convert.ToHexString(proof).ToLowerInvariant());

        Assert.True(
            TrustedReconnectCrypto.VerifyProof(
                TrustKey,
                SessionIdValue,
                Challenge,
                Challenger,
                Responder,
                proof));
    }

    [Fact]
    public void CanonicalSessionKeyMatchesCrossLanguageVector()
    {
        byte[] sessionKey = TrustedReconnectCrypto.DeriveSessionKey(
            TrustKey,
            SessionIdValue,
            Challenge,
            Challenger,
            Responder);

        Assert.Equal(
            "7f71083bd8b16f7e75f276bb9ebb7551060bf5128c5c343b2634584f5a3200f2",
            Convert.ToHexString(sessionKey).ToLowerInvariant());
    }

    [Fact]
    public void ProofRejectsDifferentSession()
    {
        byte[] proof = TrustedReconnectCrypto.CreateProof(
            TrustKey,
            SessionIdValue,
            Challenge,
            Challenger,
            Responder);

        SessionId other =
            SessionId.FromBytes(Convert.FromHexString("10112233445566778899aabbccddeeff"));

        Assert.False(
            TrustedReconnectCrypto.VerifyProof(
                TrustKey,
                other,
                Challenge,
                Challenger,
                Responder,
                proof));
    }
}
