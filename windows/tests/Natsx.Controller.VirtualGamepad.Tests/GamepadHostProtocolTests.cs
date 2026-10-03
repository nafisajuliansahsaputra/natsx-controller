using Natsx.Controller.Core;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.VirtualGamepad.Tests;

public sealed class GamepadHostProtocolTests
{
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1 })]
    [InlineData(new byte[] { 2, 0 })]
    [InlineData(new byte[] { 3 })]
    public void ReadyRejectsLegacyOrIncompatibleHost(byte[] payload)
    {
        Assert.Throws<InvalidOperationException>(() => GamepadHostProtocol.ValidateReady(payload));
    }

    [Fact]
    public void ReadyAcceptsCurrentInputMapping()
    {
        GamepadHostProtocol.ValidateReady(new byte[] { GamepadHostProtocol.InputMappingRevision });
    }

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
        byte[] payload =
            new byte[
                GamepadHostProtocol.GamepadStatePayloadSize];

        Array.Clear(payload);
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
        byte[] payload =
            new byte[
                GamepadHostProtocol.GamepadStatePayloadSize];

        Array.Clear(payload);
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
        byte[] header =
            new byte[
                GamepadHostProtocol.HeaderSize];

        Array.Clear(header);

        Assert.Throws<FormatException>(
            () =>
                GamepadHostProtocol.DecodeHeader(
                    header));
    }
}
