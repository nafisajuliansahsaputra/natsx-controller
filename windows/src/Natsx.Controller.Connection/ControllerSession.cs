using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class ControllerSession
{
    private bool _hasAcceptedSequence;
    private uint _lastAcceptedSequence;

    private TransportKind? _pendingTransport;
    private uint _pendingSequence;
    private GamepadState _pendingState = GamepadState.Neutral;
    private bool _hasPendingState;

    public TransportKind? AuthoritativeTransport { get; private set; }

    public TransportKind? PendingHandoverTransport =>
        _hasPendingState ? _pendingTransport : null;

    public GamepadState CurrentState { get; private set; } =
        GamepadState.Neutral;

    public uint? LastAcceptedSequence =>
        _hasAcceptedSequence ? _lastAcceptedSequence : null;

    public void BeginNewSession(TransportKind initialTransport)
    {
        AuthoritativeTransport = initialTransport;
        _hasAcceptedSequence = false;
        _lastAcceptedSequence = 0;
        CurrentState = GamepadState.Neutral;
        ClearPendingHandover();
    }

    public void SetAuthoritativeTransport(TransportKind transport)
    {
        AuthoritativeTransport = transport;
        ClearPendingHandover();
    }

    public void ClearAuthority()
    {
        AuthoritativeTransport = null;
        ClearPendingHandover();
    }

    public bool TryAccept(
        TransportKind transport,
        uint sequence,
        GamepadState state)
    {
        if (AuthoritativeTransport != transport)
            return false;

        if (_hasAcceptedSequence &&
            !SequenceNumber.IsNewer(
                sequence,
                _lastAcceptedSequence))
        {
            return false;
        }

        _lastAcceptedSequence = sequence;
        _hasAcceptedSequence = true;
        CurrentState = state;
        return true;
    }

    public bool TryStageHandoverState(
        TransportKind candidateTransport,
        uint sequence,
        GamepadState state)
    {
        if (AuthoritativeTransport is null ||
            candidateTransport == AuthoritativeTransport)
        {
            return false;
        }

        if (_hasAcceptedSequence &&
            !SequenceNumber.IsNewer(
                sequence,
                _lastAcceptedSequence))
        {
            return false;
        }

        if (_hasPendingState &&
            _pendingTransport == candidateTransport &&
            !SequenceNumber.IsNewer(
                sequence,
                _pendingSequence))
        {
            return false;
        }

        _pendingTransport = candidateTransport;
        _pendingSequence = sequence;
        _pendingState = state;
        _hasPendingState = true;
        return true;
    }

    public bool TryCommitHandover(
        TransportKind candidateTransport,
        out GamepadState committedState)
    {
        committedState = CurrentState;

        if (!_hasPendingState ||
            _pendingTransport != candidateTransport)
        {
            return false;
        }

        if (_hasAcceptedSequence &&
            !SequenceNumber.IsNewer(
                _pendingSequence,
                _lastAcceptedSequence))
        {
            ClearPendingHandover();
            return false;
        }

        AuthoritativeTransport = candidateTransport;
        _lastAcceptedSequence = _pendingSequence;
        _hasAcceptedSequence = true;
        CurrentState = _pendingState;
        committedState = _pendingState;
        ClearPendingHandover();
        return true;
    }

    public void AbortHandover(
        TransportKind candidateTransport)
    {
        if (_hasPendingState &&
            _pendingTransport == candidateTransport)
        {
            ClearPendingHandover();
        }
    }

    public void Neutralize()
    {
        CurrentState = GamepadState.Neutral;
    }

    private void ClearPendingHandover()
    {
        _pendingTransport = null;
        _pendingSequence = 0;
        _pendingState = GamepadState.Neutral;
        _hasPendingState = false;
    }
}
