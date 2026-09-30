namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingPayloadCodecTests
{
    [Fact]
    public void PairRequest_RoundTrips()
    {
        var payload = new PairRequestPayload(
            PeerId.FromBytes(Range(0x10, 16)),
            Range(0x20, 16),
            1_790_800_000,
            Range(0x30, 16));

        PairRequestPayload decoded = PairingPayloadCodec.DecodePairRequest(
            PairingPayloadCodec.EncodePairRequest(payload));

        Assert.Equal(payload.ControllerPeerId, decoded.ControllerPeerId);
        Assert.Equal(payload.ClientNonce, decoded.ClientNonce);
        Assert.Equal(payload.ExpiresUnixSeconds, decoded.ExpiresUnixSeconds);
        Assert.Equal(payload.Proof, decoded.Proof);
    }

    [Fact]
    public void AuthPayloads_RoundTrip()
    {
        var challenge = new AuthChallengePayload(
            PeerId.FromBytes(Range(0x00, 16)),
            Range(0x10, 16),
            Range(0x20, 16));

        AuthChallengePayload decodedChallenge =
            PairingPayloadCodec.DecodeAuthChallenge(
                PairingPayloadCodec.EncodeAuthChallenge(challenge));

        Assert.Equal(challenge.ControllerPeerId, decodedChallenge.ControllerPeerId);
        Assert.Equal(challenge.ClientNonce, decodedChallenge.ClientNonce);
        Assert.Equal(challenge.Proof, decodedChallenge.Proof);

        var response = new AuthResponsePayload(
            Range(0x30, 16),
            Range(0x40, 16));

        AuthResponsePayload decodedResponse =
            PairingPayloadCodec.DecodeAuthResponse(
                PairingPayloadCodec.EncodeAuthResponse(response));

        Assert.Equal(response.ServerNonce, decodedResponse.ServerNonce);
        Assert.Equal(response.Proof, decodedResponse.Proof);
    }

    private static byte[] Range(int start, int count) =>
        Enumerable.Range(start, count).Select(static value => checked((byte)value)).ToArray();
}
