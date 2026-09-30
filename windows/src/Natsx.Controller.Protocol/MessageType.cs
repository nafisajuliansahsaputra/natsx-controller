namespace Natsx.Controller.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    AuthChallenge = 2,
    AuthResponse = 3,
    SessionReady = 4,
    Heartbeat = 5,
    HeartbeatAck = 6,
    GamepadState = 7,
    TransportReady = 8,
    HandoverPrepare = 9,
    HandoverCommit = 10,
    Rumble = 11,
    Disconnect = 12,
    PairRequest = 13,
    PairResponse = 14,
}
