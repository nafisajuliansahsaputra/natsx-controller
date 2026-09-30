using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class SmartConnectionManager
{
    private readonly ConnectionPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<TransportKind, CandidateState> _candidates = new();

    private long? _cooldownStartedAt;
    private TimeSpan _cooldownDuration;

    public SmartConnectionManager(
        ConnectionPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _policy = policy ?? ConnectionPolicy.Competitive;
        _timeProvider = timeProvider ?? TimeProvider.System;

        foreach (TransportKind kind in Enum.GetValues<TransportKind>())
        {
            _candidates[kind] = new CandidateState();
        }
    }

    public TransportKind? ActiveTransport { get; private set; }

    public ConnectionManagerState State { get; private set; } =
        ConnectionManagerState.Disconnected;

    public void Report(TransportHealthSnapshot snapshot)
    {
        long now = _timeProvider.GetTimestamp();
        CandidateState candidate = _candidates[snapshot.Transport];
        TransportHealthSnapshot? previous = candidate.Snapshot;

        candidate.Snapshot = snapshot;

        bool good =
            snapshot.State is TransportRuntimeState.Ready or TransportRuntimeState.Active &&
            snapshot.Grade is TransportHealthGrade.Excellent or TransportHealthGrade.Good &&
            snapshot.Score >= _policy.GoodCandidateScore;

        if (good)
        {
            candidate.GoodSince ??= now;
        }
        else
        {
            candidate.GoodSince = null;
        }

        if (snapshot.Grade == TransportHealthGrade.Degraded)
        {
            candidate.DegradedSince ??= now;
        }
        else
        {
            candidate.DegradedSince = null;
        }

        bool hardFailureNow =
            snapshot.State is TransportRuntimeState.Failed or TransportRuntimeState.Unavailable ||
            snapshot.Grade == TransportHealthGrade.Lost;

        bool hardFailureBefore =
            previous is not null &&
            (previous.Value.State is TransportRuntimeState.Failed or TransportRuntimeState.Unavailable ||
             previous.Value.Grade == TransportHealthGrade.Lost);

        if (hardFailureNow && !hardFailureBefore)
        {
            RecordHardFailure(snapshot.Transport);
        }

        RefreshManagerState();
    }

    public void RecordHardFailure(TransportKind transport)
    {
        long now = _timeProvider.GetTimestamp();
        CandidateState candidate = _candidates[transport];

        candidate.Failures.Add(now);
        candidate.LastHardFailureAt = now;

        TrimFailures(candidate, _policy.FailurePenaltyWindow, now);

        int breakerFailures = candidate.Failures.Count(
            timestamp => Elapsed(timestamp, now) <= _policy.CircuitBreakerWindow);

        if (breakerFailures >= _policy.CircuitBreakerFailureCount &&
            !IsCircuitOpen(candidate, now))
        {
            candidate.CircuitOpenLevel++;
            candidate.CircuitOpenedAt = now;
            candidate.CircuitOpenDuration = candidate.CircuitOpenLevel switch
            {
                1 => _policy.CircuitBreakerInitialOpen,
                2 => _policy.CircuitBreakerSecondOpen,
                _ => _policy.CircuitBreakerMaximumOpen,
            };
        }
    }

    public HandoverProposal? Evaluate()
    {
        long now = _timeProvider.GetTimestamp();
        RefreshCooldown(now);

        if (ActiveTransport is null)
        {
            TransportKind? initial = SelectBestCandidate(
                now,
                minimumScore: _policy.UsableCandidateScore,
                requireGood: false,
                exclude: null);

            if (initial is null)
            {
                State = ConnectionManagerState.Disconnected;
                return null;
            }

            State = ConnectionManagerState.Ready;
            return new HandoverProposal(
                null,
                initial.Value,
                HandoverReason.InitialSelection,
                FailureInduced: false);
        }

        TransportKind activeKind = ActiveTransport.Value;
        CandidateState activeCandidate = _candidates[activeKind];
        TransportHealthSnapshot? activeSnapshot = activeCandidate.Snapshot;

        if (activeSnapshot is null ||
            activeSnapshot.Value.State is TransportRuntimeState.Failed or TransportRuntimeState.Unavailable ||
            activeSnapshot.Value.Grade == TransportHealthGrade.Lost)
        {
            TransportKind? emergency = SelectBestCandidate(
                now,
                minimumScore: _policy.UsableCandidateScore,
                requireGood: false,
                exclude: activeKind);

            State = ConnectionManagerState.Recovering;

            return emergency is null
                ? null
                : new HandoverProposal(
                    activeKind,
                    emergency.Value,
                    HandoverReason.ActiveLost,
                    FailureInduced: true);
        }

        bool critical = activeSnapshot.Value.Grade == TransportHealthGrade.Critical;

        if (critical)
        {
            TransportKind? emergency = SelectBestCandidate(
                now,
                minimumScore: _policy.UsableCandidateScore,
                requireGood: false,
                exclude: activeKind);

            State = ConnectionManagerState.Degraded;

            if (emergency is not null)
            {
                return new HandoverProposal(
                    activeKind,
                    emergency.Value,
                    HandoverReason.ActiveCritical,
                    FailureInduced: true);
            }
        }

        if (IsCooldownActive(now))
        {
            State = ConnectionManagerState.Cooldown;
            return null;
        }

        HandoverProposal? usbProposal = EvaluateUsbPreference(activeKind, now);
        if (usbProposal is not null)
        {
            return usbProposal;
        }

        HandoverProposal? degradedProposal =
            EvaluateDegradedActive(activeKind, activeSnapshot.Value, now);

        if (degradedProposal is not null)
        {
            return degradedProposal;
        }

        HandoverProposal? wifiRecoveryProposal =
            EvaluateWifiRecovery(activeKind, now);

        if (wifiRecoveryProposal is not null)
        {
            return wifiRecoveryProposal;
        }

        HandoverProposal? betterProposal =
            EvaluateBetterCandidate(activeKind, activeSnapshot.Value, now);

        if (betterProposal is not null)
        {
            return betterProposal;
        }

        State = activeSnapshot.Value.State == TransportRuntimeState.Suspect ||
                activeSnapshot.Value.Grade == TransportHealthGrade.Warning
            ? ConnectionManagerState.Suspect
            : activeSnapshot.Value.Grade is
                TransportHealthGrade.Degraded or
                TransportHealthGrade.Critical
                ? ConnectionManagerState.Degraded
                : ConnectionManagerState.Active;

        return null;
    }

    public void Commit(HandoverProposal proposal)
    {
        long now = _timeProvider.GetTimestamp();

        if (proposal.From != ActiveTransport)
        {
            throw new InvalidOperationException("Handover source no longer matches active transport.");
        }

        ActiveTransport = proposal.To;

        if (proposal.Reason == HandoverReason.InitialSelection)
        {
            _cooldownStartedAt = null;
            _cooldownDuration = TimeSpan.Zero;
            State = ConnectionManagerState.Active;
            return;
        }

        _cooldownStartedAt = now;

        int recentSourceFailures =
            proposal.From is null
                ? 0
                : RecentFailureCount(_candidates[proposal.From.Value], now);

        _cooldownDuration =
            proposal.FailureInduced && recentSourceFailures >= 2
                ? _policy.RepeatedFailureSwitchCooldown
                : proposal.FailureInduced
                    ? _policy.FailureSwitchCooldown
                    : _policy.NormalSwitchCooldown;

        State = ConnectionManagerState.Cooldown;
    }

    public void ClearActiveTransport()
    {
        ActiveTransport = null;
        _cooldownStartedAt = null;
        _cooldownDuration = TimeSpan.Zero;
        State = ConnectionManagerState.Recovering;
    }

    public bool IsCircuitOpen(TransportKind transport)
    {
        return IsCircuitOpen(_candidates[transport], _timeProvider.GetTimestamp());
    }

    public int GetEffectiveScore(TransportKind transport)
    {
        long now = _timeProvider.GetTimestamp();
        CandidateState candidate = _candidates[transport];

        return candidate.Snapshot is null
            ? 0
            : EffectiveScore(transport, candidate, candidate.Snapshot.Value, now);
    }

    private HandoverProposal? EvaluateUsbPreference(
        TransportKind activeKind,
        long now)
    {
        if (activeKind == TransportKind.Usb)
        {
            return null;
        }

        CandidateState usb = _candidates[TransportKind.Usb];
        TransportHealthSnapshot? snapshot = usb.Snapshot;

        if (snapshot is null ||
            !IsEligibleState(snapshot.Value.State) ||
            snapshot.Value.Grade is not (TransportHealthGrade.Excellent or TransportHealthGrade.Good) ||
            IsCircuitOpen(usb, now) ||
            !HasBeenGoodFor(usb, _policy.UsbRecoveryStability, now))
        {
            return null;
        }

        State = ConnectionManagerState.Handover;

        return new HandoverProposal(
            activeKind,
            TransportKind.Usb,
            HandoverReason.PreferredUsbReady,
            FailureInduced: false);
    }

    private HandoverProposal? EvaluateDegradedActive(
        TransportKind activeKind,
        TransportHealthSnapshot activeSnapshot,
        long now)
    {
        if (activeSnapshot.Grade != TransportHealthGrade.Degraded)
        {
            return null;
        }

        CandidateState active = _candidates[activeKind];
        TimeSpan requiredDuration =
            activeKind == TransportKind.Wifi
                ? _policy.WifiDegradedBeforeFailover
                : _policy.NormalCandidateAdvantageDuration;

        if (active.DegradedSince is null ||
            Elapsed(active.DegradedSince.Value, now) < requiredDuration)
        {
            State = ConnectionManagerState.Degraded;
            return null;
        }

        TransportKind? replacement = SelectBestCandidate(
            now,
            minimumScore: _policy.GoodCandidateScore,
            requireGood: true,
            exclude: activeKind);

        if (replacement is null)
        {
            State = ConnectionManagerState.Degraded;
            return null;
        }

        State = ConnectionManagerState.Handover;

        return new HandoverProposal(
            activeKind,
            replacement.Value,
            HandoverReason.ActiveDegraded,
            FailureInduced: true);
    }

    private HandoverProposal? EvaluateWifiRecovery(
        TransportKind activeKind,
        long now)
    {
        if (activeKind != TransportKind.Bluetooth)
        {
            return null;
        }

        CandidateState wifi = _candidates[TransportKind.Wifi];
        TransportHealthSnapshot? wifiSnapshot = wifi.Snapshot;

        if (wifiSnapshot is null ||
            !IsEligibleState(wifiSnapshot.Value.State) ||
            wifiSnapshot.Value.Grade is not (TransportHealthGrade.Excellent or TransportHealthGrade.Good) ||
            IsCircuitOpen(wifi, now))
        {
            return null;
        }

        TimeSpan required =
            HadRecentHardFailure(wifi, now)
                ? _policy.WifiFailedRecoveryStability
                : _policy.WifiDegradedRecoveryStability;

        if (!HasBeenGoodFor(wifi, required, now))
        {
            return null;
        }

        CandidateState bluetooth = _candidates[TransportKind.Bluetooth];
        TransportHealthSnapshot? bluetoothSnapshot = bluetooth.Snapshot;

        if (bluetoothSnapshot is not null &&
            EffectiveScore(TransportKind.Wifi, wifi, wifiSnapshot.Value, now) <
            EffectiveScore(TransportKind.Bluetooth, bluetooth, bluetoothSnapshot.Value, now))
        {
            return null;
        }

        State = ConnectionManagerState.Handover;

        return new HandoverProposal(
            activeKind,
            TransportKind.Wifi,
            HandoverReason.PreferredWifiRecovered,
            FailureInduced: false);
    }

    private HandoverProposal? EvaluateBetterCandidate(
        TransportKind activeKind,
        TransportHealthSnapshot activeSnapshot,
        long now)
    {
        CandidateState active = _candidates[activeKind];
        int activeScore = EffectiveScore(activeKind, active, activeSnapshot, now);

        TransportKind? best = SelectBestCandidate(
            now,
            minimumScore: _policy.GoodCandidateScore,
            requireGood: true,
            exclude: activeKind);

        if (best is null)
        {
            return null;
        }

        CandidateState bestState = _candidates[best.Value];
        TransportHealthSnapshot bestSnapshot = bestState.Snapshot!.Value;
        int bestScore = EffectiveScore(best.Value, bestState, bestSnapshot, now);

        if (bestScore < activeScore + _policy.NormalSwitchScoreMargin ||
            !HasBeenGoodFor(bestState, _policy.NormalCandidateAdvantageDuration, now))
        {
            return null;
        }

        State = ConnectionManagerState.Handover;

        return new HandoverProposal(
            activeKind,
            best.Value,
            HandoverReason.BetterCandidate,
            FailureInduced: false);
    }

    private TransportKind? SelectBestCandidate(
        long now,
        int minimumScore,
        bool requireGood,
        TransportKind? exclude)
    {
        return _candidates
            .Where(pair =>
                pair.Key != exclude &&
                pair.Value.Snapshot is not null &&
                !IsCircuitOpen(pair.Value, now) &&
                IsEligibleState(pair.Value.Snapshot!.Value.State) &&
                (!requireGood ||
                 pair.Value.Snapshot!.Value.Grade is TransportHealthGrade.Excellent or TransportHealthGrade.Good) &&
                EffectiveScore(pair.Key, pair.Value, pair.Value.Snapshot!.Value, now) >= minimumScore)
            .OrderByDescending(pair =>
                EffectiveScore(pair.Key, pair.Value, pair.Value.Snapshot!.Value, now))
            .ThenBy(pair => PreferenceOrder(pair.Key))
            .Select(pair => (TransportKind?)pair.Key)
            .FirstOrDefault();
    }

    private int EffectiveScore(
        TransportKind kind,
        CandidateState candidate,
        TransportHealthSnapshot snapshot,
        long now)
    {
        int preferenceBonus = kind switch
        {
            TransportKind.Usb => _policy.UsbPreferenceBonus,
            TransportKind.Wifi => _policy.WifiPreferenceBonus,
            TransportKind.Bluetooth => _policy.BluetoothPreferenceBonus,
            _ => 0,
        };

        int failurePenalty =
            FailurePenaltyPoints(candidate, now);

        int preferredScore =
            Math.Clamp(
                snapshot.Score + preferenceBonus,
                0,
                100);

        // Failure history decays continuously over the configured long
        // reliability horizon instead of disappearing in one abrupt step.
        return Math.Clamp(
            preferredScore - failurePenalty,
            0,
            100);
    }

    private int RecentFailureCount(
        CandidateState candidate,
        long now)
    {
        TrimFailures(
            candidate,
            _policy.FailurePenaltyWindow,
            now);

        return candidate.Failures.Count;
    }

    private int FailurePenaltyPoints(
        CandidateState candidate,
        long now)
    {
        TrimFailures(
            candidate,
            _policy.FailurePenaltyWindow,
            now);

        if (candidate.Failures.Count == 0)
            return 0;

        double windowMs =
            _policy.FailurePenaltyWindow.TotalMilliseconds;

        if (windowMs <= 0)
            return 0;

        double penalty = 0;

        foreach (long failureAt in candidate.Failures)
        {
            double ageMs =
                Elapsed(
                    failureAt,
                    now)
                .TotalMilliseconds;

            double remaining =
                Math.Clamp(
                    1d - (ageMs / windowMs),
                    0d,
                    1d);

            penalty += 12d * remaining;
        }

        return Math.Clamp(
            (int)Math.Round(penalty),
            0,
            50);
    }

    private bool HadRecentHardFailure(CandidateState candidate, long now)
    {
        return candidate.LastHardFailureAt is not null &&
               Elapsed(candidate.LastHardFailureAt.Value, now) <= _policy.FailurePenaltyWindow;
    }

    private bool HasBeenGoodFor(
        CandidateState candidate,
        TimeSpan required,
        long now)
    {
        return candidate.GoodSince is not null &&
               Elapsed(candidate.GoodSince.Value, now) >= required;
    }

    private bool IsCircuitOpen(CandidateState candidate, long now)
    {
        if (candidate.CircuitOpenedAt is null)
        {
            return false;
        }

        if (Elapsed(candidate.CircuitOpenedAt.Value, now) < candidate.CircuitOpenDuration)
        {
            return true;
        }

        candidate.CircuitOpenedAt = null;
        candidate.CircuitOpenDuration = TimeSpan.Zero;
        return false;
    }

    private bool IsCooldownActive(long now)
    {
        if (_cooldownStartedAt is null)
        {
            return false;
        }

        if (Elapsed(_cooldownStartedAt.Value, now) < _cooldownDuration)
        {
            return true;
        }

        _cooldownStartedAt = null;
        _cooldownDuration = TimeSpan.Zero;
        return false;
    }

    private void RefreshCooldown(long now)
    {
        if (!IsCooldownActive(now) && ActiveTransport is not null)
        {
            State = ConnectionManagerState.Active;
        }
    }

    private void RefreshManagerState()
    {
        if (ActiveTransport is not null)
            return;

        TransportHealthSnapshot[] snapshots =
            _candidates.Values
                .Where(candidate =>
                    candidate.Snapshot is not null)
                .Select(candidate =>
                    candidate.Snapshot!.Value)
                .ToArray();

        if (snapshots.Length == 0)
        {
            State = ConnectionManagerState.Disconnected;
            return;
        }

        if (snapshots.Any(snapshot =>
                snapshot.State ==
                TransportRuntimeState.Authenticating))
        {
            State = ConnectionManagerState.Authenticating;
            return;
        }

        if (snapshots.Any(snapshot =>
                snapshot.State ==
                TransportRuntimeState.Connecting))
        {
            State = ConnectionManagerState.Connecting;
            return;
        }

        if (snapshots.Any(snapshot =>
                snapshot.State ==
                TransportRuntimeState.Stabilizing))
        {
            State = ConnectionManagerState.Stabilizing;
            return;
        }

        if (snapshots.Any(snapshot =>
                snapshot.State is
                    TransportRuntimeState.Ready or
                    TransportRuntimeState.Active or
                    TransportRuntimeState.Suspect or
                    TransportRuntimeState.Degraded))
        {
            State = ConnectionManagerState.Ready;
            return;
        }

        State = ConnectionManagerState.Discovering;
    }

    private static bool IsEligibleState(TransportRuntimeState state)
    {
        return state is
            TransportRuntimeState.Ready or
            TransportRuntimeState.Active or
            TransportRuntimeState.Degraded;
    }

    private static int PreferenceOrder(TransportKind kind)
    {
        return kind switch
        {
            TransportKind.Usb => 0,
            TransportKind.Wifi => 1,
            TransportKind.Bluetooth => 2,
            _ => int.MaxValue,
        };
    }

    private TimeSpan Elapsed(long start, long end)
    {
        return _timeProvider.GetElapsedTime(start, end);
    }

    private void TrimFailures(
        CandidateState candidate,
        TimeSpan window,
        long now)
    {
        candidate.Failures.RemoveAll(
            timestamp => Elapsed(timestamp, now) > window);
    }

    private sealed class CandidateState
    {
        public TransportHealthSnapshot? Snapshot { get; set; }

        public long? GoodSince { get; set; }

        public long? DegradedSince { get; set; }

        public long? LastHardFailureAt { get; set; }

        public List<long> Failures { get; } = new();

        public int CircuitOpenLevel { get; set; }

        public long? CircuitOpenedAt { get; set; }

        public TimeSpan CircuitOpenDuration { get; set; }
    }
}
