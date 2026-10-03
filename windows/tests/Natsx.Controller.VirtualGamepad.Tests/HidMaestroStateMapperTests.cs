using HIDMaestro;
using Natsx.Controller.Core;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.VirtualGamepad.Tests;

public sealed class HidMaestroStateMapperTests
{
    [Fact]
    public void NormalizeStick_MapsXboxEndpointsAndCenter()
    {
        Assert.Equal(0f, HidMaestroStateMapper.NormalizeStick(short.MinValue));
        Assert.Equal(0.5f, HidMaestroStateMapper.NormalizeStick(0));
        Assert.Equal(1f, HidMaestroStateMapper.NormalizeStick(short.MaxValue));
    }

    [Theory]
    [InlineData(short.MaxValue, 0f)] // Finger up -> HID up -> XInput positive Y.
    [InlineData((short)0, 0.5f)]
    [InlineData(short.MinValue, 1f)] // Finger down -> HID down -> XInput negative Y.
    [InlineData((short)16384, 0.24999237f)]
    [InlineData((short)-16384, 0.75f)]
    public void VerticalStickPreservesDirectionAndTravel(short input, float expected)
    {
        Assert.Equal(expected, HidMaestroStateMapper.NormalizeVerticalStick(input), 6);
    }

    [Theory]
    [InlineData(GamepadButtons.A, HMButton.A)]
    [InlineData(GamepadButtons.B, HMButton.B)]
    [InlineData(GamepadButtons.X, HMButton.X)]
    [InlineData(GamepadButtons.Y, HMButton.Y)]
    [InlineData(GamepadButtons.LeftShoulder, HMButton.LeftBumper)]
    [InlineData(GamepadButtons.RightShoulder, HMButton.RightBumper)]
    [InlineData(GamepadButtons.LeftStick, HMButton.LeftStick)]
    [InlineData(GamepadButtons.RightStick, HMButton.RightStick)]
    [InlineData(GamepadButtons.Back, HMButton.Back)]
    [InlineData(GamepadButtons.Start, HMButton.Start)]
    [InlineData(GamepadButtons.Guide, HMButton.Guide)]
    [InlineData(GamepadButtons.None, HMButton.None)]
    public void IndividualButtonDoesNotActivateAnother(GamepadButtons input, HMButton expected)
    {
        Assert.Equal(expected, HidMaestroStateMapper.MapButtons(input));
    }

    [Fact]
    public async Task BackendMapsBothSticksAndIndependentTriggersThenReleasesEverything()
    {
        // Exercise the actual native-state preparation without starting a kernel device.
        await using var backend = new HidMaestroVirtualGamepadBackend();
        var type = typeof(HidMaestroVirtualGamepadBackend);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var axes = new Dictionary<HMAxis, float>();
        foreach (var (field, axis) in new[] {
            ("_leftX", HMAxis.X), ("_leftY", HMAxis.Y),
            ("_rightX", HMAxis.Rx), ("_rightY", HMAxis.Ry),
            ("_leftTrigger", HMAxis.Z), ("_rightTrigger", HMAxis.Rz) })
        {
            type.GetField(field, flags)!.SetValue(backend, axis);
            axes[axis] = 0f;
        }
        var stateField = type.GetField("_nativeState", flags)!;
        stateField.SetValue(backend, new HMGamepadState { Axes = axes });
        var apply = type.GetMethod("ApplyState", flags)!;
        foreach (short y in new[] { short.MaxValue, short.MinValue })
        {
            apply.Invoke(backend, new object[] { new GamepadState(
                GamepadButtons.A | GamepadButtons.RightShoulder, DpadState.Down,
                short.MinValue, y, short.MaxValue, y, 255, 0) });
            Assert.Equal(0f, axes[HMAxis.X]);
            Assert.Equal(1f, axes[HMAxis.Rx]);
            float expectedY = y > 0 ? 0f : 1f;
            Assert.Equal(expectedY, axes[HMAxis.Y]);
            Assert.Equal(expectedY, axes[HMAxis.Ry]);
            Assert.Equal(1f, axes[HMAxis.Z]);
            Assert.Equal(0f, axes[HMAxis.Rz]);
        }
        apply.Invoke(backend, new object[] { GamepadState.Neutral with { RightTrigger = 255 } });
        Assert.Equal(0f, axes[HMAxis.Z]);
        Assert.Equal(1f, axes[HMAxis.Rz]);
        apply.Invoke(backend, new object[] { GamepadState.Neutral });
        Assert.All(new[] { HMAxis.X, HMAxis.Y, HMAxis.Rx, HMAxis.Ry },
            axis => Assert.Equal(0.5f, axes[axis]));
        Assert.Equal(0f, axes[HMAxis.Z]);
        Assert.Equal(0f, axes[HMAxis.Rz]);
        var released = (HMGamepadState)stateField.GetValue(backend)!;
        Assert.Equal(HMButton.None, released.Buttons);
        Assert.Equal(HMHat.None, released.Hat);
    }

