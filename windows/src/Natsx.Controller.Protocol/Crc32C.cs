namespace Natsx.Controller.Protocol;

public static class Crc32C
{
    private const uint Polynomial = 0x82F63B78u;

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;

        foreach (byte value in data)
        {
            crc ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = unchecked((uint)-(int)(crc & 1u));
                crc = (crc >> 1) ^ (Polynomial & mask);
            }
        }

        return ~crc;
    }
}
