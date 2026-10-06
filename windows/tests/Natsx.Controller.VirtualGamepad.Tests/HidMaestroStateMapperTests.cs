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
