using HIDMaestro;
using Natsx.Controller.Core;

namespace Natsx.Controller.VirtualGamepad;

public static class HidMaestroStateMapper
{
    public static HMButton MapButtons(GamepadButtons buttons)
    {
        HMButton result = HMButton.None;

        if (buttons.HasFlag(GamepadButtons.A)) result |= HMButton.A;
        if (buttons.HasFlag(GamepadButtons.B)) result |= HMButton.B;
        if (buttons.HasFlag(GamepadButtons.X)) result |= HMButton.X;
        if (buttons.HasFlag(GamepadButtons.Y)) result |= HMButton.Y;
        if (buttons.HasFlag(GamepadButtons.LeftShoulder)) result |= HMButton.LeftBumper;
        if (buttons.HasFlag(GamepadButtons.RightShoulder)) result |= HMButton.RightBumper;
        if (buttons.HasFlag(GamepadButtons.Back)) result |= HMButton.Back;
        if (buttons.HasFlag(GamepadButtons.Start)) result |= HMButton.Start;
        if (buttons.HasFlag(GamepadButtons.LeftStick)) result |= HMButton.LeftStick;
        if (buttons.HasFlag(GamepadButtons.RightStick)) result |= HMButton.RightStick;
        if (buttons.HasFlag(GamepadButtons.Guide)) result |= HMButton.Guide;

        return result;
    }

    public static HMHat MapHat(DpadState dpad)
    {
        bool up = dpad.HasFlag(DpadState.Up);
        bool down = dpad.HasFlag(DpadState.Down);
        bool left = dpad.HasFlag(DpadState.Left);
        bool right = dpad.HasFlag(DpadState.Right);

        return (up, down, left, right) switch
        {
            (false, false, false, false) => HMHat.None,
            (true, false, false, false) => HMHat.North,
            (true, false, false, true) => HMHat.NorthEast,
            (false, false, false, true) => HMHat.East,
            (false, true, false, true) => HMHat.SouthEast,
            (false, true, false, false) => HMHat.South,
            (false, true, true, false) => HMHat.SouthWest,
            (false, false, true, false) => HMHat.West,
            (true, false, true, false) => HMHat.NorthWest,
            _ => HMHat.None,
        };
    }

    public static float NormalizeStick(short value)
    {
        return value >= 0
            ? 0.5f + (value / (float)short.MaxValue) * 0.5f
            : 0.5f + (value / 32768f) * 0.5f;
    }

    // Protocol/XInput: positive Y is up. HIDMaestro HID axes: 0 is up,
    // 1 is down; its XInput companion reverses Y when exposing the gamepad.
    public static float NormalizeVerticalStick(short value)
    {
        return 1f - NormalizeStick(value);
    }

    public static float NormalizeTrigger(byte value)
    {
        return value / 255f;
    }
}
