using System.Security.Cryptography;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public static class WifiRealtimeDatagramCodec
{
    public static WifiGamepadDatagram DecodeGamepadState(
        ReadOnlySpan<byte> datagram,
        WifiTrustedSession trustedSession)
    {
        ArgumentNullException.ThrowIfNull(trustedSession);

        ProtocolFrame frame = ProtocolFrameCodec.Decode(
            datagram,
            trustedSession.SessionKey);

        if (frame.MessageType != MessageType.GamepadState)
        {
            throw new FormatException(
                $"Expected {MessageType.GamepadState}, received {frame.MessageType}.");
        }

        if (!frame.Flags.HasFlag(FrameFlags.Authenticated))
        {
            throw new CryptographicException(
                "Wi-Fi realtime controller frames must be authenticated.");
        }

        if (frame.SessionId != trustedSession.SessionId)
        {
            throw new CryptographicException(
                "Wi-Fi realtime frame belongs to a different controller session.");
        }

        GamepadState state = GamepadStateCodec.Decode(frame.Payload);

        return new WifiGamepadDatagram(
            frame.Sequence,
            frame.MonotonicTimestampMicros,
            state);
    }
}
