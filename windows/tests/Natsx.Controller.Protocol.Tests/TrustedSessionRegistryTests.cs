using System.Security.Cryptography;

namespace Natsx.Controller.Protocol.Tests;

public sealed class TrustedSessionRegistryTests
{
    [Fact]
    public void ReplaceAndGet_UseDefensiveKeyCopies()
    {
        using var registry =
            new TrustedSessionRegistry();

        PeerId peerId =
            PeerId.CreateRandom();
        SessionId sessionId =
            SessionId.CreateRandom();

        byte[] original =
            Enumerable.Range(0, 32)
                .Select(static value => (byte)value)
                .ToArray();

        byte[] expected =
            original.ToArray();

        registry.Replace(
            peerId,
            sessionId,
            original);

        CryptographicOperations.ZeroMemory(
            original);

        using TrustedSessionMaterial stored =
            Assert.IsType<TrustedSessionMaterial>(
                registry.Get(peerId));

        Assert.Equal(
            sessionId,
            stored.SessionId);

        byte[] firstCopy =
            stored.CopySessionKey();

        Assert.Equal(
            expected,
            firstCopy);

        firstCopy[0] ^= 0xFF;

        using TrustedSessionMaterial reread =
            Assert.IsType<TrustedSessionMaterial>(
                registry.Get(peerId));

        byte[] secondCopy =
            reread.CopySessionKey();

        Assert.Equal(
            expected,
            secondCopy);

        CryptographicOperations.ZeroMemory(
            expected);
        CryptographicOperations.ZeroMemory(
            firstCopy);
        CryptographicOperations.ZeroMemory(
            secondCopy);
    }

    [Fact]
    public void GetBySessionId_ReturnsPeerAndIndependentMaterial()
    {
        using var registry =
            new TrustedSessionRegistry();

        PeerId firstPeer =
            PeerId.CreateRandom();
        PeerId secondPeer =
            PeerId.CreateRandom();
        SessionId firstSession =
            SessionId.CreateRandom();
        SessionId secondSession =
            SessionId.CreateRandom();

        byte[] firstKey =
            RandomNumberGenerator.GetBytes(32);
        byte[] secondKey =
            RandomNumberGenerator.GetBytes(32);

        registry.Replace(
            firstPeer,
            firstSession,
            firstKey);
        registry.Replace(
            secondPeer,
            secondSession,
            secondKey);

        using TrustedSessionRegistration registration =
            Assert.IsType<TrustedSessionRegistration>(
                registry.GetBySessionId(secondSession));

        Assert.Equal(
            secondPeer,
            registration.PeerId);
        Assert.Equal(
            secondSession,
            registration.Material.SessionId);

        Assert.Null(
            registry.GetBySessionId(
                SessionId.CreateRandom()));

        CryptographicOperations.ZeroMemory(
            firstKey);
        CryptographicOperations.ZeroMemory(
            secondKey);
    }

    [Fact]
    public void ReplaceAndRemove_ControlCurrentSession()
    {
        using var registry =
            new TrustedSessionRegistry();

        PeerId peerId =
            PeerId.CreateRandom();

        byte[] firstKey =
            RandomNumberGenerator.GetBytes(32);
        byte[] secondKey =
            RandomNumberGenerator.GetBytes(32);

        SessionId firstSession =
            SessionId.CreateRandom();
        SessionId secondSession =
            SessionId.CreateRandom();

        registry.Replace(
            peerId,
            firstSession,
            firstKey);

        using TrustedSessionMaterial first =
            Assert.IsType<TrustedSessionMaterial>(
                registry.Get(peerId));

        registry.Replace(
            peerId,
            secondSession,
            secondKey);

        using TrustedSessionMaterial current =
            Assert.IsType<TrustedSessionMaterial>(
                registry.Get(peerId));

        Assert.Equal(
            firstSession,
            first.SessionId);
        Assert.Equal(
            secondSession,
            current.SessionId);

        registry.Remove(peerId);

        Assert.Null(
            registry.Get(peerId));

        CryptographicOperations.ZeroMemory(
            firstKey);
        CryptographicOperations.ZeroMemory(
            secondKey);
    }
}
