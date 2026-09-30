using Natsx.Controller.Core;

namespace Natsx.Controller.Connection;

public sealed class TransportHealthEvaluator
{
    private readonly ConnectionPolicy _policy;

    public TransportHealthEvaluator(ConnectionPolicy? policy = null)
    {
        _policy = policy ?? ConnectionPolicy.Competitive;
    }

    public TransportHealthSnapshot Evaluate(TransportMetrics metrics)
    {
        Validate(metrics);

        TransportHealthGrade silenceGrade = ClassifySilence(metrics.Silence);
        TransportHealthGrade latencyGrade = ClassifyLatency(metrics.Transport, metrics.RoundTripTime);
        TransportHealthGrade jitterGrade = ClassifyJitter(metrics.Jitter);
        TransportHealthGrade lossGrade = ClassifyLoss(metrics.PacketLossPercent);

        TransportHealthGrade grade = Worst(
            silenceGrade,
            latencyGrade,
            jitterGrade,
            lossGrade);

        int score = 100;
        score -= PenaltyForLatency(latencyGrade);
        score -= PenaltyForJitter(jitterGrade);
        score -= PenaltyForLoss(lossGrade);
        score -= PenaltyForSilence(silenceGrade);

        if (metrics.State is TransportRuntimeState.Failed or TransportRuntimeState.Unavailable)
        {
            grade = TransportHealthGrade.Lost;
            score = 0;
        }

        return new TransportHealthSnapshot(
            metrics.Transport,
            metrics.State,
            metrics.RoundTripTime,
            metrics.Jitter,
            metrics.PacketLossPercent,
            metrics.Silence,
            Math.Clamp(score, 0, 100),
            grade);
    }

    private TransportHealthGrade ClassifySilence(TimeSpan silence)
    {
        if (silence <= _policy.SuspectSilence)
        {
            return TransportHealthGrade.Excellent;
        }

        if (silence <= _policy.DegradedSilence)
        {
            return TransportHealthGrade.Warning;
        }

        if (silence <= _policy.FailoverSilence)
        {
            return TransportHealthGrade.Degraded;
        }

        if (silence <= _policy.LostSilence)
        {
            return TransportHealthGrade.Critical;
        }

        return TransportHealthGrade.Lost;
    }

    private TransportHealthGrade ClassifyLatency(
        TransportKind transport,
        TimeSpan rtt)
    {
        var thresholds = transport switch
        {
            TransportKind.Usb => (
                _policy.UsbExcellentRtt,
                _policy.UsbGoodRtt,
                _policy.UsbWarningRtt,
                _policy.UsbDegradedRtt),
            TransportKind.Wifi => (
                _policy.WifiExcellentRtt,
                _policy.WifiGoodRtt,
                _policy.WifiWarningRtt,
                _policy.WifiDegradedRtt),
            TransportKind.Bluetooth => (
                _policy.BluetoothExcellentRtt,
                _policy.BluetoothGoodRtt,
                _policy.BluetoothWarningRtt,
                _policy.BluetoothDegradedRtt),
            _ => throw new ArgumentOutOfRangeException(nameof(transport)),
        };

        if (rtt <= thresholds.Item1)
        {
            return TransportHealthGrade.Excellent;
        }

        if (rtt <= thresholds.Item2)
        {
            return TransportHealthGrade.Good;
        }

        if (rtt <= thresholds.Item3)
        {
            return TransportHealthGrade.Warning;
        }

        if (rtt <= thresholds.Item4)
        {
            return TransportHealthGrade.Degraded;
        }

        return TransportHealthGrade.Critical;
    }

    private TransportHealthGrade ClassifyJitter(TimeSpan jitter)
    {
        if (jitter < _policy.ExcellentJitter)
        {
            return TransportHealthGrade.Excellent;
        }

        if (jitter <= _policy.GoodJitter)
        {
            return TransportHealthGrade.Good;
        }

        if (jitter <= _policy.WarningJitter)
        {
            return TransportHealthGrade.Warning;
        }

        if (jitter <= _policy.DegradedJitter)
        {
            return TransportHealthGrade.Degraded;
        }

        return TransportHealthGrade.Critical;
    }

    private TransportHealthGrade ClassifyLoss(double packetLossPercent)
    {
        if (packetLossPercent < _policy.ExcellentPacketLossPercent)
        {
            return TransportHealthGrade.Excellent;
        }

        if (packetLossPercent <= _policy.GoodPacketLossPercent)
        {
            return TransportHealthGrade.Good;
        }

        if (packetLossPercent <= _policy.WarningPacketLossPercent)
        {
            return TransportHealthGrade.Warning;
        }

        if (packetLossPercent <= _policy.DegradedPacketLossPercent)
        {
            return TransportHealthGrade.Degraded;
        }

        return TransportHealthGrade.Critical;
    }

    private static int PenaltyForLatency(TransportHealthGrade grade)
    {
        return grade switch
        {
            TransportHealthGrade.Excellent => 0,
            TransportHealthGrade.Good => 4,
            TransportHealthGrade.Warning => 12,
            TransportHealthGrade.Degraded => 24,
            TransportHealthGrade.Critical => 42,
            TransportHealthGrade.Lost => 60,
            _ => 0,
        };
    }

    private static int PenaltyForJitter(TransportHealthGrade grade)
    {
        return grade switch
        {
            TransportHealthGrade.Excellent => 0,
            TransportHealthGrade.Good => 3,
            TransportHealthGrade.Warning => 8,
            TransportHealthGrade.Degraded => 16,
            TransportHealthGrade.Critical => 28,
            TransportHealthGrade.Lost => 40,
            _ => 0,
        };
    }

    private static int PenaltyForLoss(TransportHealthGrade grade)
    {
        return grade switch
        {
            TransportHealthGrade.Excellent => 0,
            TransportHealthGrade.Good => 5,
            TransportHealthGrade.Warning => 12,
            TransportHealthGrade.Degraded => 25,
            TransportHealthGrade.Critical => 45,
            TransportHealthGrade.Lost => 60,
            _ => 0,
        };
    }

    private static int PenaltyForSilence(TransportHealthGrade grade)
    {
        return grade switch
        {
            TransportHealthGrade.Excellent => 0,
            TransportHealthGrade.Good => 2,
            TransportHealthGrade.Warning => 8,
            TransportHealthGrade.Degraded => 18,
            TransportHealthGrade.Critical => 35,
            TransportHealthGrade.Lost => 70,
            _ => 0,
        };
    }

    private static TransportHealthGrade Worst(
        params TransportHealthGrade[] grades)
    {
        return grades.Max();
    }

    private static void Validate(TransportMetrics metrics)
    {
        if (metrics.RoundTripTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "RTT cannot be negative.");
        }

        if (metrics.Jitter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Jitter cannot be negative.");
        }

        if (metrics.Silence < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Silence cannot be negative.");
        }

        if (double.IsNaN(metrics.PacketLossPercent) ||
            double.IsInfinity(metrics.PacketLossPercent) ||
            metrics.PacketLossPercent < 0 ||
            metrics.PacketLossPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(metrics), "Packet loss must be between 0 and 100 percent.");
        }
    }
}
