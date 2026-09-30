namespace Natsx.Controller.Connection;

public enum ConnectionManagerState
{
    Disconnected,
    Discovering,
    Connecting,
    Authenticating,
    Stabilizing,
    Ready,
    Active,
    Suspect,
    Degraded,
    Handover,
    Recovering,
    Cooldown,
}

public enum TransportRuntimeState
{
    Unavailable,
    Available,
    Connecting,
    Authenticating,
    Stabilizing,
    Ready,
    Active,
    Suspect,
    Degraded,
    Failed,
    Cooldown,
}
