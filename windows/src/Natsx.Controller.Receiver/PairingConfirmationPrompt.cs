using Natsx.Controller.Protocol;

namespace Natsx.Controller.Receiver;

public sealed record PairingConfirmationPrompt(
    string ComparisonCode,
    PeerId RemotePeerId);
