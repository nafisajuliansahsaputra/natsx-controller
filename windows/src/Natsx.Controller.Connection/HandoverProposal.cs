using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public enum HandoverReason
{
    InitialSelection,
    PreferredUsbReady,
    PreferredWifiRecovered,
    ActiveDegraded,
    ActiveCritical,
    ActiveLost,
    BetterCandidate,
}

public readonly record struct HandoverProposal(
    TransportKind? From,
    TransportKind To,
    HandoverReason Reason,
    bool FailureInduced);
