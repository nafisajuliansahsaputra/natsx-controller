using System.Security.Cryptography;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiRealtimeDatagramCodecTests
{
    private static readonly byte[] Key =
        Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();

    private static readonly SessionId Session =
        SessionId.FromBytes(Convert.FromHexString(
            "00112233445566778899AABBCCDDEEFF"));

    [Fact]
    public void DecodeGamepadState_AcceptsAuthenticatedTrustedSession()
    {
        using var trusted = new WifiTrustedSession(Session, Key);

        var expectedState = new GamepadState(
            GamepadButtons.A | GamepadButtons.RightShoulder,
            DpadState.Up | DpadState.Left,
            -1000,
            2000,
            -3000,
            4000,
            17,
            250);

        byte[] frame = ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                Session,
                42,
                123456,
                GamepadStateCodec.Encode(expectedState)),
            Key);

        WifiGamepadDatagram decoded =
            WifiRealtimeDatagramCodec.DecodeGamepadState(frame, trusted);

        Assert.Equal(42u, decoded.Sequence);
        Assert.Equal(123456ul, decoded.RemoteMonotonicTimestampMicros);
        Assert.Equal(expectedState, decoded.State);
    }

    [Fact]
    public void DecodeGamepadState_RejectsDifferentSession()
    {
        var otherSession = SessionId.FromBytes(Convert.FromHexString(
            "FFEEDDCCBBAA99887766554433221100"));

        using var trusted = new WifiTrustedSession(otherSession, Key);

        byte[] frame = ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                Session,
                1,
                1,
                GamepadStateCodec.Encode(GamepadState.Neutral)),
            Key);

        Assert.Throws<CryptographicException>(
            () => WifiRealtimeDatagramCodec.DecodeGamepadState(frame, trusted));
    }

    [Fact]
    public void DecodeGamepadState_RejectsUnauthenticatedFrame()
    {
        using var trusted = new WifiTrustedSession(Session, Key);

        byte[] frame = ProtocolFrameCodec.Encode(
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.None,
                Session,
                1,
                1,
                GamepadStateCodec.Encode(GamepadState.Neutral)));

        Assert.Throws<CryptographicException>(
            () => WifiRealtimeDatagramCodec.DecodeGamepadState(frame, trusted));
    }

    [Fact]
    public void TrustedSession_RequiresFull256BitSessionKey()
    {
        Assert.Throws<ArgumentException>(
            () => new WifiTrustedSession(Session, new byte[16]));
    }
}
