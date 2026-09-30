using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

public readonly record struct WifiGamepadDatagram(
    uint Sequence,
    ulong RemoteMonotonicTimestampMicros,
    GamepadState State);
