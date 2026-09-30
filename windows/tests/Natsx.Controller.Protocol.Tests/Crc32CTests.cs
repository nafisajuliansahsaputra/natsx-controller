using System.Text;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class Crc32CTests
{
    [Fact]
    public void Compute_StandardVector_MatchesCastagnoli()
    {
        uint actual = Crc32C.Compute(Encoding.ASCII.GetBytes("123456789"));

        Assert.Equal(0xE3069283u, actual);
    }
}
