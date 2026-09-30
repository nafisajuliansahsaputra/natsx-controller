namespace Natsx.Controller.Transport.Bluetooth;

public static class BluetoothRfcommConstants
{
    public static Guid ServiceUuid { get; } =
        new("6f0f4f92-8d9d-4f7b-9bf0-1a6c4f6e4e36");

    public const byte ProtocolTransportValue = 3;
}
