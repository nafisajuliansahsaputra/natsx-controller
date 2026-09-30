using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public interface IControllerTransport : IAsyncDisposable
{
    event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

    TransportKind Kind { get; }

    TransportRuntimeState State { get; }

    ValueTask ConnectAsync(CancellationToken cancellationToken);

    ValueTask DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Updates only the transport's local authority state. Implementations must
    /// keep this call non-blocking and must not perform transport I/O here.
    /// Receiving packets alone must not make a standby transport authoritative.
    /// </summary>
    void SetAuthoritative(bool authoritative);

    TransportHealthSnapshot GetHealthSnapshot();
}
