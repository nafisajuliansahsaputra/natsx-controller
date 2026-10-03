using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Usb;

/// <summary>Finds session setup when opening an already-streaming USB uplink.</summary>
public static class UsbInitialSessionReader
{
    public static async ValueTask<byte[]> ReadAsync(Stream input, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] frame = await UsbStreamFrameCodec.ReadFrameAsync(input, cancellationToken).ConfigureAwait(false);
            if (frame.Length < ProtocolConstants.HeaderSize)
            {
                throw new FormatException("USB session frame is shorter than the protocol header.");
            }

            MessageType type = (MessageType)frame[6];
            switch (type)
            {
                case MessageType.PairingOffer:
                case MessageType.AuthChallenge:
                case MessageType.SessionReady:
                    // Authentication and trust validation are performed by
                    // the existing handshake/join server before attachment.
                    return frame;
                case MessageType.GamepadState:
                case MessageType.TransportPreference:
                case MessageType.HeartbeatAck:
                    // Never grant authority to pre-authentication input.
                    continue;
                default:
                    throw new FormatException($"Unsupported initial USB control message: {type}.");
            }
        }
    }
}
