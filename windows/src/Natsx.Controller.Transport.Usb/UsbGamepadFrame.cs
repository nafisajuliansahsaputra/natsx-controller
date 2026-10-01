using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Usb;

public readonly record struct UsbGamepadFrame(
    uint Sequence,
    ulong RemoteMonotonicTimestampMicros,
    GamepadState State);
