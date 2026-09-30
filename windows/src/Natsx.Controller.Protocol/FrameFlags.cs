namespace Natsx.Controller.Protocol;

[Flags]
public enum FrameFlags : byte
{
    None = 0,
    Authenticated = 1 << 0,
}
