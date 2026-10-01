namespace Natsx.Controller.Transport.Usb;

public sealed record UsbBootstrapPolicy(
    TimeSpan ReenumerationTimeout,
    TimeSpan PollInterval)
{
    public static UsbBootstrapPolicy Default { get; } =
        new(
            ReenumerationTimeout: TimeSpan.FromSeconds(5),
            PollInterval: TimeSpan.FromMilliseconds(100));

    public void Validate()
    {
        if (ReenumerationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReenumerationTimeout));
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PollInterval));
        }

        if (PollInterval > ReenumerationTimeout)
        {
            throw new ArgumentException(
                "USB bootstrap poll interval cannot exceed the re-enumeration timeout.");
        }
    }
}
