namespace Natsx.Controller.Receiver;

public sealed record ReceiverDiagnosticsSnapshot(
    string SmartAutoState,
    string ActiveTransport,
    string BackupTransports,
    string VirtualControllerStatus,
    int TrustedControllerCount,
    TimeSpan? RoundTripTime,
    TimeSpan? Jitter,
    double? PacketLossPercent,
    double InputRateHz,
    int ReconnectCount,
    string RecentHandoverReason);
