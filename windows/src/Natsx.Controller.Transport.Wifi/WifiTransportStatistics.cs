using Natsx.Controller.Connection;
using Natsx.Controller.Core;

namespace Natsx.Controller.Transport.Wifi;

internal sealed class WifiTransportStatistics
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Queue<PacketSample> _packetSamples = new();

    private long? _lastPacketAt;
    private long? _lastInterArrivalAt;
    private TimeSpan _jitter;
    private TimeSpan _rtt;
    private uint? _lastSequence;

    public WifiTransportStatistics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void RecordState(uint sequence)
    {
        lock (_gate)
        {
            long now = _timeProvider.GetTimestamp();
            int lost = 0;

            if (_lastSequence is uint previous &&
                SequenceNumber.IsNewer(sequence, previous))
            {
                uint delta = unchecked(sequence - previous);
                if (delta > 1 && delta <= 257)
                {
                    lost = checked((int)delta - 1);
                }
            }

            _lastSequence = sequence;
            _lastPacketAt = now;

            if (_lastInterArrivalAt is long previousArrival)
            {
                TimeSpan interval = _timeProvider.GetElapsedTime(previousArrival, now);
                TimeSpan expected = TimeSpan.FromMilliseconds(1000d / 120d);
                double deviationMs = Math.Abs((interval - expected).TotalMilliseconds);
                _jitter = TimeSpan.FromMilliseconds(
                    (_jitter.TotalMilliseconds * 0.8) + (deviationMs * 0.2));
            }

            _lastInterArrivalAt = now;
            _packetSamples.Enqueue(new PacketSample(now, 1, lost));
            Trim(now);
        }
    }

    public void RecordHeartbeatRoundTrip(TimeSpan roundTrip)
    {
        if (roundTrip < TimeSpan.Zero)
        {
            return;
        }

        lock (_gate)
        {
            _rtt = roundTrip;
        }
    }

    public TransportMetrics Snapshot(TransportRuntimeState state)
    {
        lock (_gate)
        {
            long now = _timeProvider.GetTimestamp();
            Trim(now);

            int received = 0;
            int lost = 0;
            foreach (PacketSample sample in _packetSamples)
            {
                received += sample.Received;
                lost += sample.Lost;
            }

            int total = received + lost;
            double lossPercent = total == 0 ? 0 : lost * 100d / total;

            TimeSpan silence = _lastPacketAt is long last
                ? _timeProvider.GetElapsedTime(last, now)
                : TimeSpan.Zero;

            return new TransportMetrics(
                TransportKind.Wifi,
                state,
                _rtt,
                _jitter,
                lossPercent,
                silence);
        }
    }

    private void Trim(long now)
    {
        TimeSpan window = TimeSpan.FromSeconds(2);

        while (_packetSamples.Count > 0 &&
               _timeProvider.GetElapsedTime(_packetSamples.Peek().Timestamp, now) > window)
        {
            _packetSamples.Dequeue();
        }
    }

    private readonly record struct PacketSample(long Timestamp, int Received, int Lost);
}
