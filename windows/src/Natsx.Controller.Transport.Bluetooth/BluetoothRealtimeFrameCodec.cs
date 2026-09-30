using System.Security.Cryptography;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Bluetooth;

public static class BluetoothRealtimeFrameCodec
{
    public static BluetoothGamepadFrame DecodeGamepadState(
        ReadOnlySpan<byte> frameBytes,
        BluetoothTrustedSession trustedSession)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        ProtocolFrame frame =
            ProtocolFrameCodec.Decode(
                frameBytes,
                trustedSession.SessionKey);

        if (frame.MessageType != MessageType.GamepadState)
        {
            throw new FormatException(
                $"Expected {MessageType.GamepadState}, received {frame.MessageType}.");
        }

        if (!frame.Flags.HasFlag(FrameFlags.Authenticated))
        {
            throw new CryptographicException(
                "Bluetooth realtime controller frames must be authenticated.");
        }

        if (frame.SessionId != trustedSession.SessionId)
        {
            throw new CryptographicException(
                "Bluetooth realtime frame belongs to a different controller session.");
        }

        GamepadState state =
            GamepadStateCodec.Decode(frame.Payload);

        return new BluetoothGamepadFrame(
            frame.Sequence,
            frame.MonotonicTimestampMicros,
            state);
    }
}
