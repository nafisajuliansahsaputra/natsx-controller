using System.Buffers.Binary;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class UsbAoaBootstrapCoordinatorTests
{
    [Fact]
    public async Task AlreadyAccessoryMode_ReturnsReadyWithoutBootstrap()
    {
        var accessory = new FakeAccessoryBackend
        {
            IsReady = true,
        };

        var provider =
            new FakeBootstrapProvider(
                UsbBootstrapProviderState.Ready);

        var coordinator =
            new UsbAoaBootstrapCoordinator(
                accessory,
                provider);

        UsbBootstrapResult result =
            await coordinator.EnsureAccessoryModeAsync();

        Assert.Equal(
            UsbBootstrapStatus.AccessoryAlreadyReady,
            result.Status);
        Assert.True(result.IsReady);
        Assert.Equal(0, provider.EnumerateCount);
    }

    [Fact]
    public async Task MissingBootstrapDriver_IsReportedExplicitly()
    {
        var coordinator =
            new UsbAoaBootstrapCoordinator(
                new FakeAccessoryBackend(),
                new UnavailableUsbAoaBootstrapDeviceProvider(
                    UsbBootstrapProviderState.DriverMissing));

        UsbBootstrapResult result =
            await coordinator.EnsureAccessoryModeAsync();

        Assert.Equal(
            UsbBootstrapStatus.BootstrapDriverMissing,
            result.Status);
        Assert.False(result.IsReady);
    }

    [Fact]
    public async Task ReadyProviderWithNoDevice_ReportsNoDataPath()
    {
        var coordinator =
            new UsbAoaBootstrapCoordinator(
                new FakeAccessoryBackend(),
                new FakeBootstrapProvider(
                    UsbBootstrapProviderState.Ready));

        UsbBootstrapResult result =
            await coordinator.EnsureAccessoryModeAsync();

        Assert.Equal(
            UsbBootstrapStatus.NoDataDeviceDetected,
            result.Status);
        Assert.Contains(
            "charge-only",
            result.Diagnostic,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedAoaDevice_IsNotReportedAsCableFailure()
    {
        var device =
            new FakeBootstrapDevice(
                protocolVersion: 0);

        var provider =
            new FakeBootstrapProvider(
                UsbBootstrapProviderState.Ready,
                device);

        var coordinator =
            new UsbAoaBootstrapCoordinator(
                new FakeAccessoryBackend(),
                provider);

        UsbBootstrapResult result =
            await coordinator.EnsureAccessoryModeAsync();

        Assert.Equal(
            UsbBootstrapStatus.AoaUnsupported,
            result.Status);
        Assert.False(result.IsReady);
    }

    [Fact]
    public async Task StartAccessory_WaitsForAoaReenumeration()
    {
        var accessory =
            new FakeAccessoryBackend();

        var device =
            new FakeBootstrapDevice(
                protocolVersion: 2,
                onStartAccessory: () =>
                    accessory.IsReady = true);

        var provider =
            new FakeBootstrapProvider(
                UsbBootstrapProviderState.Ready,
                device);

        var coordinator =
            new UsbAoaBootstrapCoordinator(
                accessory,
                provider,
                policy:
                    new UsbBootstrapPolicy(
                        TimeSpan.FromMilliseconds(20),
                        TimeSpan.FromMilliseconds(1)));

        UsbBootstrapResult result =
            await coordinator.EnsureAccessoryModeAsync();

        Assert.Equal(
            UsbBootstrapStatus.AccessoryStarted,
            result.Status);
        Assert.True(result.IsReady);
        Assert.Equal((ushort)2, result.AoaProtocolVersion);
        Assert.Equal(1, device.StartAccessoryCount);
    }

    [Fact]
    public async Task StartAccessoryWithoutReenumeration_TimesOut()
    {
        var device =
            new FakeBootstrapDevice(
                protocolVersion: 2);

        var coordinator =
            new UsbAoaBootstrapCoordinator(
                new FakeAccessoryBackend(),
                new FakeBootstrapProvider(
                    UsbBootstrapProviderState.Ready,
                    device),
                policy:
                    new UsbBootstrapPolicy(
                        TimeSpan.FromMilliseconds(5),
                        TimeSpan.FromMilliseconds(1)));

        UsbBootstrapResult result =
            await coordinator.EnsureAccessoryModeAsync();

        Assert.Equal(
            UsbBootstrapStatus.AccessoryReenumerationTimeout,
            result.Status);
        Assert.False(result.IsReady);
    }

    private sealed class FakeAccessoryBackend :
        IAoaAccessoryDataBackend
    {
        public bool IsReady { get; set; }

        public ValueTask<IReadOnlyList<WinUsbAoaAccessoryDevice>>
            EnumerateAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<WinUsbAoaAccessoryDevice> result =
                IsReady
                    ? new[]
                    {
                        new WinUsbAoaAccessoryDevice(
                            "aoa-device",
                            "NATSX AOA",
                            AndroidOpenAccessoryConstants.GoogleVendorId,
                            AndroidOpenAccessoryConstants.AccessoryProductId),
                    }
                    : Array.Empty<WinUsbAoaAccessoryDevice>();

            return ValueTask.FromResult(result);
        }

        public ValueTask<WinUsbAoaAccessoryConnection?> OpenFirstAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<WinUsbAoaAccessoryConnection?>(null);
        }
    }

    private sealed class FakeBootstrapProvider :
        IUsbAoaBootstrapDeviceProvider
    {
        private readonly IReadOnlyList<IUsbAoaBootstrapDevice> _devices;

        public FakeBootstrapProvider(
            UsbBootstrapProviderState state,
            params IUsbAoaBootstrapDevice[] devices)
        {
            State = state;
            _devices = devices;
        }

        public UsbBootstrapProviderState State { get; }

        public int EnumerateCount { get; private set; }

        public ValueTask<IReadOnlyList<IUsbAoaBootstrapDevice>>
            EnumerateAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnumerateCount++;
            return ValueTask.FromResult(_devices);
        }
    }

    private sealed class FakeBootstrapDevice :
        IUsbAoaBootstrapDevice
    {
        private readonly ushort _protocolVersion;
        private readonly Action? _onStartAccessory;

        public FakeBootstrapDevice(
            ushort protocolVersion,
            Action? onStartAccessory = null)
        {
            _protocolVersion = protocolVersion;
            _onStartAccessory = onStartAccessory;
        }

        public string DeviceId => "bootstrap-device";

        public ushort VendorId => 0x1234;

        public ushort ProductId => 0x5678;

        public int StartAccessoryCount { get; private set; }

        public ValueTask<int> ControlInAsync(
            byte requestType,
            byte request,
            ushort value,
            ushort index,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Assert.Equal(
                AndroidOpenAccessoryConstants.GetProtocolRequest,
                request);

            BinaryPrimitives.WriteUInt16LittleEndian(
                buffer.Span,
                _protocolVersion);

            return ValueTask.FromResult(2);
        }

        public ValueTask ControlOutAsync(
            byte requestType,
            byte request,
            ushort value,
            ushort index,
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request ==
                AndroidOpenAccessoryConstants.StartAccessoryRequest)
            {
                StartAccessoryCount++;
                _onStartAccessory?.Invoke();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
