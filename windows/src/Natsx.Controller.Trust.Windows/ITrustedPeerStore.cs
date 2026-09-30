using Natsx.Controller.Protocol;

namespace Natsx.Controller.Trust.Windows;

public interface ITrustedPeerStore
{
    void Put(
        TrustedPeerRecord record,
        ReadOnlySpan<byte> trustSecret);

    TrustedPeerMaterial? Get(PeerId peerId);

    IReadOnlyList<TrustedPeerRecord> List();

    void Remove(PeerId peerId);

    void Clear();
}
