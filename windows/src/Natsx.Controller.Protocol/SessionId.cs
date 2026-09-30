using System.Buffers.Binary;

namespace Natsx.Controller.Protocol;

public readonly record struct SessionId(ulong Part0, ulong Part1)
{
    public const int Size = 16;

    public static SessionId Zero => default;

    public static SessionId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Size)
        {
            throw new ArgumentException("Session ID must be exactly 16 bytes.", nameof(bytes));
        }

        return new SessionId(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
    }

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException("Destination must provide at least 16 bytes.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt64LittleEndian(destination[..8], Part0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..Size], Part1);
    }
}
