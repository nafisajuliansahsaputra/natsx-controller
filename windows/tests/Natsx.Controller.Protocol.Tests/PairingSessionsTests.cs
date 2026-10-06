using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingSessionsTests
{
    [Fact]
    public void BothPeers_ProduceSameCodeAndTrustSecret()
    {
        PeerId android =
            PeerId.CreateRandom();
        PeerId windows =
            PeerId.CreateRandom();

        using var initiator =
            new PairingInitiatorSession(
                android);

        PairingOfferPayload offer =
            initiator.Offer;

        using var responder =
            new PairingResponderSession(
                windows,
                offer);

        string initiatorCode =
            initiator.AcceptResponse(
                responder.Response);

        Assert.Equal(
            responder.ComparisonCode,
            initiatorCode);

        PairingConfirmPayload androidConfirm =
            initiator.ApproveDisplayedCode();

        PairingConfirmPayload windowsConfirm =
            responder.ApproveDisplayedCode();

        using PairingEstablishedMaterial androidMaterial =
            initiator.AcceptRemoteConfirmation(
                windowsConfirm);

        using PairingEstablishedMaterial windowsMaterial =
            responder.AcceptRemoteConfirmation(
                androidConfirm);

        Assert.Equal(
            windows,
            androidMaterial.RemotePeerId);

        Assert.Equal(
            android,
            windowsMaterial.RemotePeerId);

        byte[] androidSecret =
            androidMaterial.CopyTrustSecret();

        byte[] windowsSecret =
            windowsMaterial.CopyTrustSecret();

        try
        {
            Assert.Equal(
                androidSecret,
                windowsSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                androidSecret);
            CryptographicOperations.ZeroMemory(
                windowsSecret);
        }
    }

    [Fact]
    public void WrongRoleConfirmation_IsRejected()
    {
        PeerId android =
            PeerId.CreateRandom();
        PeerId windows =
            PeerId.CreateRandom();

        using var initiator =
            new PairingInitiatorSession(
                android);

        using var responder =
            new PairingResponderSession(
                windows,
                initiator.Offer);

        initiator.AcceptResponse(
            responder.Response);

        initiator.ApproveDisplayedCode();

        PairingConfirmPayload responderConfirm =
            responder.ApproveDisplayedCode();

        var wrongRole =
            responderConfirm with
            {
                Role =
                    PairingRole.AndroidController,
            };

        Assert.Throws<CryptographicException>(
            () =>
                initiator
                    .AcceptRemoteConfirmation(
                        wrongRole));
    }

    [Fact]
    public void PairingFrames_RequireZeroSessionAndUnauthenticatedEnvelope()
    {
        PeerId android =
            PeerId.CreateRandom();

        using var initiator =
            new PairingInitiatorSession(
                android);

        byte[] encoded =
            PairingFrameCodec.EncodeOffer(
                initiator.Offer,
                123);

        PairingOfferPayload decoded =
            PairingFrameCodec.DecodeOffer(
                encoded);

        Assert.Equal(
            android,
            decoded.AndroidPeerId);

        ProtocolFrame frame =
            ProtocolFrameCodec.Decode(
                encoded);

        Assert.Equal(
            MessageType.PairingOffer,
            frame.MessageType);
        Assert.Equal(
            FrameFlags.None,
            frame.Flags);
        Assert.Equal(
            SessionId.Zero,
            frame.SessionId);
        Assert.Equal(
            (uint)0,
            frame.Sequence);
    }
}
