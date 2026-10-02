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
            _candidates[kind] = new CandidateState(_timeProvider);
        }
    }

    public TransportKind? ActiveTransport { get; private set; }

    public TransportKind? PreferredTransport { get; private set; }

    public ConnectionManagerState State { get; private set; } =
        ConnectionManagerState.Disconnected;

    public void Report(TransportHealthSnapshot snapshot)
    {
        long now = _timeProvider.GetTimestamp();
        CandidateState candidate = _candidates[snapshot.Transport];
        TransportHealthSnapshot? previous = candidate.Snapshot;

        candidate.Snapshot = snapshot;

        if (!IsPreReadyState(snapshot.State))
        {
            candidate.HealthHistory.Record(now, snapshot, _policy.LongWindow);
            candidate.HealthWindows =
                candidate.HealthHistory.Snapshot(now, _policy);
        }

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
            (IsEligibleState(snapshot.State) &&
             snapshot.Grade == TransportHealthGrade.Lost);

        bool hardFailureBefore =
            previous is not null &&
            (previous.Value.State is TransportRuntimeState.Failed or TransportRuntimeState.Unavailable ||
             (IsEligibleState(previous.Value.State) &&
              previous.Value.Grade == TransportHealthGrade.Lost));

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

    public void SetPreferredTransport(
        TransportKind? transport)
    {
        PreferredTransport = transport;

        // A user preference change is intentional and should not be delayed
        // by a cooldown created by a previous automatic handover.
        _cooldownStartedAt = null;
        _cooldownDuration = TimeSpan.Zero;
    }

    public HandoverProposal? Evaluate()
    {
        long now = _timeProvider.GetTimestamp();
        RefreshCooldown(now);

        if (ActiveTransport is null)
        {
            TransportKind? initial =
                SelectPreferredCandidate(
                    now,
                    minimumScore: _policy.UsableCandidateScore)
                ?? SelectBestCandidate(
                    now,
                    minimumScore: _policy.UsableCandidateScore,
                    requireGood: false,
                    exclude: null);

            if (initial is null)
            {
                State = DeterminePreActiveState();
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

        HandoverProposal? preferredProposal =
            EvaluateManualPreference(
                activeKind,
                now);

        if (preferredProposal is not null)
        {
            return preferredProposal;
        }

        if (IsCooldownActive(now))
        {
            State = ConnectionManagerState.Cooldown;
            return null;
        }

        if (PreferredTransport is not null &&
            PreferredTransport == activeKind)
        {
            activeCandidate.HealthWindows =
                activeCandidate.HealthHistory.Snapshot(
                    now,
                    _policy);

            State =
                activeSnapshot.Value.Grade ==
                    TransportHealthGrade.Degraded
                    ? ConnectionManagerState.Degraded
                    : ConnectionManagerState.Active;

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

        activeCandidate.HealthWindows =
            activeCandidate.HealthHistory.Snapshot(now, _policy);

        if (activeSnapshot.Value.Grade is
            TransportHealthGrade.Degraded or
            TransportHealthGrade.Critical or
            TransportHealthGrade.Lost)
        {
            State = ConnectionManagerState.Degraded;
        }
        else if (activeCandidate.HealthWindows.Fast.HasSamples &&
                 (int)activeCandidate.HealthWindows.Fast.WorstGrade >=
                 (int)TransportHealthGrade.Warning)
        {
            State = ConnectionManagerState.Suspect;
        }
        else
        {
            State = ConnectionManagerState.Active;
        }

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

    public TransportHealthWindows GetHealthWindows(TransportKind transport)
    {
        long now = _timeProvider.GetTimestamp();
        CandidateState candidate = _candidates[transport];
        candidate.HealthWindows =
            candidate.HealthHistory.Snapshot(now, _policy);
        return candidate.HealthWindows;
    }

    private HandoverProposal? EvaluateManualPreference(
        TransportKind activeKind,
        long now)
    {
        if (PreferredTransport is not TransportKind preferred ||
            preferred == activeKind)
        {
            return null;
        }

        CandidateState candidate =
            _candidates[preferred];
        TransportHealthSnapshot? snapshot =
            candidate.Snapshot;

        if (snapshot is null ||
            !IsEligibleState(snapshot.Value.State) ||
            IsCircuitOpen(candidate, now) ||
            EffectiveScore(
                preferred,
                candidate,
                snapshot.Value,
                now) < _policy.UsableCandidateScore)
        {
            return null;
        }

        State = ConnectionManagerState.Handover;

        return new HandoverProposal(
            activeKind,
            preferred,
            HandoverReason.ManualPreference,
            FailureInduced: false);
    }

    private TransportKind? SelectPreferredCandidate(
        long now,
        int minimumScore)
    {
        if (PreferredTransport is not TransportKind preferred)
        {
            return null;
        }

        CandidateState candidate =
            _candidates[preferred];
        TransportHealthSnapshot? snapshot =
            candidate.Snapshot;

        if (snapshot is null ||
            !IsEligibleState(snapshot.Value.State) ||
            IsCircuitOpen(candidate, now) ||
            EffectiveScore(
                preferred,
                candidate,
                snapshot.Value,
                now) < minimumScore)
        {
            return null;
        }

        return preferred;
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
            .ThenByDescending(pair => LongWindowScore(pair.Value, now))
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

        candidate.HealthWindows =
            candidate.HealthHistory.Snapshot(now, _policy);

        int observedScore = snapshot.Score;

        if (candidate.HealthWindows.Normal.HasSamples)
        {
            observedScore = Math.Min(
                observedScore,
                candidate.HealthWindows.Normal.AverageScore);
        }

        int failurePenalty = FailurePenalty(candidate, now);
        int preferredScore = Math.Clamp(observedScore + preferenceBonus, 0, 100);

        // Preference is a tie-break/transport bias, not a way to erase
        // reliability history. Apply the failure penalty after capping the
        // quality+preference score so repeated failures always reduce trust.
        return Math.Clamp(preferredScore - failurePenalty, 0, 100);
    }

    private int LongWindowScore(CandidateState candidate, long now)
    {
        candidate.HealthWindows =
            candidate.HealthHistory.Snapshot(now, _policy);

        return candidate.HealthWindows.Long.HasSamples
            ? candidate.HealthWindows.Long.AverageScore
            : 0;
    }

    private int FailurePenalty(CandidateState candidate, long now)
    {
        int failures = RecentFailureCount(candidate, now);

        if (failures == 0 || candidate.LastHardFailureAt is null)
        {
            return 0;
        }

        int fullPenalty = failures switch
        {
            1 => 10,
            2 => 20,
            3 => 35,
            _ => 50,
        };

        TimeSpan age = Elapsed(candidate.LastHardFailureAt.Value, now);

        if (age >= _policy.FailurePenaltyWindow)
        {
            return 0;
        }

        double remainingFraction =
            1.0 -
            (age.TotalMilliseconds /
             _policy.FailurePenaltyWindow.TotalMilliseconds);

        return (int)Math.Ceiling(fullPenalty * remainingFraction);
    }

    private int RecentFailureCount(CandidateState candidate, long now)
    {
        TrimFailures(candidate, _policy.FailurePenaltyWindow, now);
        return candidate.Failures.Count;
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
        if (ActiveTransport is null)
        {
            State = DeterminePreActiveState();
        }
    }

    private ConnectionManagerState DeterminePreActiveState()
    {
        TransportRuntimeState[] states = _candidates.Values
            .Where(candidate => candidate.Snapshot is not null)
            .Select(candidate => candidate.Snapshot!.Value.State)
            .ToArray();

        if (states.Length == 0)
        {
            return ConnectionManagerState.Disconnected;
        }

        if (states.Contains(TransportRuntimeState.Stabilizing))
        {
            return ConnectionManagerState.Stabilizing;
        }

        if (states.Contains(TransportRuntimeState.Authenticating))
        {
            return ConnectionManagerState.Authenticating;
        }

        if (states.Contains(TransportRuntimeState.Connecting))
        {
            return ConnectionManagerState.Connecting;
        }

        if (states.Contains(TransportRuntimeState.Ready))
        {
            return ConnectionManagerState.Ready;
        }

        if (states.Contains(TransportRuntimeState.Available))
        {
            return ConnectionManagerState.Discovering;
        }

        return states.Any(state => state == TransportRuntimeState.Failed)
            ? ConnectionManagerState.Recovering
            : ConnectionManagerState.Disconnected;
    }

    private static bool IsPreReadyState(TransportRuntimeState state)
    {
        return state is
            TransportRuntimeState.Available or
            TransportRuntimeState.Connecting or
            TransportRuntimeState.Authenticating or
            TransportRuntimeState.Stabilizing;
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
        public CandidateState(TimeProvider timeProvider)
        {
            HealthHistory = new TransportHealthWindowHistory(timeProvider);
        }

        public TransportHealthSnapshot? Snapshot { get; set; }

        public TransportHealthWindowHistory HealthHistory { get; }

        public TransportHealthWindows HealthWindows { get; set; }

        public long? GoodSince { get; set; }

        public long? DegradedSince { get; set; }

        public long? LastHardFailureAt { get; set; }

        public List<long> Failures { get; } = new();

        public int CircuitOpenLevel { get; set; }

        public long? CircuitOpenedAt { get; set; }

        public TimeSpan CircuitOpenDuration { get; set; }
    }
}
