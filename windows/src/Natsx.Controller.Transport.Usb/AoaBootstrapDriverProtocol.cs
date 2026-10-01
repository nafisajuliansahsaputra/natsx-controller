using System.Buffers.Binary;

namespace Natsx.Controller.Transport.Usb;

public static class AoaBootstrapDriverProtocol
{
    public const uint ProtocolVersion = 1;
    public const uint MinimumSupportedDriverBuild = 2;

    public static readonly Guid DeviceInterfaceGuid =
        new("54E7A3A1-01F0-41B8-B397-75E2A6D42C11");

    public const ushort DeviceType = 0xA361;
    public const ushort GetVersionFunction = 0x800;
    public const ushort StartAoaFunction = 0x801;

    public const int VersionResponseSize = 8;
    public const int StartResponseSize = 4;

    public static uint GetVersionControlCode =>
        BuildControlCode(
            GetVersionFunction,
            access: 1);

    public static uint StartAoaControlCode =>
        BuildControlCode(
            StartAoaFunction,
            access: 3);

    public static void ValidateCompatibility(
        AoaBootstrapDriverVersion version)
    {
        if (version.ProtocolVersion != ProtocolVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported NATSX AOA bootstrap driver protocol version {version.ProtocolVersion}.");
        }

        if (version.DriverBuild < MinimumSupportedDriverBuild)
        {
            throw new InvalidOperationException(
                $"NATSX AOA bootstrap driver build {version.DriverBuild} is older than the minimum supported build {MinimumSupportedDriverBuild}.");
        }
    }

    public static AoaBootstrapDriverVersion ParseVersion(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < VersionResponseSize)
        {
            throw new FormatException(
                "AOA bootstrap driver version response is truncated.");
        }

        return new AoaBootstrapDriverVersion(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]));
    }

    public static ushort ParseAoaProtocolVersion(
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < StartResponseSize)
        {
            throw new FormatException(
                "AOA bootstrap start response is truncated.");
        }

        ushort version =
            BinaryPrimitives.ReadUInt16LittleEndian(bytes);

        ushort reserved =
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]);

        if (reserved != 0)
        {
            throw new FormatException(
                "AOA bootstrap start response reserved field must be zero.");
        }

        return version;
    }

    private static uint BuildControlCode(
        ushort function,
        uint access)
    {
        const uint methodBuffered = 0;

        return
            ((uint)DeviceType << 16) |
            (access << 14) |
            ((uint)function << 2) |
            methodBuffered;
    }
}

public readonly record struct AoaBootstrapDriverVersion(
    uint ProtocolVersion,
    uint DriverBuild);