    [Fact]
    public void NormalizeTrigger_MapsByteRange()
    {
        Assert.Equal(0f, HidMaestroStateMapper.NormalizeTrigger(0));
        Assert.Equal(1f, HidMaestroStateMapper.NormalizeTrigger(255));
    }

    [Fact]
    public void MapButtons_MapsEveryV1XboxControl()
    {
        GamepadButtons input =
            GamepadButtons.A |
            GamepadButtons.B |
            GamepadButtons.X |
            GamepadButtons.Y |
            GamepadButtons.LeftShoulder |
            GamepadButtons.RightShoulder |
            GamepadButtons.Back |
            GamepadButtons.Start |
            GamepadButtons.LeftStick |
            GamepadButtons.RightStick |
            GamepadButtons.Guide;

        HMButton output = HidMaestroStateMapper.MapButtons(input);

        Assert.True(output.HasFlag(HMButton.A));
        Assert.True(output.HasFlag(HMButton.B));
        Assert.True(output.HasFlag(HMButton.X));
        Assert.True(output.HasFlag(HMButton.Y));
        Assert.True(output.HasFlag(HMButton.LeftBumper));
        Assert.True(output.HasFlag(HMButton.RightBumper));
        Assert.True(output.HasFlag(HMButton.Back));
        Assert.True(output.HasFlag(HMButton.Start));
        Assert.True(output.HasFlag(HMButton.LeftStick));
        Assert.True(output.HasFlag(HMButton.RightStick));
        Assert.True(output.HasFlag(HMButton.Guide));
    }

    [Theory]
    [InlineData(DpadState.Neutral, HMHat.None)]
    [InlineData(DpadState.Up | DpadState.Down, HMHat.None)]
    [InlineData(DpadState.Left | DpadState.Right, HMHat.None)]
    [InlineData(DpadState.Up, HMHat.North)]
    [InlineData(DpadState.Up | DpadState.Right, HMHat.NorthEast)]
    [InlineData(DpadState.Right, HMHat.East)]
    [InlineData(DpadState.Down | DpadState.Right, HMHat.SouthEast)]
    [InlineData(DpadState.Down, HMHat.South)]
    [InlineData(DpadState.Down | DpadState.Left, HMHat.SouthWest)]
    [InlineData(DpadState.Left, HMHat.West)]
    [InlineData(DpadState.Up | DpadState.Left, HMHat.NorthWest)]
    public void MapHat_MapsEightWayDpad(DpadState input, HMHat expected)
    {
        Assert.Equal(expected, HidMaestroStateMapper.MapHat(input));
    }
}


public sealed class HidMaestroVirtualGamepadDiagnosticsTests
{
    [Fact]
    public async Task FreshBackendReportsStableIdentityWithoutStartingDriver()
    {
        await using var backend =
            new HidMaestroVirtualGamepadBackend();

        HidMaestroVirtualGamepadDiagnostics diagnostics =
            backend.GetDiagnostics();

        Assert.False(
            diagnostics.IsStarted);
        Assert.Equal(
            HidMaestroVirtualGamepadBackend.ProfileId,
            diagnostics.ProfileId);
        Assert.Equal(
            HidMaestroVirtualGamepadBackend.IdentityKey,
            diagnostics.IdentityKey);
        Assert.Equal(
            0,
            diagnostics.SubmittedStateCount);
        Assert.Equal(
            0,
            diagnostics.RumblePacketCount);
    }
}
