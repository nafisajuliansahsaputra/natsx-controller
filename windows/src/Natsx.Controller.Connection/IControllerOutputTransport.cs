using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

/// <summary>
/// Optional reverse/output capability for a controller transport.
///
/// Output traffic is deliberately separated from IControllerTransport so
/// haptics can fail or be unavailable without affecting realtime input
/// authority, health evaluation, or handover.
/// </summary>
public interface IControllerOutputTransport
{
    TransportKind Kind { get; }

    ValueTask<bool> TrySendRumbleAsync(
        RumbleState rumble,
        CancellationToken cancellationToken = default);
}
