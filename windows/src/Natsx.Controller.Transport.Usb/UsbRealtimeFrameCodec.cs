using System.Security.Cryptography;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

public static class UsbRealtimeFrameCodec
{
    public static UsbGamepadFrame DecodeGamepadState(
        ReadOnlySpan<byte> frameBytes,
        UsbTrustedSession trustedSession)
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
                "Usb realtime controller frames must be authenticated.");
        }

        if (frame.SessionId != trustedSession.SessionId)
        {
            throw new CryptographicException(
                "Usb realtime frame belongs to a different controller session.");
        }

        GamepadState state =
            GamepadStateCodec.Decode(frame.Payload);

        return new UsbGamepadFrame(
            frame.Sequence,
            frame.MonotonicTimestampMicros,
            state);
    }
}
