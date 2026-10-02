using HIDMaestro;
using Natsx.Controller.Core;

namespace Natsx.Controller.VirtualGamepad;

public readonly record struct HidMaestroVirtualGamepadDiagnostics(
    bool IsStarted,
    string ProfileId,
    string IdentityKey,
    long SubmittedStateCount,
    long RumblePacketCount);

public sealed class HidMaestroVirtualGamepadBackend : IVirtualGamepadBackend
{
    public const string ProfileId = "xbox-360-wired";
    public const string IdentityKey = "natsx-controller-primary";

    private HMContext? _context;
    private HMController? _controller;
    private HMGamepadState _nativeState;
    private HMAxis _leftX = HMAxis.None;
    private HMAxis _leftY = HMAxis.None;
    private HMAxis _rightX = HMAxis.None;
    private HMAxis _rightY = HMAxis.None;
    private HMAxis _leftTrigger = HMAxis.None;
    private HMAxis _rightTrigger = HMAxis.None;
    private long _submittedStateCount;
    private long _rumblePacketCount;

    public bool IsStarted => _controller is not null;

    public event Action<RumbleState>? RumbleReceived;

    public HidMaestroVirtualGamepadDiagnostics GetDiagnostics() =>
        new(
            IsStarted,
            ProfileId,
            IdentityKey,
            Interlocked.Read(
                ref _submittedStateCount),
            Interlocked.Read(
                ref _rumblePacketCount));

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_controller is not null)
        {
            return ValueTask.CompletedTask;
        }

        var context = new HMContext();
        HMController? controller = null;

        try
        {
            // HIDMaestro's driver installation is idempotent. The first
            // installation requires elevation; subsequent calls are a cheap
            // compatibility/version check and keep controller creation behind
            // one stable backend boundary.
            context.InstallDriver();
            context.LoadDefaultProfiles();

            HMProfile profile = context.GetProfile(ProfileId)
                ?? throw new InvalidOperationException(
                    $"HIDMaestro profile '{ProfileId}' is unavailable.");

            ConfigureAxes(profile);

            _nativeState = new HMGamepadState
            {
                Axes = CreateAxesDictionary(),
                Buttons = HMButton.None,
                Hat = HMHat.None,
            };

            controller = context.CreateController(profile, IdentityKey);
            controller.OutputReceived += OnOutputReceived;

            _context = context;
            _controller = controller;

            Submit(GamepadState.Neutral);
            return ValueTask.CompletedTask;
        }
        catch
        {
            if (controller is not null)
            {
                controller.OutputReceived -= OnOutputReceived;
                controller.Dispose();
            }

            context.Dispose();
            ResetNativeState();
            throw;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        HMController? controller = _controller;
        HMContext? context = _context;

        _controller = null;
        _context = null;

        if (controller is not null)
        {
            try
            {
                SubmitNeutralBeforeDispose(controller);
            }
            finally
            {
                controller.OutputReceived -= OnOutputReceived;
                controller.Dispose();
            }
        }

        context?.Dispose();
        ResetNativeState();

        return ValueTask.CompletedTask;
    }

    public void Submit(GamepadState state)
    {
        HMController controller = _controller
            ?? throw new InvalidOperationException(
                "HIDMaestro virtual controller has not been started.");

        ApplyState(state);
        controller.SubmitState(in _nativeState);

        Interlocked.Increment(
            ref _submittedStateCount);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    private void ConfigureAxes(HMProfile profile)
    {
        if (profile.Sticks.Count < 2)
        {
            throw new InvalidOperationException(
                $"HIDMaestro profile '{ProfileId}' does not expose two sticks.");
        }

        if (profile.Triggers.Count < 2)
        {
            throw new InvalidOperationException(
                $"HIDMaestro profile '{ProfileId}' does not expose two triggers.");
        }

        _leftX = profile.Sticks[0].XAxis;
        _leftY = profile.Sticks[0].YAxis;
        _rightX = profile.Sticks[1].XAxis;
        _rightY = profile.Sticks[1].YAxis;
        _leftTrigger = profile.Triggers[0].Axis;
        _rightTrigger = profile.Triggers[1].Axis;

        if (_leftX == HMAxis.None ||
            _leftY == HMAxis.None ||
            _rightX == HMAxis.None ||
            _rightY == HMAxis.None ||
            _leftTrigger == HMAxis.None ||
            _rightTrigger == HMAxis.None)
        {
            throw new InvalidOperationException(
                $"HIDMaestro profile '{ProfileId}' exposes an incomplete axis layout.");
        }
    }

    private Dictionary<HMAxis, float> CreateAxesDictionary()
    {
        return new Dictionary<HMAxis, float>(6)
        {
            [_leftX] = 0.5f,
            [_leftY] = 0.5f,
            [_rightX] = 0.5f,
            [_rightY] = 0.5f,
            [_leftTrigger] = 0f,
            [_rightTrigger] = 0f,
        };
    }

    private void ApplyState(GamepadState state)
    {
        Dictionary<HMAxis, float> axes = _nativeState.Axes
            ?? throw new InvalidOperationException("HIDMaestro axis buffer is unavailable.");

        axes[_leftX] = HidMaestroStateMapper.NormalizeStick(state.LeftX);
        axes[_leftY] = HidMaestroStateMapper.NormalizeStick(state.LeftY);
        axes[_rightX] = HidMaestroStateMapper.NormalizeStick(state.RightX);
        axes[_rightY] = HidMaestroStateMapper.NormalizeStick(state.RightY);
        axes[_leftTrigger] = HidMaestroStateMapper.NormalizeTrigger(state.LeftTrigger);
        axes[_rightTrigger] = HidMaestroStateMapper.NormalizeTrigger(state.RightTrigger);

        _nativeState.Buttons = HidMaestroStateMapper.MapButtons(state.Buttons);
        _nativeState.Hat = HidMaestroStateMapper.MapHat(state.Dpad);
        _nativeState.HatDegrees = null;
        _nativeState.HatHundredths = null;
        _nativeState.HatRaw = null;
    }

    private void SubmitNeutralBeforeDispose(HMController controller)
    {
        if (_nativeState.Axes is null)
        {
            return;
        }

        ApplyState(GamepadState.Neutral);
        controller.SubmitState(in _nativeState);
    }

    private void OnOutputReceived(HMController controller, HMOutputPacket packet)
    {
        if (packet.Source != HMOutputSource.XInput)
        {
            return;
        }

        ReadOnlySpan<byte> data = packet.Data.Span;

        if (data.Length < 5)
        {
            return;
        }

        Interlocked.Increment(
            ref _rumblePacketCount);

        RumbleReceived?.Invoke(new RumbleState(
            LowFrequencyMotor: data[2],
            HighFrequencyMotor: data[3]));
    }

    private void ResetNativeState()
    {
        _nativeState = default;
        _leftX = HMAxis.None;
        _leftY = HMAxis.None;
        _rightX = HMAxis.None;
        _rightY = HMAxis.None;
        _leftTrigger = HMAxis.None;
        _rightTrigger = HMAxis.None;
    }
}
