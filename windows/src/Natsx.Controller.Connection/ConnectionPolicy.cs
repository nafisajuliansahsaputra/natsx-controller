namespace Natsx.Controller.Connection;

public sealed record ConnectionPolicy
{
    public TimeSpan FastWindow { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan NormalWindow { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan LongWindow { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan SuspectSilence { get; init; } = TimeSpan.FromMilliseconds(25);
    public TimeSpan DegradedSilence { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan FailoverSilence { get; init; } = TimeSpan.FromMilliseconds(80);
    public TimeSpan LostSilence { get; init; } = TimeSpan.FromMilliseconds(120);
    public TimeSpan NeutralizeSilence { get; init; } = TimeSpan.FromMilliseconds(150);

    public TimeSpan UsbRecoveryStability { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan WifiDegradedRecoveryStability { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan WifiFailedRecoveryStability { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan BluetoothRecoveryStability { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan NormalSwitchCooldown { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan FailureSwitchCooldown { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RepeatedFailureSwitchCooldown { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan CircuitBreakerWindow { get; init; } = TimeSpan.FromSeconds(20);
    public int CircuitBreakerFailureCount { get; init; } = 3;
    public TimeSpan CircuitBreakerInitialOpen { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CircuitBreakerSecondOpen { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CircuitBreakerMaximumOpen { get; init; } = TimeSpan.FromSeconds(60);

    public int NormalSwitchScoreMargin { get; init; } = 15;
    public int GoodCandidateScore { get; init; } = 70;
    public int UsableCandidateScore { get; init; } = 45;

    public static ConnectionPolicy Competitive { get; } = new();
}
