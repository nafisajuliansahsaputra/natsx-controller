using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public readonly record struct TransportHealthWindowSummary(
    TimeSpan Window,
    int SampleCount,
    TimeSpan AverageRoundTripTime,
    TimeSpan AverageJitter,
    double AveragePacketLossPercent,
    TimeSpan MaximumSilence,
    int AverageScore,
    TransportHealthGrade WorstGrade);

public sealed class TransportHealthMonitor
{
    private readonly ConnectionPolicy _policy;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<TransportKind, List<TimedSample>> _samples = new();

    public TransportHealthMonitor(
        ConnectionPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _policy = policy ?? ConnectionPolicy.Competitive;
        _timeProvider = timeProvider ?? TimeProvider.System;

        foreach (TransportKind transport in Enum.GetValues<TransportKind>())
            _samples[transport] = new List<TimedSample>();
    }

    public void Report(TransportHealthSnapshot snapshot)
    {
        long now = _timeProvider.GetTimestamp();
        List<TimedSample> samples = _samples[snapshot.Transport];

        samples.Add(new TimedSample(now, snapshot));
        Trim(samples, _policy.LongWindow, now);
    }

    public TransportHealthWindowSummary GetFast(TransportKind transport) =>
        Summarize(transport, _policy.FastWindow);

    public TransportHealthWindowSummary GetNormal(TransportKind transport) =>
        Summarize(transport, _policy.NormalWindow);

    public TransportHealthWindowSummary GetLong(TransportKind transport) =>
        Summarize(transport, _policy.LongWindow);

    public TransportHealthWindowSummary Summarize(
        TransportKind transport,
        TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));

        long now = _timeProvider.GetTimestamp();
        List<TimedSample> samples = _samples[transport];
        Trim(samples, _policy.LongWindow, now);

        List<TransportHealthSnapshot> inWindow = samples
            .Where(sample => _timeProvider.GetElapsedTime(sample.Timestamp, now) <= window)
            .Select(sample => sample.Snapshot)
            .ToList();

        if (inWindow.Count == 0)
        {
            return new TransportHealthWindowSummary(
                window,
                0,
                TimeSpan.Zero,
                TimeSpan.Zero,
                0,
                TimeSpan.MaxValue,
                0,
                TransportHealthGrade.Lost);
        }

        return new TransportHealthWindowSummary(
            window,
            inWindow.Count,
            TimeSpan.FromTicks((long)inWindow.Average(x => x.RoundTripTime.Ticks)),
            TimeSpan.FromTicks((long)inWindow.Average(x => x.Jitter.Ticks)),
            inWindow.Average(x => x.PacketLossPercent),
            inWindow.Max(x => x.Silence),
            (int)Math.Round(inWindow.Average(x => x.Score)),
            inWindow.Max(x => x.Grade));
    }

    private void Trim(
        List<TimedSample> samples,
        TimeSpan window,
        long now)
    {
        samples.RemoveAll(
            sample => _timeProvider.GetElapsedTime(sample.Timestamp, now) > window);
    }

    private readonly record struct TimedSample(
        long Timestamp,
        TransportHealthSnapshot Snapshot);
}

public sealed class TransportSilenceTracker
{
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<TransportKind, long?> _lastFreshPacket = new();

    public TransportSilenceTracker(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        foreach (TransportKind transport in Enum.GetValues<TransportKind>())
            _lastFreshPacket[transport] = null;
    }

    public void MarkFreshPacket(TransportKind transport)
    {
        _lastFreshPacket[transport] = _timeProvider.GetTimestamp();
    }

    public void Clear(TransportKind transport)
    {
        _lastFreshPacket[transport] = null;
    }

    public TimeSpan GetSilence(TransportKind transport)
    {
        long? last = _lastFreshPacket[transport];

        return last is null
            ? TimeSpan.MaxValue
            : _timeProvider.GetElapsedTime(
                last.Value,
                _timeProvider.GetTimestamp());
    }

    public TransportHealthGrade Classify(
        TransportKind transport,
        ConnectionPolicy? policy = null)
    {
        ConnectionPolicy activePolicy =
            policy ?? ConnectionPolicy.Competitive;

        TimeSpan silence = GetSilence(transport);

        if (silence <= activePolicy.SuspectSilence)
            return TransportHealthGrade.Excellent;

        if (silence <= activePolicy.DegradedSilence)
            return TransportHealthGrade.Warning;

        if (silence <= activePolicy.FailoverSilence)
            return TransportHealthGrade.Degraded;

        if (silence <= activePolicy.LostSilence)
            return TransportHealthGrade.Critical;

        return TransportHealthGrade.Lost;
    }
}
