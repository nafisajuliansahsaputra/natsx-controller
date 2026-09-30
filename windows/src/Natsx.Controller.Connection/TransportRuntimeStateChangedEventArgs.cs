using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class TransportRuntimeStateChangedEventArgs : EventArgs
{
    public TransportRuntimeStateChangedEventArgs(
        TransportKind transport,
        TransportRuntimeState state)
    {
        Transport = transport;
        State = state;
    }

    public TransportKind Transport { get; }

    public TransportRuntimeState State { get; }
}
