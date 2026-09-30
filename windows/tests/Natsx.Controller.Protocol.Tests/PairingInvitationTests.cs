namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingInvitationTests
{
    [Fact]
    public void CanonicalInvitation_RoundTripsWithoutLeakingSecretInToString()
    {
        PeerId peer = PeerId.FromBytes(
            Enumerable.Range(0, 16).Select(static value => (byte)value).ToArray());

        byte[] secret = Enumerable.Range(0x20, 32)
            .Select(static value => (byte)value)
            .ToArray();

        using var invitation = new PairingInvitation(
            peer,
            secret,
            1_790_800_000);

        string encoded = invitation.ToUriString();

        Assert.Equal(
            "natsx://pair/v1?peer=000102030405060708090a0b0c0d0e0f" +
            "&secret=ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8" +
            "&expires=1790800000",
            encoded);

        using PairingInvitation parsed = PairingInvitation.Parse(encoded);

        Assert.Equal(peer, parsed.ReceiverPeerId);
        Assert.Equal(secret, parsed.CopySecret());
        Assert.Equal(1_790_800_000, parsed.ExpiresUnixSeconds);
        Assert.Contains("[redacted]", parsed.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8",
            parsed.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invitation_RejectsWrongScheme()
    {
        Assert.Throws<FormatException>(() =>
            PairingInvitation.Parse(
                "https://pair/v1?peer=000102030405060708090a0b0c0d0e0f" +
                "&secret=ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8" +
                "&expires=1790800000"));
    }
}
