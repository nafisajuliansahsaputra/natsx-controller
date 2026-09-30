using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class ControllerSession
{
    private readonly object _gate = new();

    private bool _hasAcceptedSequence;
    private uint _lastAcceptedSequence;

    private TransportKind? _authoritativeTransport;
    private GamepadState _currentState =
        GamepadState.Neutral;

    private TransportKind? _pendingTransport;
    private uint _pendingSequence;
    private GamepadState _pendingState =
        GamepadState.Neutral;
    private bool _hasPendingState;

    public TransportKind? AuthoritativeTransport
    {
        get
        {
            lock (_gate)
                return _authoritativeTransport;
        }
    }

    public TransportKind? PendingHandoverTransport
    {
        get
        {
            lock (_gate)
            {
                return _hasPendingState
                    ? _pendingTransport
                    : null;
            }
        }
    }

    public GamepadState CurrentState
    {
        get
        {
            lock (_gate)
                return _currentState;
        }
    }

    public uint? LastAcceptedSequence
    {
        get
        {
            lock (_gate)
            {
                return _hasAcceptedSequence
                    ? _lastAcceptedSequence
                    : null;
            }
        }
    }

    public void BeginNewSession(
        TransportKind initialTransport)
    {
        lock (_gate)
        {
            _authoritativeTransport =
                initialTransport;
            _hasAcceptedSequence = false;
            _lastAcceptedSequence = 0;
            _currentState =
                GamepadState.Neutral;
            ClearPendingHandoverLocked();
        }
    }

    public void SetAuthoritativeTransport(
        TransportKind transport)
    {
        lock (_gate)
        {
            _authoritativeTransport = transport;
            ClearPendingHandoverLocked();
        }
    }

    public void ClearAuthority()
    {
        lock (_gate)
        {
            _authoritativeTransport = null;
            ClearPendingHandoverLocked();
        }
    }

    public bool TryAccept(
        TransportKind transport,
        uint sequence,
        GamepadState state)
    {
        lock (_gate)
        {
            if (_authoritativeTransport !=
                transport)
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

            _lastAcceptedSequence = sequence;
            _hasAcceptedSequence = true;
            _currentState = state;
            return true;
        }
    }

    public bool TryStageHandoverState(
        TransportKind candidateTransport,
        uint sequence,
        GamepadState state)
    {
        lock (_gate)
        {
            if (_authoritativeTransport is null ||
                candidateTransport ==
                    _authoritativeTransport)
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
                _pendingTransport ==
                    candidateTransport &&
                !SequenceNumber.IsNewer(
                    sequence,
                    _pendingSequence))
            {
                return false;
            }

            _pendingTransport =
                candidateTransport;
            _pendingSequence = sequence;
            _pendingState = state;
            _hasPendingState = true;
            return true;
        }
    }

    public bool TryCommitHandover(
        TransportKind candidateTransport,
        out GamepadState committedState)
    {
        lock (_gate)
        {
            committedState =
                _currentState;

            if (!_hasPendingState ||
                _pendingTransport !=
                    candidateTransport)
            {
                return false;
            }

            if (_hasAcceptedSequence &&
                !SequenceNumber.IsNewer(
                    _pendingSequence,
                    _lastAcceptedSequence))
            {
                ClearPendingHandoverLocked();
                return false;
            }

            _authoritativeTransport =
                candidateTransport;
            _lastAcceptedSequence =
                _pendingSequence;
            _hasAcceptedSequence = true;
            _currentState =
                _pendingState;
            committedState =
                _pendingState;

            ClearPendingHandoverLocked();
            return true;
        }
    }

    public void AbortHandover(
        TransportKind candidateTransport)
    {
        lock (_gate)
        {
            if (_hasPendingState &&
                _pendingTransport ==
                    candidateTransport)
            {
                ClearPendingHandoverLocked();
            }
        }
    }

    public void Neutralize()
    {
        lock (_gate)
        {
            _currentState =
                GamepadState.Neutral;
        }
    }

    private void ClearPendingHandoverLocked()
    {
        _pendingTransport = null;
        _pendingSequence = 0;
        _pendingState =
            GamepadState.Neutral;
        _hasPendingState = false;
    }
}
