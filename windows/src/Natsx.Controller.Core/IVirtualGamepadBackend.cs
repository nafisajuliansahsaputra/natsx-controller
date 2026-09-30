namespace Natsx.Controller.Core;

public interface IVirtualGamepadBackend : IAsyncDisposable
{
    bool IsStarted { get; }

    event Action<RumbleState>? RumbleReceived;

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    void Submit(GamepadState state);
}
