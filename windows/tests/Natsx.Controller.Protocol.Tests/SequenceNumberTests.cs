using Natsx.Controller.Core;

namespace Natsx.Controller.Protocol.Tests;

public sealed class SequenceNumberTests
{
    [Fact]
    public void IsNewer_IncrementsNormally()
    {
        Assert.True(SequenceNumber.IsNewer(11, 10));
        Assert.False(SequenceNumber.IsNewer(10, 10));
        Assert.False(SequenceNumber.IsNewer(9, 10));
    }

    [Fact]
    public void IsNewer_HandlesWrapAround()
    {
        Assert.True(SequenceNumber.IsNewer(0, uint.MaxValue));
        Assert.True(SequenceNumber.IsNewer(1, uint.MaxValue));
        Assert.False(SequenceNumber.IsNewer(uint.MaxValue, 0));
    }

    [Fact]
    public void IsNewer_RejectsHalfRangeAmbiguity()
    {
        Assert.False(SequenceNumber.IsNewer(0x80000000u, 0));
    }
}
