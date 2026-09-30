using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public readonly record struct PeerId(ulong Part0, ulong Part1)
{
    public const int Size = 16;

    public static PeerId Zero => default;

    public static PeerId CreateRandom()
    {
        Span<byte> bytes = stackalloc byte[Size];
        RandomNumberGenerator.Fill(bytes);
        return FromBytes(bytes);
    }

    public static PeerId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
        {
            throw new ArgumentException(
                "Peer ID must be exactly 16 bytes.",
                nameof(bytes));
        }

        return new PeerId(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
    }

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException(
                "Destination must provide at least 16 bytes.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination[..8], Part0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..Size], Part1);
    }

    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[Size];
        WriteBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
