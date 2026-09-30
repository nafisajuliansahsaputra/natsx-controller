using System.Security.Cryptography;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class ProtocolFrameCodecTests
{
    private static readonly byte[] SessionKey =
        Convert.FromHexString(
            "000102030405060708090A0B0C0D0E0F" +
            "101112131415161718191A1B1C1D1E1F");

    [Fact]
    public void Encode_CanonicalAuthenticatedGamepadFrame_MatchesVector()
    {
        var sessionId = SessionId.FromBytes(
            Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

        var state = new GamepadState(
            GamepadButtons.A |
            GamepadButtons.Y |
            GamepadButtons.RightShoulder |
            GamepadButtons.Start,
            DpadState.Up | DpadState.Left,
            short.MinValue,
            short.MaxValue,
            -12345,
            12345,
            17,
            250);

        var frame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.GamepadState,
            FrameFlags.Authenticated,
            sessionId,
            0x01020304u,
            0x0102030405060708ul,
            GamepadStateCodec.Encode(state));

        byte[] actual = ProtocolFrameCodec.Encode(frame, SessionKey);

        Assert.Equal(
            "4e584331010007012800100000112233445566778899aabbccddeeff040302010807060504030201290205000080ff7fc7cf393011fa0000200bd07c33ca7384f4d02a0c8dfad9ba21f4d6be",
            Convert.ToHexString(actual).ToLowerInvariant());
    }

    [Fact]
    public void Decode_CanonicalAuthenticatedGamepadFrame_RoundTrips()
    {
        byte[] encoded = Convert.FromHexString(
            "4E584331010007012800100000112233445566778899AABBCCDDEEFF040302010807060504030201290205000080FF7FC7CF393011FA0000200BD07C33CA7384F4D02A0C8DFAD9BA21F4D6BE");

        ProtocolFrame frame = ProtocolFrameCodec.Decode(encoded, SessionKey);
        GamepadState state = GamepadStateCodec.Decode(frame.Payload);

        Assert.Equal(ProtocolVersion.Current, frame.Version);
        Assert.Equal(MessageType.GamepadState, frame.MessageType);
        Assert.Equal(FrameFlags.Authenticated, frame.Flags);
        Assert.Equal(0x01020304u, frame.Sequence);
        Assert.Equal(0x0102030405060708ul, frame.MonotonicTimestampMicros);
        Assert.True(state.Buttons.HasFlag(GamepadButtons.A));
        Assert.True(state.Buttons.HasFlag(GamepadButtons.Y));
        Assert.Equal(short.MinValue, state.LeftX);
        Assert.Equal((byte)250, state.RightTrigger);
    }

    [Fact]
    public void Decode_RejectsTamperedAuthenticatedFrame()
    {
        var sessionId = SessionId.FromBytes(new byte[16]);
        var frame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.GamepadState,
            FrameFlags.Authenticated,
            sessionId,
            1,
            1,
            GamepadStateCodec.Encode(GamepadState.Neutral));

        byte[] encoded = ProtocolFrameCodec.Encode(frame, SessionKey);
        encoded[ProtocolConstants.HeaderSize + 4] ^= 0x01;

        Assert.Throws<FormatException>(() => ProtocolFrameCodec.Decode(encoded, SessionKey));
    }

    [Fact]
    public void Decode_RejectsWrongAuthenticationKey()
    {
        var sessionId = SessionId.FromBytes(new byte[16]);
        var frame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.GamepadState,
            FrameFlags.Authenticated,
            sessionId,
            1,
            1,
            GamepadStateCodec.Encode(GamepadState.Neutral));

        byte[] encoded = ProtocolFrameCodec.Encode(frame, SessionKey);
        byte[] wrongKey = RandomNumberGenerator.GetBytes(32);

        Assert.Throws<CryptographicException>(() => ProtocolFrameCodec.Decode(encoded, wrongKey));
    }
}
