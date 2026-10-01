using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class InputSafetyEngine
{
    private readonly ControllerSession _session;
    private readonly IVirtualGamepadBackend _backend;
    private readonly ConnectionPolicy _policy;
    private readonly TimeProvider _timeProvider;

    private long _lastFreshTimestamp;
    private bool _hasFreshState;
    private bool _isNeutralized;

    public InputSafetyEngine(
        ControllerSession session,
        IVirtualGamepadBackend backend,
        ConnectionPolicy policy,
        TimeProvider? timeProvider = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsNeutralized => _isNeutralized;

    public bool TryAccept(
        TransportKind transport,
        uint sequence,
        GamepadState state)
    {
        if (!_session.TryAccept(transport, sequence, state))
        {
            return false;
        }

        _lastFreshTimestamp = _timeProvider.GetTimestamp();
        _hasFreshState = true;
        _isNeutralized = false;
        _backend.Submit(state);
        return true;
    }

    public bool TryAdoptEquivalentState(
        TransportKind transport,
        uint sequence,
        GamepadState state)
    {
        if (!_session.TryAdoptEquivalentState(
                transport,
                sequence,
                state))
        {
            return false;
        }

        _lastFreshTimestamp = _timeProvider.GetTimestamp();
        _hasFreshState = true;
        _isNeutralized = false;
        return true;
    }

    public bool Evaluate()
    {
        if (!_hasFreshState || _isNeutralized)
        {
            return false;
        }

        TimeSpan silence = _timeProvider.GetElapsedTime(
            _lastFreshTimestamp,
            _timeProvider.GetTimestamp());

        if (silence < _policy.NeutralizeSilence)
        {
            return false;
        }

        ForceNeutral();
        return true;
    }

    public void ForceNeutral()
    {
        _session.Neutralize();
        _backend.Submit(GamepadState.Neutral);
        _isNeutralized = true;
    }
}
