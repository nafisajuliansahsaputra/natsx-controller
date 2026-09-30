namespace Natsx.Controller.Protocol;

public static class ProtocolConstants
{
    public const int HeaderSize = 40;
    public const int CrcSize = 4;
    public const int AuthenticationTagSize = 16;
    public const int GamepadStatePayloadSize = 16;
    public const int MaximumPayloadSize = 4096;

    public static ReadOnlySpan<byte> Magic => "NXC1"u8;
}
