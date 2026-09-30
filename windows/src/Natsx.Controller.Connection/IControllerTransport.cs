using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public interface IControllerTransport : IAsyncDisposable
{
    TransportKind Kind { get; }

    TransportRuntimeState State { get; }

    ValueTask ConnectAsync(CancellationToken cancellationToken);

    ValueTask DisconnectAsync(CancellationToken cancellationToken);

    TransportHealthSnapshot GetHealthSnapshot();
}
