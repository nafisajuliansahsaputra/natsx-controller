namespace Natsx.Controller.Protocol;

public sealed record ProtocolFrame(
    ProtocolVersion Version,
    MessageType MessageType,
    FrameFlags Flags,
    SessionId SessionId,
    uint Sequence,
    ulong MonotonicTimestampMicros,
    byte[] Payload);
