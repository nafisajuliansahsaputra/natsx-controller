using System.Threading.Channels;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

/// <summary>
/// Bridges authenticated Wi-Fi datagrams into the shared ControllerSession.
/// The ControllerSession remains the source of truth for authority and global
/// sequence freshness, so Wi-Fi cannot bypass stale/duplicate rejection.
/// </summary>
public sealed class WifiRealtimeIngress
{
    private readonly ControllerSession _controllerSession;

    public WifiRealtimeIngress(ControllerSession controllerSession)
    {
        _controllerSession = controllerSession
            ?? throw new ArgumentNullException(nameof(controllerSession));
    }

    public long AcceptedStates { get; private set; }

    public long RejectedStates { get; private set; }

    public event Action<GamepadState>? StateAccepted;

    public bool TryAccept(WifiGamepadDatagram datagram)
    {
        bool accepted = _controllerSession.TryAccept(
            TransportKind.Wifi,
            datagram.Sequence,
            datagram.State);

        if (!accepted)
        {
            RejectedStates++;
            return false;
        }

        AcceptedStates++;
        StateAccepted?.Invoke(datagram.State);
        return true;
    }

    public async Task RunAsync(
        ChannelReader<WifiGamepadDatagram> reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        await foreach (WifiGamepadDatagram datagram in
            reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            TryAccept(datagram);
        }
    }
}
