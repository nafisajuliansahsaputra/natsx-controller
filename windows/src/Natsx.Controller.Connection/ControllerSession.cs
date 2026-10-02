using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class ControllerSession
{
    private bool _hasAcceptedSequence;
    private uint _lastAcceptedSequence;

    public TransportKind? AuthoritativeTransport { get; private set; }

    public GamepadState CurrentState { get; private set; } = GamepadState.Neutral;

    public uint? LastAcceptedSequence => _hasAcceptedSequence ? _lastAcceptedSequence : null;

    public void SetAuthoritativeTransport(TransportKind transport)
    {
        AuthoritativeTransport = transport;
    }

    public void ClearAuthority()
    {
        AuthoritativeTransport = null;
    }

    public bool TryAccept(
        TransportKind transport,
        uint sequence,
        GamepadState state)
    {
        if (AuthoritativeTransport != transport)
        {
            return false;
        }

        if (_hasAcceptedSequence && !SequenceNumber.IsNewer(sequence, _lastAcceptedSequence))
        {
            return false;
        }

        _lastAcceptedSequence = sequence;
        _hasAcceptedSequence = true;
        CurrentState = state;
        return true;
    }

    public bool TryAdoptEquivalentState(
        TransportKind transport,
        uint sequence,
        GamepadState state)
    {
        if (!_hasAcceptedSequence ||
            sequence != _lastAcceptedSequence ||
            state != CurrentState)
        {
            return false;
        }

        AuthoritativeTransport = transport;
        return true;
    }

    public void Neutralize()
    {
        CurrentState = GamepadState.Neutral;
    }
}
