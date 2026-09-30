using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingSessionsTests
{
    [Fact]
    public void InitiatorAndResponderCompleteWithSameTrustKey()
    {
        PeerId initiatorPeer = PeerId.CreateRandom();
        PeerId responderPeer = PeerId.CreateRandom();

        using var initiator = new PairingInitiatorSession(initiatorPeer);
        using var responder = new PairingResponderSession(
            responderPeer,
            initiator.Offer);

        string initiatorCode = initiator.AcceptResponse(responder.Response);

        Assert.Equal(responder.ComparisonCode, initiatorCode);

        PairingConfirmPayload initiatorConfirm =
            initiator.ApproveDisplayedCode();

        PairingConfirmPayload responderConfirm =
            responder.ApproveDisplayedCode();

        using PairingEstablishedMaterial initiatorMaterial =
            initiator.AcceptRemoteConfirmation(responderConfirm);

        using PairingEstablishedMaterial responderMaterial =
            responder.AcceptRemoteConfirmation(initiatorConfirm);

        Assert.Equal(responderPeer, initiatorMaterial.RemotePeerId);
        Assert.Equal(initiatorPeer, responderMaterial.RemotePeerId);
        Assert.Equal(
            initiatorMaterial.ExportTrustKey(),
            responderMaterial.ExportTrustKey());
    }

    [Fact]
    public void WrongRemoteConfirmationIsRejected()
    {
        using var initiator =
            new PairingInitiatorSession(PeerId.CreateRandom());

        using var responder =
            new PairingResponderSession(
                PeerId.CreateRandom(),
                initiator.Offer);

        initiator.AcceptResponse(responder.Response);
        initiator.ApproveDisplayedCode();

        byte[] fakeProof = new byte[PairingConfirmPayloadCodec.ProofSize];

        var wrong = new PairingConfirmPayload(
            responder.RemotePeerId,
            PairingRole.Responder,
            fakeProof);

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => initiator.AcceptRemoteConfirmation(wrong));
    }

    [Fact]
    public void CannotCompleteBeforeLocalApproval()
    {
        using var initiator =
            new PairingInitiatorSession(PeerId.CreateRandom());

        using var responder =
            new PairingResponderSession(
                PeerId.CreateRandom(),
                initiator.Offer);

        initiator.AcceptResponse(responder.Response);
        PairingConfirmPayload responderConfirm =
            responder.ApproveDisplayedCode();

        Assert.Throws<InvalidOperationException>(
            () => initiator.AcceptRemoteConfirmation(responderConfirm));
    }
}
