using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Bluetooth;

public readonly record struct BluetoothGamepadFrame(
    uint Sequence,
    ulong RemoteMonotonicTimestampMicros,
    GamepadState State);
