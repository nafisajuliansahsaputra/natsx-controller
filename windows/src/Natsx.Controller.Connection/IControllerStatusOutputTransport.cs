namespace Natsx.Controller.Connection;

/// <summary>
/// Optional reverse control/status capability for a controller transport.
///
/// Smart Auto handover notifications are queued and delivered away from the
/// realtime input path. Failure to deliver status must never change input
/// authority or transport health.
/// </summary>
public interface IControllerStatusOutputTransport
{
    TransportKind Kind { get; }

    ValueTask<bool> TrySendHandoverCommitAsync(
        TransportKind activeTransport,
        uint stateSequence,
        CancellationToken cancellationToken = default);
}
