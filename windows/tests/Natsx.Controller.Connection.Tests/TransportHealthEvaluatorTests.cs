using Natsx.Controller.Core;
using Natsx.Controller.Connection;

namespace Natsx.Controller.Connection.Tests;

public sealed class TransportHealthEvaluatorTests
{
    private readonly TransportHealthEvaluator _evaluator = new();

    [Fact]
    public void HealthyWifi_IsExcellent()
    {
        TransportHealthSnapshot result = _evaluator.Evaluate(
            new TransportMetrics(
                TransportKind.Wifi,
                TransportRuntimeState.Ready,
                TimeSpan.FromMilliseconds(5),
                TimeSpan.FromMilliseconds(1),
                0.1,
                TimeSpan.FromMilliseconds(5)));

        Assert.Equal(TransportHealthGrade.Excellent, result.Grade);
        Assert.True(result.Score >= 95);
    }

    [Fact]
    public void WifiAboveCriticalLatency_IsCritical()
    {
        TransportHealthSnapshot result = _evaluator.Evaluate(
            new TransportMetrics(
                TransportKind.Wifi,
                TransportRuntimeState.Ready,
                TimeSpan.FromMilliseconds(61),
                TimeSpan.FromMilliseconds(1),
                0,
                TimeSpan.FromMilliseconds(5)));

        Assert.Equal(TransportHealthGrade.Critical, result.Grade);
    }

    [Fact]
    public void SilenceBeyondLostThreshold_IsLost()
    {
        TransportHealthSnapshot result = _evaluator.Evaluate(
            new TransportMetrics(
                TransportKind.Bluetooth,
                TransportRuntimeState.Ready,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(2),
                0,
                TimeSpan.FromMilliseconds(121)));

        Assert.Equal(TransportHealthGrade.Lost, result.Grade);
    }

    [Theory]
    [InlineData(0.2, TransportHealthGrade.Excellent)]
    [InlineData(0.5, TransportHealthGrade.Good)]
    [InlineData(2.0, TransportHealthGrade.Warning)]
    [InlineData(5.0, TransportHealthGrade.Degraded)]
    [InlineData(9.0, TransportHealthGrade.Critical)]
    public void PacketLoss_MapsToExpectedBand(
        double loss,
        TransportHealthGrade expected)
    {
        TransportHealthSnapshot result = _evaluator.Evaluate(
            new TransportMetrics(
                TransportKind.Wifi,
                TransportRuntimeState.Ready,
                TimeSpan.FromMilliseconds(5),
                TimeSpan.FromMilliseconds(1),
                loss,
                TimeSpan.FromMilliseconds(5)));

        Assert.Equal(expected, result.Grade);
    }
}
