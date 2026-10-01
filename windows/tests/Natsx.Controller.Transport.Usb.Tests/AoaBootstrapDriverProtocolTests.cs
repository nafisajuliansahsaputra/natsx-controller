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
            2u,
            AoaBootstrapDriverProtocol.MinimumSupportedDriverBuild);
        Assert.Equal(
            "NatsxAoaBootstrap",
            AoaBootstrapDriverProtocol.ServiceName);
    }

    [Fact]
    public void Compatibility_RejectsUnknownProtocolVersion()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                AoaBootstrapDriverProtocol.ValidateCompatibility(
                    new AoaBootstrapDriverVersion(2, 2)));
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
            new AoaBootstrapDriverVersion(1, 2));
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
            2);

        Assert.Equal(
            new AoaBootstrapDriverVersion(1, 2),
            AoaBootstrapDriverProtocol.ParseVersion(bytes));
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
