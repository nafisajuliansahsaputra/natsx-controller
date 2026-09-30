using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class GamepadStateCodecTests
{
    [Fact]
    public void Encode_CanonicalState_MatchesVector()
    {
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

        byte[] actual = GamepadStateCodec.Encode(state);

        Assert.Equal(
            "290205000080ff7fc7cf393011fa0000",
            Convert.ToHexString(actual).ToLowerInvariant());
    }

    [Fact]
    public void Decode_Encode_RoundTrips()
    {
        var expected = new GamepadState(
            GamepadButtons.A | GamepadButtons.LeftShoulder | GamepadButtons.RightStick,
            DpadState.Down | DpadState.Right,
            -12000,
            23000,
            4400,
            -5500,
            99,
            201);

        byte[] payload = GamepadStateCodec.Encode(expected);
        GamepadState actual = GamepadStateCodec.Decode(payload);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decode_RejectsReservedBytes()
    {
        var payload = new byte[ProtocolConstants.GamepadStatePayloadSize];
        payload[3] = 1;

        Assert.Throws<FormatException>(() => GamepadStateCodec.Decode(payload));
    }

    [Fact]
    public void Decode_RejectsOppositeDpadDirections()
    {
        var payload = new byte[ProtocolConstants.GamepadStatePayloadSize];
        payload[2] = (byte)(DpadState.Up | DpadState.Down);

        Assert.Throws<FormatException>(() => GamepadStateCodec.Decode(payload));
    }
}
