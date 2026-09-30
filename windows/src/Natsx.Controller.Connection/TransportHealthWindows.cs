namespace Natsx.Controller.Connection;

public readonly record struct TransportHealthWindowSummary(
    TimeSpan Duration,
    int SampleCount,
    TimeSpan Coverage,
    int AverageScore,
    int MinimumScore,
    TransportHealthGrade WorstGrade)
{
    public bool HasSamples => SampleCount > 0;
}

public readonly record struct TransportHealthWindows(
    TransportHealthWindowSummary Fast,
    TransportHealthWindowSummary Normal,
    TransportHealthWindowSummary Long);

internal sealed class TransportHealthWindowHistory
{
    private readonly TimeProvider _timeProvider;
    private readonly Queue<Observation> _observations = new();

    public TransportHealthWindowHistory(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public void Record(
        long timestamp,
        TransportHealthSnapshot snapshot,
        TimeSpan longestWindow)
    {
        _observations.Enqueue(new Observation(timestamp, snapshot));
        Trim(longestWindow, timestamp);
    }

    public TransportHealthWindows Snapshot(
        long now,
        ConnectionPolicy policy)
    {
        Trim(policy.LongWindow, now);

        return new TransportHealthWindows(
            Summarize(policy.FastWindow, now),
            Summarize(policy.NormalWindow, now),
            Summarize(policy.LongWindow, now));
    }

    private TransportHealthWindowSummary Summarize(
        TimeSpan duration,
        long now)
    {
        int count = 0;
        int totalScore = 0;
        int minimumScore = 100;
        TransportHealthGrade worstGrade = TransportHealthGrade.Excellent;
        long? oldestTimestamp = null;

        foreach (Observation observation in _observations)
        {
            if (_timeProvider.GetElapsedTime(observation.Timestamp, now) > duration)
            {
                continue;
            }

            count++;
            totalScore += observation.Snapshot.Score;
            minimumScore = Math.Min(minimumScore, observation.Snapshot.Score);
            worstGrade = (TransportHealthGrade)Math.Max(
                (int)worstGrade,
                (int)observation.Snapshot.Grade);
            oldestTimestamp ??= observation.Timestamp;
        }

        if (count == 0)
        {
            return new TransportHealthWindowSummary(
                duration,
                0,
                TimeSpan.Zero,
                0,
                0,
                TransportHealthGrade.Lost);
        }

        TimeSpan coverage = _timeProvider.GetElapsedTime(
            oldestTimestamp!.Value,
            now);

        if (coverage > duration)
        {
            coverage = duration;
        }

        int averageScore = (int)Math.Round(
            totalScore / (double)count,
            MidpointRounding.AwayFromZero);

        return new TransportHealthWindowSummary(
            duration,
            count,
            coverage,
            averageScore,
            minimumScore,
            worstGrade);
    }

    private void Trim(TimeSpan longestWindow, long now)
    {
        while (_observations.TryPeek(out Observation observation) &&
               _timeProvider.GetElapsedTime(observation.Timestamp, now) > longestWindow)
        {
            _observations.Dequeue();
        }
    }

    private readonly record struct Observation(
        long Timestamp,
        TransportHealthSnapshot Snapshot);
}
