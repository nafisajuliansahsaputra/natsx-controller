using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public readonly record struct LatestTransportState(
    TransportKind Transport,
    uint Sequence,
    GamepadState State,
    long ReceivedTimestamp);
