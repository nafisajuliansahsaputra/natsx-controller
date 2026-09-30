using System.Buffers.Binary;
using Natsx.Controller.Core;

namespace Natsx.Controller.Protocol;

public static class GamepadStateCodec
{
    private const ushort AllowedButtonMask = 0x07FF;
    private const byte AllowedDpadMask = 0x0F;

    public static byte[] Encode(GamepadState state)
    {
        var payload = new byte[ProtocolConstants.GamepadStatePayloadSize];
        Write(state, payload);
        return payload;
    }

    public static void Write(GamepadState state, Span<byte> destination)
    {
        if (destination.Length < ProtocolConstants.GamepadStatePayloadSize)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        Validate(state);

        destination[..ProtocolConstants.GamepadStatePayloadSize].Clear();

        BinaryPrimitives.WriteUInt16LittleEndian(destination[0..2], (ushort)state.Buttons);
        destination[2] = (byte)state.Dpad;

        BinaryPrimitives.WriteInt16LittleEndian(destination[4..6], state.LeftX);
        BinaryPrimitives.WriteInt16LittleEndian(destination[6..8], state.LeftY);
        BinaryPrimitives.WriteInt16LittleEndian(destination[8..10], state.RightX);
        BinaryPrimitives.WriteInt16LittleEndian(destination[10..12], state.RightY);

        destination[12] = state.LeftTrigger;
        destination[13] = state.RightTrigger;
    }

    public static GamepadState Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != ProtocolConstants.GamepadStatePayloadSize)
        {
            throw new FormatException("GAMEPAD_STATE payload must be exactly 16 bytes.");
        }

        if (payload[3] != 0 || payload[14] != 0 || payload[15] != 0)
        {
            throw new FormatException("Reserved GAMEPAD_STATE bytes must be zero.");
        }

        var buttonsRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2]);
        var dpadRaw = payload[2];

        if ((buttonsRaw & ~AllowedButtonMask) != 0)
        {
            throw new FormatException("Reserved button bits must be zero.");
        }

        if ((dpadRaw & ~AllowedDpadMask) != 0)
        {
            throw new FormatException("Reserved D-pad bits must be zero.");
        }

        var dpad = (DpadState)dpadRaw;
        ValidateDpad(dpad);

        return new GamepadState(
            (GamepadButtons)buttonsRaw,
            dpad,
            BinaryPrimitives.ReadInt16LittleEndian(payload[4..6]),
            BinaryPrimitives.ReadInt16LittleEndian(payload[6..8]),
            BinaryPrimitives.ReadInt16LittleEndian(payload[8..10]),
            BinaryPrimitives.ReadInt16LittleEndian(payload[10..12]),
            payload[12],
            payload[13]);
    }

    private static void Validate(GamepadState state)
    {
        if (((ushort)state.Buttons & ~AllowedButtonMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Gamepad state contains reserved button bits.");
        }

        if (((byte)state.Dpad & ~AllowedDpadMask) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Gamepad state contains reserved D-pad bits.");
        }

        ValidateDpad(state.Dpad);
    }

    private static void ValidateDpad(DpadState dpad)
    {
        bool verticalConflict =
            dpad.HasFlag(DpadState.Up) &&
            dpad.HasFlag(DpadState.Down);

        bool horizontalConflict =
            dpad.HasFlag(DpadState.Left) &&
            dpad.HasFlag(DpadState.Right);

        if (verticalConflict || horizontalConflict)
        {
            throw new FormatException("Opposite D-pad directions are invalid.");
        }
    }
}
