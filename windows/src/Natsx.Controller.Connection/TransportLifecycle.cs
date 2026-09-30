namespace Natsx.Controller.Connection;

/// <summary>
/// Small shared state holder for transport connection lifecycle.
///
/// The same instance may be shared between control/authentication and realtime
/// transport components so Smart Connection observes one coherent lifecycle.
/// </summary>
public sealed class TransportLifecycle
{
    private int _state;

    public TransportLifecycle(
        TransportRuntimeState initialState = TransportRuntimeState.Unavailable)
    {
        _state = (int)initialState;
    }

    public event Action<TransportRuntimeState>? StateChanged;

    public TransportRuntimeState State =>
        (TransportRuntimeState)Volatile.Read(ref _state);

    public void SetState(TransportRuntimeState state)
    {
        TransportRuntimeState previous =
            (TransportRuntimeState)Interlocked.Exchange(
                ref _state,
                (int)state);

        if (previous != state)
        {
            StateChanged?.Invoke(state);
        }
    }

    public bool TryTransition(
        TransportRuntimeState expected,
        TransportRuntimeState next)
    {
        bool changed =
            Interlocked.CompareExchange(
                ref _state,
                (int)next,
                (int)expected) ==
            (int)expected;

        if (changed && expected != next)
        {
            StateChanged?.Invoke(next);
        }

        return changed;
    }
}
