using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class TransportGamepadStateEventArgs : EventArgs
{
    public TransportGamepadStateEventArgs(
        TransportKind transport,
        uint sequence,
        GamepadState state,
        long receivedTimestamp)
    {
        Transport = transport;
        Sequence = sequence;
        State = state;
        ReceivedTimestamp = receivedTimestamp;
    }

    public TransportKind Transport { get; }

    public uint Sequence { get; }

    public GamepadState State { get; }

    public long ReceivedTimestamp { get; }
}
