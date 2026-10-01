using System.Security.Cryptography;

namespace Natsx.Controller.Protocol.Tests;

public sealed class FirstPairingSessionTests
{
    private const TransportCapabilities AllCapabilities =
        TransportCapabilities.Wifi |
        TransportCapabilities.Bluetooth |
        TransportCapabilities.UsbDirect;

    [Fact]
    public void TwoSidedConfirmation_DerivesSameTrustSecret()
    {
        PeerId androidPeer = PeerId.CreateRandom();
        PeerId windowsPeer = PeerId.CreateRandom();

        using var android =
            new AndroidFirstPairingSession(
                androidPeer,
                AllCapabilities);

        using var windows =
            new WindowsFirstPairingSession(
                windowsPeer,
                AllCapabilities);

        byte[] hello =
            android.CreateHelloFrame(100);

        WindowsPairingResponse response =
            windows.AcceptHelloFrame(
                hello,
                200);

        string androidSas =
            android.AcceptResponseFrame(
                response.FrameBytes);

        Assert.Equal(
            response.Sas,
            androidSas);
        Assert.Matches(
            "^[0-9]{6}$",
            androidSas);

        byte[] confirm =
            android.CreateConfirmFrame(
                userConfirmedSas: true,
                monotonicTimestampMicros: 300);

        WindowsPairingCompletion completion =
            windows.AcceptConfirmFrame(
                confirm,
                userConfirmedSas: true,
                monotonicTimestampMicros: 400);

        using FirstPairingResult windowsResult =
            completion.Result;

        using FirstPairingResult androidResult =
            android.AcceptCompleteFrame(
                completion.FrameBytes);

        Assert.Equal(
            androidPeer,
            windowsResult.RemotePeerId);
        Assert.Equal(
            windowsPeer,
            androidResult.RemotePeerId);

        byte[] windowsSecret =
            windowsResult.CopyTrustSecret();
        byte[] androidSecret =
            androidResult.CopyTrustSecret();

        try
        {
            Assert.Equal(
                windowsSecret,
                androidSecret);
            Assert.Equal(
                PairingCrypto.DerivedKeySize,
                windowsSecret.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                windowsSecret);
            CryptographicOperations.ZeroMemory(
                androidSecret);
        }
    }

    [Fact]
    public void AndroidRejectingSas_DoesNotProduceConfirmFrame()
    {
        using var android =
            new AndroidFirstPairingSession(
                PeerId.CreateRandom(),
                AllCapabilities);
        using var windows =
            new WindowsFirstPairingSession(
                PeerId.CreateRandom(),
                AllCapabilities);

        WindowsPairingResponse response =
            windows.AcceptHelloFrame(
                android.CreateHelloFrame(100),
                200);

        _ = android.AcceptResponseFrame(
            response.FrameBytes);

        Assert.Throws<OperationCanceledException>(
            () =>
                android.CreateConfirmFrame(
                    userConfirmedSas: false,
                    monotonicTimestampMicros: 300));
    }

    [Fact]
    public void WindowsRejectingSas_DoesNotProduceCompletion()
    {
        using var android =
            new AndroidFirstPairingSession(
                PeerId.CreateRandom(),
                AllCapabilities);
        using var windows =
            new WindowsFirstPairingSession(
                PeerId.CreateRandom(),
                AllCapabilities);

        WindowsPairingResponse response =
            windows.AcceptHelloFrame(
                android.CreateHelloFrame(100),
                200);

        _ = android.AcceptResponseFrame(
            response.FrameBytes);

        byte[] confirm =
            android.CreateConfirmFrame(
                userConfirmedSas: true,
                monotonicTimestampMicros: 300);

        Assert.Throws<OperationCanceledException>(
            () =>
                windows.AcceptConfirmFrame(
                    confirm,
                    userConfirmedSas: false,
                    monotonicTimestampMicros: 400));
    }

    [Fact]
    public void ReflectedAndroidConfirmation_CannotActAsWindowsCompletion()
    {
        using var android =
            new AndroidFirstPairingSession(
                PeerId.CreateRandom(),
                AllCapabilities);
        using var windows =
            new WindowsFirstPairingSession(
                PeerId.CreateRandom(),
                AllCapabilities);

        WindowsPairingResponse response =
            windows.AcceptHelloFrame(
                android.CreateHelloFrame(100),
                200);

        _ = android.AcceptResponseFrame(
            response.FrameBytes);

        byte[] confirm =
            android.CreateConfirmFrame(
                userConfirmedSas: true,
                monotonicTimestampMicros: 300);

        ProtocolFrame confirmFrame =
            ProtocolFrameCodec.Decode(
                confirm);

        PairingConfirmationPayload payload =
            PairingPayloadCodec.DecodeConfirmation(
                confirmFrame.Payload);

        byte[] reflected =
            ProtocolFrameCodec.Encode(
                confirmFrame with
                {
                    MessageType =
                        MessageType.PairingComplete,
                    Payload =
                        PairingPayloadCodec.EncodeConfirmation(
                            new PairingConfirmationPayload(
                                response.AndroidPeerId,
                                payload.Proof)),
                });

        Assert.Throws<CryptographicException>(
            () =>
                android.AcceptCompleteFrame(
                    reflected));

        CryptographicOperations.ZeroMemory(
            payload.Proof);
    }
}
