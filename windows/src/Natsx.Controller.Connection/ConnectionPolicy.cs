namespace Natsx.Controller.Connection;

public sealed record ConnectionPolicy
{
    public TimeSpan HealthEvaluationInterval { get; init; } = TimeSpan.FromMilliseconds(25);

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

    public TimeSpan WifiDegradedBeforeFailover { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan NormalCandidateAdvantageDuration { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan NormalSwitchCooldown { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan FailureSwitchCooldown { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RepeatedFailureSwitchCooldown { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan CircuitBreakerWindow { get; init; } = TimeSpan.FromSeconds(20);
    public int CircuitBreakerFailureCount { get; init; } = 3;
    public TimeSpan CircuitBreakerInitialOpen { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan CircuitBreakerSecondOpen { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CircuitBreakerMaximumOpen { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan FailurePenaltyWindow { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan WifiExcellentRtt { get; init; } = TimeSpan.FromMilliseconds(8);
    public TimeSpan WifiGoodRtt { get; init; } = TimeSpan.FromMilliseconds(15);
    public TimeSpan WifiWarningRtt { get; init; } = TimeSpan.FromMilliseconds(30);
    public TimeSpan WifiDegradedRtt { get; init; } = TimeSpan.FromMilliseconds(60);

    public TimeSpan BluetoothExcellentRtt { get; init; } = TimeSpan.FromMilliseconds(15);
    public TimeSpan BluetoothGoodRtt { get; init; } = TimeSpan.FromMilliseconds(25);
    public TimeSpan BluetoothWarningRtt { get; init; } = TimeSpan.FromMilliseconds(40);
    public TimeSpan BluetoothDegradedRtt { get; init; } = TimeSpan.FromMilliseconds(70);

    public TimeSpan UsbExcellentRtt { get; init; } = TimeSpan.FromMilliseconds(4);
    public TimeSpan UsbGoodRtt { get; init; } = TimeSpan.FromMilliseconds(8);
    public TimeSpan UsbWarningRtt { get; init; } = TimeSpan.FromMilliseconds(15);
    public TimeSpan UsbDegradedRtt { get; init; } = TimeSpan.FromMilliseconds(30);

    public TimeSpan ExcellentJitter { get; init; } = TimeSpan.FromMilliseconds(3);
    public TimeSpan GoodJitter { get; init; } = TimeSpan.FromMilliseconds(6);
    public TimeSpan WarningJitter { get; init; } = TimeSpan.FromMilliseconds(12);
    public TimeSpan DegradedJitter { get; init; } = TimeSpan.FromMilliseconds(25);

    public double ExcellentPacketLossPercent { get; init; } = 0.25;
    public double GoodPacketLossPercent { get; init; } = 1;
    public double WarningPacketLossPercent { get; init; } = 3;
    public double DegradedPacketLossPercent { get; init; } = 8;
    public double EmergencyPacketLossPercent { get; init; } = 15;

    public int NormalSwitchScoreMargin { get; init; } = 15;
    public int GoodCandidateScore { get; init; } = 70;
    public int UsableCandidateScore { get; init; } = 45;

    public int UsbPreferenceBonus { get; init; } = 20;
    public int WifiPreferenceBonus { get; init; } = 10;
    public int BluetoothPreferenceBonus { get; init; } = 0;

    public static ConnectionPolicy Competitive { get; } = new();
}
