using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingProtocolTests
{
    private static readonly PairingOfferPayload Offer = new(
        PeerId.FromBytes(Convert.FromHexString("101112131415161718191a1b1c1d1e1f")),
        Convert.FromHexString(
            "202122232425262728292a2b2c2d2e2f" +
            "303132333435363738393a3b3c3d3e3f"),
        Convert.FromHexString(
            "04" +
            "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f" +
            "606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f"));

    private static readonly PairingResponsePayload Response = new(
        PeerId.FromBytes(Convert.FromHexString("808182838485868788898a8b8c8d8e8f")),
        Convert.FromHexString(
            "909192939495969798999a9b9c9d9e9f" +
            "a0a1a2a3a4a5a6a7a8a9aaabacadaeaf"),
        Convert.FromHexString(
            "04" +
            "b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecf" +
            "d0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeef"));

    private static readonly byte[] SharedSecret =
        Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();

    [Fact]
    public void OfferAndResponseRoundTrip()
    {
        PairingOfferPayload decodedOffer =
            PairingExchangePayloadCodec.DecodeOffer(
                PairingExchangePayloadCodec.EncodeOffer(Offer));

        PairingResponsePayload decodedResponse =
            PairingExchangePayloadCodec.DecodeResponse(
                PairingExchangePayloadCodec.EncodeResponse(Response));

        Assert.Equal(Offer.InitiatorPeerId, decodedOffer.InitiatorPeerId);
        Assert.Equal(Offer.InitiatorNonce, decodedOffer.InitiatorNonce);
        Assert.Equal(Offer.InitiatorPublicKey, decodedOffer.InitiatorPublicKey);

        Assert.Equal(Response.ResponderPeerId, decodedResponse.ResponderPeerId);
        Assert.Equal(Response.ResponderNonce, decodedResponse.ResponderNonce);
        Assert.Equal(Response.ResponderPublicKey, decodedResponse.ResponderPublicKey);
    }

    [Fact]
    public void CanonicalPairingDerivationMatchesVector()
    {
        byte[] transcriptHash = PairingCrypto.ComputeTranscriptHash(Offer, Response);
        byte[] trustKey = PairingCrypto.DeriveTrustKey(SharedSecret, transcriptHash);
        string code = PairingCrypto.DeriveSixDigitCode(SharedSecret, transcriptHash);

        Assert.Equal(
            "70fcc403a3a3d7525d16359e622f44caf69d99b62ff77429cc465d776b83aece",
            Convert.ToHexString(transcriptHash).ToLowerInvariant());

        Assert.Equal(
            "0d7312c8a793c72f4f59d0c48aba5ee6e749dbe6f440e0e6eba1e711fa74b1ec",
            Convert.ToHexString(trustKey).ToLowerInvariant());

        Assert.Equal("694336", code);

        byte[] initiatorProof = PairingCrypto.CreateConfirmationProof(
            trustKey,
            transcriptHash,
            PairingRole.Initiator);

        byte[] responderProof = PairingCrypto.CreateConfirmationProof(
            trustKey,
            transcriptHash,
            PairingRole.Responder);

        Assert.Equal(
            "1c875bd84d8dcb3c002d4e2ef206fb122977ab9eaf2924cf67627ee13b5beda9",
            Convert.ToHexString(initiatorProof).ToLowerInvariant());

        Assert.Equal(
            "77aa59d281e0af944592dfd35a60e801459f20508f27768a4ffed97858324562",
            Convert.ToHexString(responderProof).ToLowerInvariant());

        Assert.True(
            PairingCrypto.VerifyConfirmationProof(
                trustKey,
                transcriptHash,
                PairingRole.Responder,
                responderProof));
    }

    [Fact]
    public void PairingConfirmRoundTrips()
    {
        byte[] proof = Enumerable.Range(0, 32)
            .Select(static value => (byte)value)
            .ToArray();

        var expected = new PairingConfirmPayload(
            Offer.InitiatorPeerId,
            PairingRole.Initiator,
            proof);

        PairingConfirmPayload decoded =
            PairingConfirmPayloadCodec.Decode(
                PairingConfirmPayloadCodec.Encode(expected));

        Assert.Equal(expected.PeerId, decoded.PeerId);
        Assert.Equal(expected.Role, decoded.Role);
        Assert.Equal(expected.Proof, decoded.Proof);
    }

    [Fact]
    public void EcdhAgreementIsSymmetric()
    {
        using var left = new PairingKeyAgreement();
        using var right = new PairingKeyAgreement();

        byte[] leftSecret = left.DeriveSharedSecret(right.PublicKey);
        byte[] rightSecret = right.DeriveSharedSecret(left.PublicKey);

        Assert.Equal(PairingCrypto.SharedSecretSize, leftSecret.Length);
        Assert.Equal(leftSecret, rightSecret);
    }

    [Fact]
    public void AbortPayloadRejectsReservedBytes()
    {
        byte[] bytes = PairingAbortPayloadCodec.Encode(
            new PairingAbortPayload(PairingAbortReason.UserRejected));

        Assert.Equal(
            PairingAbortReason.UserRejected,
            PairingAbortPayloadCodec.Decode(bytes).Reason);

        bytes[2] = 1;

        Assert.Throws<FormatException>(
            () => PairingAbortPayloadCodec.Decode(bytes));
    }
}
