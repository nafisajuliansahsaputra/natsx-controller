using Natsx.Controller.Core;

namespace Natsx.Controller.Connection.Tests;

internal sealed class ScriptedControllerTransport : IControllerTransport
{
    private readonly TimeProvider _clock;

    public ScriptedControllerTransport(
        TransportKind kind,
        TimeProvider clock,
        TransportHealthSnapshot initialSnapshot)
    {
        Kind = kind;
        _clock = clock;
        Snapshot = initialSnapshot;
    }

    public event EventHandler<TransportGamepadStateEventArgs>? GamepadStateReceived;

    public event EventHandler<TransportRuntimeStateChangedEventArgs>? StateChanged;

    public TransportKind Kind { get; }

    public TransportRuntimeState State => Snapshot.State;

    public TransportHealthSnapshot Snapshot { get; private set; }

    public bool IsAuthoritative { get; private set; }

    public ValueTask ConnectAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask DisconnectAsync(CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void SetAuthoritative(bool authoritative)
    {
        IsAuthoritative = authoritative;

        if (Snapshot.State is
            TransportRuntimeState.Failed or
            TransportRuntimeState.Unavailable)
        {
            return;
        }

        Snapshot = Snapshot with
        {
            State = authoritative
                ? TransportRuntimeState.Active
                : TransportRuntimeState.Ready,
        };
    }

    public TransportHealthSnapshot GetHealthSnapshot() => Snapshot;

    public void SetHealth(TransportHealthSnapshot snapshot)
    {
        if (snapshot.Transport != Kind)
        {
            throw new ArgumentException(
                "Scripted health snapshot belongs to a different transport.",
                nameof(snapshot));
        }

        Snapshot = snapshot;

        StateChanged?.Invoke(
            this,
            new TransportRuntimeStateChangedEventArgs(
                Kind,
                snapshot.State));
    }

    public void Publish(uint sequence, GamepadState state)
    {
        GamepadStateReceived?.Invoke(
            this,
            new TransportGamepadStateEventArgs(
                Kind,
                sequence,
                state,
                _clock.GetTimestamp()));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
