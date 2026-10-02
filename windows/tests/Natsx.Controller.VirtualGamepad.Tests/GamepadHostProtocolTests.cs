using Natsx.Controller.Core;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.VirtualGamepad.Tests;

public sealed class GamepadHostProtocolTests
{
    [Fact]
    public void GamepadState_RoundTrips()
    {
        var expected =
            new GamepadState(
                GamepadButtons.A |
                GamepadButtons.RightShoulder |
                GamepadButtons.Guide,
                DpadState.Up |
                DpadState.Right,
                short.MinValue,
                short.MaxValue,
                -1234,
                2345,
                17,
                249);

        byte[] payload =
            new byte[
                GamepadHostProtocol.GamepadStatePayloadSize];

        GamepadHostProtocol.EncodeGamepadState(
            expected,
            payload);

        GamepadState actual =
            GamepadHostProtocol.DecodeGamepadState(
                payload);

        Assert.Equal(
            expected,
            actual);
    }

    [Fact]
    public void DecodeGamepadState_RejectsUnknownButtonBits()
    {
        Span<byte> payload =
            stackalloc byte[
                GamepadHostProtocol.GamepadStatePayloadSize];

        payload.Clear();
        payload[1] =
            0x80;

        Assert.Throws<FormatException>(
            () =>
                GamepadHostProtocol.DecodeGamepadState(
                    payload));
    }

    [Fact]
    public void DecodeGamepadState_RejectsOpposingDpadDirections()
    {
        Span<byte> payload =
            stackalloc byte[
                GamepadHostProtocol.GamepadStatePayloadSize];

        payload.Clear();
        payload[2] =
            (byte)(
                DpadState.Up |
                DpadState.Down);

        Assert.Throws<FormatException>(
            () =>
                GamepadHostProtocol.DecodeGamepadState(
                    payload));
    }

    [Fact]
    public void Header_RoundTrips()
    {
        byte[] header =
            new byte[
                GamepadHostProtocol.HeaderSize];

        GamepadHostProtocol.EncodeHeader(
            header,
            GamepadHostMessageType.GamepadState,
            GamepadHostProtocol.GamepadStatePayloadSize);

        (
            GamepadHostMessageType messageType,
            ushort payloadLength) =
            GamepadHostProtocol.DecodeHeader(
                header);

        Assert.Equal(
            GamepadHostMessageType.GamepadState,
            messageType);

        Assert.Equal(
            GamepadHostProtocol.GamepadStatePayloadSize,
            payloadLength);
    }

    [Fact]
    public void Header_RejectsInvalidMagic()
    {
        Span<byte> header =
            stackalloc byte[
                GamepadHostProtocol.HeaderSize];

        header.Clear();

        Assert.Throws<FormatException>(
            () =>
                GamepadHostProtocol.DecodeHeader(
                    header));
    }
}
