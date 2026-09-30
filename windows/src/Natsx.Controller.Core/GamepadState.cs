namespace Natsx.Controller.Core;

public readonly record struct GamepadState(
    GamepadButtons Buttons,
    DpadState Dpad,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY,
    byte LeftTrigger,
    byte RightTrigger)
{
    public static GamepadState Neutral => new(
        GamepadButtons.None,
        DpadState.Neutral,
        0,
        0,
        0,
        0,
        0,
        0);
}
