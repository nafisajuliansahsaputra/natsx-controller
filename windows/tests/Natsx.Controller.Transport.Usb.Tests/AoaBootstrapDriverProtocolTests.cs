using System.Buffers.Binary;

namespace Natsx.Controller.Transport.Usb.Tests;

public sealed class AoaBootstrapDriverProtocolTests
{
    [Fact]
    public void ManagedProtocolVersion_MatchesKernelContract()
    {
        Assert.Equal(
            1u,
            AoaBootstrapDriverProtocol.ProtocolVersion);
        Assert.Equal(
            8u,
            AoaBootstrapDriverProtocol.MinimumSupportedDriverBuild);
        Assert.Equal(
            "NatsxAoaBootstrap",
            AoaBootstrapDriverProtocol.ServiceName);
        Assert.Equal(
            @"\\.\NatsxAoaBootstrap",
            AoaBootstrapDriverProtocol.ControlDevicePath);
    }

    [Fact]
    public void Compatibility_RejectsUnknownProtocolVersion()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                AoaBootstrapDriverProtocol.ValidateCompatibility(
                    new AoaBootstrapDriverVersion(2, 8)));
    }

    [Fact]
    public void Compatibility_RejectsObsoleteDriverBuild()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                AoaBootstrapDriverProtocol.ValidateCompatibility(
                    new AoaBootstrapDriverVersion(1, 1)));
    }

    [Fact]
    public void Compatibility_AcceptsCurrentDriver()
    {
        AoaBootstrapDriverProtocol.ValidateCompatibility(
            new AoaBootstrapDriverVersion(1, 8));
    }

    [Fact]
    public void ControlCodes_MatchKernelCtlCodeLayout()
    {
        Assert.Equal(
            0xA3616000u,
            AoaBootstrapDriverProtocol.GetVersionControlCode);

        Assert.Equal(
            0xA361E004u,
            AoaBootstrapDriverProtocol.StartAoaControlCode);

        Assert.Equal(
            0xA3616008u,
            AoaBootstrapDriverProtocol.GetStatusControlCode);

        Assert.Equal(
            0xA361600Cu,
            AoaBootstrapDriverProtocol.ProbeProtocolRawControlCode);
    }

    [Fact]
    public void VersionResponse_ParsesLittleEndianContract()
    {
        var bytes = new byte[8];

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            1);

        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            8);

        Assert.Equal(
            new AoaBootstrapDriverVersion(1, 8),
            AoaBootstrapDriverProtocol.ParseVersion(bytes));
    }

    [Fact]
    public void StatusResponse_ParsesTargetReadiness()
    {
        var bytes = new byte[24];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), unchecked((int)0xC0000010));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 1);

        Assert.Equal(
            new AoaBootstrapDriverStatus(
                1,
                5,
                1,
                0,
                unchecked((int)0xC0000010),
                1),
            AoaBootstrapDriverProtocol.ParseStatus(bytes));
    }

    [Fact]
    public void RawProtocolProbe_ParsesReadOnlyKernelDiagnostic()
    {
        var bytes = new byte[24];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 0);

        Assert.Equal(
            new AoaBootstrapRawProtocolProbe(
                1,
                8,
                0,
                0,
                2,
                2,
                0),
            AoaBootstrapDriverProtocol.ParseRawProtocolProbe(bytes));
    }

    [Fact]
    public void StartResponse_RejectsNonZeroReservedField()
    {
        byte[] bytes =
        {
            2, 0,
            1, 0,
        };

        Assert.Throws<FormatException>(
            () =>
                AoaBootstrapDriverProtocol
                    .ParseAoaProtocolVersion(bytes));
    }
}
