using System.Threading.Channels;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

/// <summary>
/// Bridges authenticated Wi-Fi datagrams into the shared input safety path.
/// Authority, global sequence freshness, backend submission and stuck-input
/// protection remain centralized in InputSafetyEngine/ControllerSession.
/// </summary>
public sealed class WifiRealtimeIngress
{
    private readonly InputSafetyEngine _inputSafety;

    public WifiRealtimeIngress(InputSafetyEngine inputSafety)
    {
        _inputSafety = inputSafety
            ?? throw new ArgumentNullException(nameof(inputSafety));
    }

    public long AcceptedStates { get; private set; }

    public long RejectedStates { get; private set; }

    public event Action<GamepadState>? StateAccepted;

    public bool TryAccept(WifiGamepadDatagram datagram)
    {
        bool accepted = _inputSafety.TryAccept(
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
