using System.Buffers.Binary;
using Natsx.Controller.Core;

namespace Natsx.Controller.VirtualGamepad;

public enum GamepadHostMessageType : byte
{
    Hello = 1,
    Ready = 2,
    GamepadState = 3,
    Rumble = 4,
    Error = 5,
}

public static class GamepadHostProtocol
{
    public const string PipeName =
        "NATSX.Controller.GamepadHost.v1";

    public const byte Version = 1;
    // READY must attest the Y-axis mapping implemented by the installed host.
    // Old hosts emitted an empty READY and could silently run old input code.
    public const byte InputMappingRevision = 2;
    public const int HeaderSize = 8;
    public const int GamepadStatePayloadSize = 13;
    public const int RumblePayloadSize = 2;
    public const int MaximumPayloadSize = 256;

    private const uint Magic = 0x3158544E;

    public static void ValidateReady(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 1 || payload[0] != InputMappingRevision)
        {
            throw new InvalidOperationException(
                "NATSX GamepadHost input mapping is outdated or incompatible. " +
                "Run the latest NATSX Receiver Setup to update both Receiver and GamepadHost.");
        }
    }

    public static void EncodeHeader(
        Span<byte> destination,
        GamepadHostMessageType messageType,
        ushort payloadLength)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException(
                "Destination is too small for a gamepad-host frame header.",
                nameof(destination));
        }

        if (payloadLength > MaximumPayloadSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payloadLength));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            destination,
            Magic);
        destination[4] =
            Version;
        destination[5] =
            (byte)messageType;
        BinaryPrimitives.WriteUInt16LittleEndian(
            destination[6..],
            payloadLength);
    }

    public static (
        GamepadHostMessageType MessageType,
        ushort PayloadLength)
        DecodeHeader(
            ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            throw new FormatException(
                "Gamepad-host frame header is truncated.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(
                source) != Magic)
        {
            throw new FormatException(
                "Gamepad-host frame magic is invalid.");
        }

        if (source[4] != Version)
        {
            throw new FormatException(
                $"Unsupported gamepad-host protocol version {source[4]}.");
        }

        GamepadHostMessageType messageType =
            (GamepadHostMessageType)source[5];

        if (!Enum.IsDefined(
                messageType))
        {
            throw new FormatException(
                $"Unknown gamepad-host message type {source[5]}.");
        }

        ushort payloadLength =
            BinaryPrimitives.ReadUInt16LittleEndian(
                source[6..]);

        if (payloadLength >
            MaximumPayloadSize)
        {
            throw new FormatException(
                "Gamepad-host payload exceeds the protocol limit.");
        }

        return (
            messageType,
            payloadLength);
    }

    public static void EncodeGamepadState(
        GamepadState state,
        Span<byte> destination)
    {
        if (destination.Length <
            GamepadStatePayloadSize)
        {
            throw new ArgumentException(
                "Destination is too small for a gamepad state.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            destination,
            (ushort)state.Buttons);
        destination[2] =
            (byte)state.Dpad;
        BinaryPrimitives.WriteInt16LittleEndian(
            destination[3..],
            state.LeftX);
        BinaryPrimitives.WriteInt16LittleEndian(
            destination[5..],
            state.LeftY);
        BinaryPrimitives.WriteInt16LittleEndian(
            destination[7..],
            state.RightX);
        BinaryPrimitives.WriteInt16LittleEndian(
            destination[9..],
            state.RightY);
        destination[11] =
            state.LeftTrigger;
        destination[12] =
            state.RightTrigger;
    }

    public static GamepadState DecodeGamepadState(
        ReadOnlySpan<byte> source)
    {
        if (source.Length !=
            GamepadStatePayloadSize)
        {
            throw new FormatException(
                $"Gamepad-state payload must be exactly {GamepadStatePayloadSize} bytes.");
        }

        GamepadButtons buttons =
            (GamepadButtons)
            BinaryPrimitives.ReadUInt16LittleEndian(
                source);

        const GamepadButtons validButtons =
            GamepadButtons.A |
            GamepadButtons.B |
            GamepadButtons.X |
            GamepadButtons.Y |
            GamepadButtons.LeftShoulder |
            GamepadButtons.RightShoulder |
            GamepadButtons.LeftStick |
            GamepadButtons.RightStick |
            GamepadButtons.Back |
            GamepadButtons.Start |
            GamepadButtons.Guide;

        if ((buttons & ~validButtons) !=
            GamepadButtons.None)
        {
            throw new FormatException(
                "Gamepad-state payload contains unknown button bits.");
        }

        DpadState dpad =
            (DpadState)source[2];

        const DpadState validDpad =
            DpadState.Up |
            DpadState.Down |
            DpadState.Left |
            DpadState.Right;

        if ((dpad & ~validDpad) !=
            DpadState.Neutral)
        {
            throw new FormatException(
                "Gamepad-state payload contains unknown D-pad bits.");
        }

        if ((dpad &
                (DpadState.Up |
                 DpadState.Down)) ==
            (DpadState.Up |
             DpadState.Down))
        {
            throw new FormatException(
                "Gamepad-state payload presses D-pad Up and Down simultaneously.");
        }

        if ((dpad &
                (DpadState.Left |
                 DpadState.Right)) ==
            (DpadState.Left |
             DpadState.Right))
        {
            throw new FormatException(
                "Gamepad-state payload presses D-pad Left and Right simultaneously.");
        }

        return new GamepadState(
            buttons,
            dpad,
            BinaryPrimitives.ReadInt16LittleEndian(
                source[3..]),
            BinaryPrimitives.ReadInt16LittleEndian(
                source[5..]),
            BinaryPrimitives.ReadInt16LittleEndian(
                source[7..]),
            BinaryPrimitives.ReadInt16LittleEndian(
                source[9..]),
            source[11],
            source[12]);
    }

    public static void EncodeRumble(
        RumbleState rumble,
        Span<byte> destination)
    {
        if (destination.Length <
            RumblePayloadSize)
        {
            throw new ArgumentException(
                "Destination is too small for rumble state.",
                nameof(destination));
        }

        destination[0] =
            rumble.LowFrequencyMotor;
        destination[1] =
            rumble.HighFrequencyMotor;
    }

    public static RumbleState DecodeRumble(
        ReadOnlySpan<byte> source)
    {
        if (source.Length !=
            RumblePayloadSize)
        {
            throw new FormatException(
                $"Rumble payload must be exactly {RumblePayloadSize} bytes.");
        }

        return new RumbleState(
            source[0],
            source[1]);
    }
}
