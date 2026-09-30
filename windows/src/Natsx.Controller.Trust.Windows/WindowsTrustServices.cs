using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows;

public sealed class WindowsTrustServices
{
    private WindowsTrustServices(
        PeerId localPeerId,
        ITrustedPeerStore trustedPeers)
    {
        LocalPeerId = localPeerId;
        TrustedPeers = trustedPeers;
    }

    public PeerId LocalPeerId { get; }

    public ITrustedPeerStore TrustedPeers { get; }

    public static WindowsTrustServices CreateDefault()
    {
        var identityStore =
            new LocalPeerIdentityStore(
                TrustStoragePaths.LocalPeerIdentity);

        var trustedPeerStore =
            new FileTrustedPeerStore(
                TrustStoragePaths.TrustedPeers,
                new DpapiTrustSecretProtector());

        return new WindowsTrustServices(
            identityStore.GetOrCreate(),
            trustedPeerStore);
    }
}
