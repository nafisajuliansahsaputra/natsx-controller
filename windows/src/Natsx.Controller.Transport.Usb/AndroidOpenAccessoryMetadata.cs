namespace Natsx.Controller.Transport.Usb;

public sealed record AndroidOpenAccessoryMetadata(
    string Manufacturer,
    string Model,
    string Description,
    string Version,
    string Uri,
    string Serial)
{
    public static AndroidOpenAccessoryMetadata NatsxDefault { get; } =
        new(
            "NATSX",
            "NATSX Controller Windows Receiver",
            "Low-latency NATSX Controller USB transport",
            "1",
            "https://natsx.my.id",
            "natsx-controller");
}
