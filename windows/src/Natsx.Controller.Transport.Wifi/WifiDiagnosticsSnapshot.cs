using System.Net;
using Natsx.Controller.Connection;

namespace Natsx.Controller.Transport.Wifi;

public readonly record struct WifiDiagnosticsSnapshot(
    TransportRuntimeState TransportState,
    bool ReceiverRunning,
    IPEndPoint? LocalEndPoint,
    IPEndPoint? AuthenticatedRemoteEndPoint,
    long AcceptedStateDatagrams,
    long AcceptedControlDatagrams,
    long RejectedDatagrams,
    long HeartbeatsSent,
    TimeSpan Silence,
    TimeSpan? RoundTripTime,
    TimeSpan Jitter,
    double PacketLossPercent);
