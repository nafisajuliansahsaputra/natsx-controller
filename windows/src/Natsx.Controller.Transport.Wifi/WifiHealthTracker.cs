using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

public sealed class WifiHealthTracker
{
    public static readonly TimeSpan DefaultLossWindow =
        TimeSpan.FromSeconds(2);

    private const uint MaximumCountedSequenceGap = 4096;

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lossWindow;
    private readonly Queue<LossObservation> _lossObservations = new();

    private bool _hasSequence;
    private uint _lastSequence;
    private long _expectedPackets;
    private long _receivedPackets;

    private TimeSpan? _lastRoundTripTime;
    private double _jitterMilliseconds;

    public WifiHealthTracker(
        TimeProvider? timeProvider = null,
        TimeSpan? lossWindow = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lossWindow = lossWindow ?? DefaultLossWindow;

        if (_lossWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lossWindow));
        }
    }

    public void RecordState(uint sequence)
    {
        lock (_gate)
        {
            long now = _timeProvider.GetTimestamp();

            if (!_hasSequence)
            {
                _hasSequence = true;
                _lastSequence = sequence;
                AddLossObservation(now, expected: 1, received: 1);
                TrimLossWindow(now);
                return;
            }

            if (!SequenceNumber.IsNewer(sequence, _lastSequence))
            {
                TrimLossWindow(now);
                return;
            }

            uint delta = unchecked(sequence - _lastSequence);
            _lastSequence = sequence;

            long expected = Math.Min(delta, MaximumCountedSequenceGap);
            AddLossObservation(now, expected, received: 1);
            TrimLossWindow(now);
        }
    }

    public void RecordRoundTripTime(TimeSpan roundTripTime)
    {
        if (roundTripTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(roundTripTime));
        }

        lock (_gate)
        {
            if (_lastRoundTripTime is not null)
            {
                double deviation = Math.Abs(
                    roundTripTime.TotalMilliseconds -
                    _lastRoundTripTime.Value.TotalMilliseconds);

                _jitterMilliseconds =
                    _jitterMilliseconds == 0
                        ? deviation
                        : (_jitterMilliseconds * 0.75) + (deviation * 0.25);
            }

            _lastRoundTripTime = roundTripTime;
        }
    }

    public WifiHealthSample Snapshot()
    {
        lock (_gate)
        {
            long now = _timeProvider.GetTimestamp();
            TrimLossWindow(now);

            double loss =
                _expectedPackets <= 0
                    ? 0
                    : ((_expectedPackets - _receivedPackets) /
                       (double)_expectedPackets) * 100.0;

            return new WifiHealthSample(
                _lastRoundTripTime,
                TimeSpan.FromMilliseconds(_jitterMilliseconds),
                Math.Clamp(loss, 0, 100));
        }
    }

    public TransportMetrics ToTransportMetrics(
        TransportRuntimeState state,
        TimeSpan silence)
    {
        WifiHealthSample sample = Snapshot();

        return new TransportMetrics(
            TransportKind.Wifi,
            state,
            sample.RoundTripTime ?? TimeSpan.Zero,
            sample.Jitter,
            sample.PacketLossPercent,
            silence);
    }

    private void AddLossObservation(
        long timestamp,
        long expected,
        long received)
    {
        _lossObservations.Enqueue(
            new LossObservation(timestamp, expected, received));

        _expectedPackets += expected;
        _receivedPackets += received;
    }

    private void TrimLossWindow(long now)
    {
        while (_lossObservations.TryPeek(out LossObservation observation) &&
               _timeProvider.GetElapsedTime(observation.Timestamp, now) > _lossWindow)
        {
            _lossObservations.Dequeue();
            _expectedPackets -= observation.Expected;
            _receivedPackets -= observation.Received;
        }
    }

    private readonly record struct LossObservation(
        long Timestamp,
        long Expected,
        long Received);
}

public readonly record struct WifiHealthSample(
    TimeSpan? RoundTripTime,
    TimeSpan Jitter,
    double PacketLossPercent);
