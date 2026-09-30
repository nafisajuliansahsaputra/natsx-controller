namespace Natsx.Controller.Trust.Windows;

public static class TrustStoragePaths
{
    public static string RootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "NATSX",
            "Controller");

    public static string TrustedPeers =>
        Path.Combine(RootDirectory, "trusted-peers-v1.json");

    public static string LocalPeerIdentity =>
        Path.Combine(RootDirectory, "local-peer-id-v1.txt");
}
