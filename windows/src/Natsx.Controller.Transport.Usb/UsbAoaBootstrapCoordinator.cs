namespace Natsx.Controller.Transport.Usb;

public sealed class UsbAoaBootstrapCoordinator
{
    private readonly IAoaAccessoryDataBackend _accessoryBackend;
    private readonly IUsbAoaBootstrapDeviceProvider _bootstrapProvider;
    private readonly UsbBootstrapPolicy _policy;

    public UsbAoaBootstrapCoordinator(
        IAoaAccessoryDataBackend accessoryBackend,
        IUsbAoaBootstrapDeviceProvider bootstrapProvider,
        UsbBootstrapPolicy? policy = null)
    {
        _accessoryBackend =
            accessoryBackend ??
            throw new ArgumentNullException(nameof(accessoryBackend));
        _bootstrapProvider =
            bootstrapProvider ??
            throw new ArgumentNullException(nameof(bootstrapProvider));
        _policy =
            policy ??
            UsbBootstrapPolicy.Default;

        _policy.Validate();
    }

    public async ValueTask<UsbBootstrapResult> EnsureAccessoryModeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<WinUsbAoaAccessoryDevice> alreadyReady =
            await _accessoryBackend
                .EnumerateAsync(cancellationToken)
                .ConfigureAwait(false);

        if (alreadyReady.Count > 0)
        {
            return new UsbBootstrapResult(
                UsbBootstrapStatus.AccessoryAlreadyReady,
                DeviceId: alreadyReady[0].DeviceId,
                Diagnostic: "Android device is already in AOA data mode.");
        }

        UsbBootstrapResult? providerFailure =
            MapProviderState(_bootstrapProvider.State);

        if (providerFailure is not null)
        {
            return providerFailure;
        }

        IReadOnlyList<IUsbAoaBootstrapDevice> candidates;

        try
        {
            candidates =
                await _bootstrapProvider
                    .EnumerateAsync(cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new UsbBootstrapResult(
                UsbBootstrapStatus.BootstrapRequiresElevation,
                Diagnostic: exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidOperationException)
        {
            return new UsbBootstrapResult(
                UsbBootstrapStatus.BootstrapFailed,
                Diagnostic: exception.Message);
        }

        if (candidates.Count == 0)
        {
            return new UsbBootstrapResult(
                UsbBootstrapStatus.NoDataDeviceDetected,
                Diagnostic:
                    "No USB data device is visible to the bootstrap backend. " +
                    "The cable may be charge-only, USB data may be disabled, or the phone may be disconnected.");
        }

        Exception? lastFailure = null;

        foreach (IUsbAoaBootstrapDevice candidate in candidates)
        {
            await using (candidate.ConfigureAwait(false))
            {
                try
                {
                    ushort protocolVersion =
                        await candidate
                            .StartAccessoryModeAsync(cancellationToken)
                            .ConfigureAwait(false);

                    if (protocolVersion == 0)
                    {
                        throw new NotSupportedException(
                            "Connected Android device does not advertise Android Open Accessory support.");
                    }

                    WinUsbAoaAccessoryDevice? reenumerated =
                        await WaitForAccessoryAsync(
                            cancellationToken)
                            .ConfigureAwait(false);

                    if (reenumerated is null)
                    {
                        return new UsbBootstrapResult(
                            UsbBootstrapStatus.AccessoryReenumerationTimeout,
                            DeviceId: candidate.DeviceId,
                            AoaProtocolVersion: protocolVersion,
                            Diagnostic:
                                "AOA bootstrap completed but the device did not re-enumerate into the NATSX WinUSB accessory interface before timeout.");
                    }

                    return new UsbBootstrapResult(
                        UsbBootstrapStatus.AccessoryStarted,
                        DeviceId: reenumerated.DeviceId,
                        AoaProtocolVersion: protocolVersion,
                        Diagnostic:
                            "AOA bootstrap completed and the device re-enumerated into accessory data mode.");
                }
                catch (NotSupportedException exception)
                {
                    lastFailure = exception;
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException)
                {
                    lastFailure = exception;
                }
            }
        }

        if (lastFailure is NotSupportedException)
        {
            return new UsbBootstrapResult(
                UsbBootstrapStatus.AoaUnsupported,
                Diagnostic: lastFailure.Message);
        }

        if (lastFailure is UnauthorizedAccessException)
        {
            return new UsbBootstrapResult(
                UsbBootstrapStatus.BootstrapRequiresElevation,
                Diagnostic: lastFailure.Message);
        }

        return new UsbBootstrapResult(
            UsbBootstrapStatus.BootstrapFailed,
            Diagnostic:
                lastFailure?.Message ??
                "No bootstrap candidate could enter Android Open Accessory mode.");
    }

    private async ValueTask<WinUsbAoaAccessoryDevice?> WaitForAccessoryAsync(
        CancellationToken cancellationToken)
    {
        TimeSpan elapsed = TimeSpan.Zero;

        while (elapsed <= _policy.ReenumerationTimeout)
        {
            IReadOnlyList<WinUsbAoaAccessoryDevice> devices =
                await _accessoryBackend
                    .EnumerateAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (devices.Count > 0)
            {
                return devices[0];
            }

            if (elapsed == _policy.ReenumerationTimeout)
            {
                break;
            }

            TimeSpan remaining =
                _policy.ReenumerationTimeout - elapsed;

            TimeSpan delay =
                remaining < _policy.PollInterval
                    ? remaining
                    : _policy.PollInterval;

            await Task.Delay(
                delay,
                cancellationToken)
                .ConfigureAwait(false);

            elapsed += delay;
        }

        return null;
    }

    private static UsbBootstrapResult? MapProviderState(
        UsbBootstrapProviderState state) =>
        state switch
        {
            UsbBootstrapProviderState.Ready => null,
            UsbBootstrapProviderState.DriverMissing =>
                new UsbBootstrapResult(
                    UsbBootstrapStatus.BootstrapDriverMissing,
                    Diagnostic:
                        "The NATSX AOA bootstrap driver is not installed."),
            UsbBootstrapProviderState.RequiresElevation =>
                new UsbBootstrapResult(
                    UsbBootstrapStatus.BootstrapRequiresElevation,
                    Diagnostic:
                        "The USB bootstrap backend requires elevated setup before realtime use."),
            _ =>
                new UsbBootstrapResult(
                    UsbBootstrapStatus.BootstrapBackendUnavailable,
                    Diagnostic:
                        "No safe pre-AOA Windows bootstrap backend is currently available."),
        };
}
