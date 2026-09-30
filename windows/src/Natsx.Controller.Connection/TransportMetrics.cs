using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public readonly record struct TransportMetrics(
    TransportKind Transport,
    TransportRuntimeState State,
    TimeSpan RoundTripTime,
    TimeSpan Jitter,
    double PacketLossPercent,
    TimeSpan Silence);
