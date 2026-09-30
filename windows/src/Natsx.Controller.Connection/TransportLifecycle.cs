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

    public TransportRuntimeState State =>
        (TransportRuntimeState)Volatile.Read(ref _state);

    public void SetState(TransportRuntimeState state)
    {
        Volatile.Write(ref _state, (int)state);
    }

    public bool TryTransition(
        TransportRuntimeState expected,
        TransportRuntimeState next)
    {
        return Interlocked.CompareExchange(
                   ref _state,
                   (int)next,
                   (int)expected) ==
               (int)expected;
    }
}
