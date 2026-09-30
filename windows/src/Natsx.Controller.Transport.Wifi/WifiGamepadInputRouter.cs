using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiGamepadInputRouter
{
    private readonly InputSafetyEngine _safetyEngine;

    public WifiGamepadInputRouter(InputSafetyEngine safetyEngine)
    {
        _safetyEngine = safetyEngine ?? throw new ArgumentNullException(nameof(safetyEngine));
    }

    public bool TryHandle(ProtocolFrame frame)
    {
        if (frame.MessageType != MessageType.GamepadState)
            return false;

        if (!frame.Flags.HasFlag(FrameFlags.Authenticated))
            return false;

        GamepadState state;

        try
        {
            state = GamepadStateCodec.Decode(frame.Payload);
        }
        catch (FormatException)
        {
            return false;
        }

        return _safetyEngine.TryAccept(
            TransportKind.Wifi,
            frame.Sequence,
            state);
    }
}
