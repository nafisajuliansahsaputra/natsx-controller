namespace Natsx.Controller.Protocol;

public readonly record struct ProtocolVersion(byte Major, byte Minor)
{
    public static ProtocolVersion Current => new(1, 0);

    public override string ToString() => $"{Major}.{Minor}";
}
