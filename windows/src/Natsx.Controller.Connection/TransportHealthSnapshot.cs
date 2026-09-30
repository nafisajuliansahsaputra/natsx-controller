using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public enum TransportHealthGrade
{
    Excellent,
    Good,
    Warning,
    Degraded,
    Critical,
    Lost,
}

public readonly record struct TransportHealthSnapshot(
    TransportKind Transport,
    TransportRuntimeState State,
    TimeSpan RoundTripTime,
    TimeSpan Jitter,
    double PacketLossPercent,
    TimeSpan Silence,
    int Score,
    TransportHealthGrade Grade);
