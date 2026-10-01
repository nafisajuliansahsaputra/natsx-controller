using Natsx.Controller.Connection;

namespace Natsx.Controller.Transport.Usb;

public sealed class UsbHealthTracker
{
    private readonly object _gate = new();

    private TimeSpan? _lastRoundTripTime;
    private double _jitterMilliseconds;

    public void RecordRoundTripTime(
        TimeSpan roundTripTime)
    {
        if (roundTripTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roundTripTime));
        }

        lock (_gate)
        {
            if (_lastRoundTripTime is not null)
            {
                double deviation =
                    Math.Abs(
                        roundTripTime.TotalMilliseconds -
                        _lastRoundTripTime.Value
                            .TotalMilliseconds);

                _jitterMilliseconds =
                    _jitterMilliseconds == 0
                        ? deviation
                        : (_jitterMilliseconds * 0.75) +
                          (deviation * 0.25);
            }

            _lastRoundTripTime =
                roundTripTime;
        }
    }

    public UsbHealthSample Snapshot()
    {
        lock (_gate)
        {
            return new UsbHealthSample(
                _lastRoundTripTime,
                TimeSpan.FromMilliseconds(
                    _jitterMilliseconds));
        }
    }

    public TransportMetrics ToTransportMetrics(
        TransportRuntimeState state,
        TimeSpan silence)
    {
        UsbHealthSample sample =
            Snapshot();

        return new TransportMetrics(
            Natsx.Controller.Core.TransportKind.Usb,
            state,
            sample.RoundTripTime ?? TimeSpan.Zero,
            sample.Jitter,
            0,
            silence);
    }
}

public readonly record struct UsbHealthSample(
    TimeSpan? RoundTripTime,
    TimeSpan Jitter);
