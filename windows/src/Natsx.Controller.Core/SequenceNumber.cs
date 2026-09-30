namespace Natsx.Controller.Core;

public static class SequenceNumber
{
    /// <summary>
    /// Compares uint sequence numbers using half-range serial number arithmetic.
    /// A candidate is newer when it is ahead of the reference by less than 2^31.
    /// </summary>
    public static bool IsNewer(uint candidate, uint reference)
    {
        return candidate != reference && unchecked((int)(candidate - reference)) > 0;
    }
}
