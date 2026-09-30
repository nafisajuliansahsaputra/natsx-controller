namespace Natsx.Controller.Core;

public readonly record struct RumbleState(
    byte LowFrequencyMotor,
    byte HighFrequencyMotor);
