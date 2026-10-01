namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingPayloadCodecTests
{
    private static readonly PeerId AndroidPeer =
        PeerId.FromBytes(Convert.FromHexString("000102030405060708090A0B0C0D0E0F"));

    private static readonly PeerId WindowsPeer =
        PeerId.FromBytes(Convert.FromHexString("101112131415161718191A1B1C1D1E1F"));

    [Fact]
    public void CanonicalPairingPayloadsMatchFrozenVectors()
    {
        byte[] androidNonce = Convert.FromHexString(
            "202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F");
        byte[] windowsNonce = Convert.FromHexString(
            "404142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F");
        byte[] androidKey = Convert.FromHexString(
            "04606162636465666768696A6B6C6D6E6F707172737475767778797A7B7C7D7E7F" +
            "808182838485868788898A8B8C8D8E8F909192939495969798999A9B9C9D9E9F");
        byte[] windowsKey = Convert.FromHexString(
            "04A0A1A2A3A4A5A6A7A8A9AAABACADAEAFB0B1B2B3B4B5B6B7B8B9BABBBCBDBEBF" +
            "C0C1C2C3C4C5C6C7C8C9CACBCCCDCECFD0D1D2D3D4D5D6D7D8D9DADBDCDDDEDF");
        byte[] responseProof = Convert.FromHexString(
            "69168C6FB8AB71E31E4CABD994C045261BBBA267427408BFC3AD11A9E445DC6B");
        byte[] androidProof = Convert.FromHexString(
            "5901A5130C82C7FC1655C5504BF00AEB5C1E0678058F6B5B43D20161B1B02B7F");
        byte[] windowsProof = Convert.FromHexString(
            "5B85B56DA7B3D414441A0D22138E9334B99AB5CB448DE7C316AC9B0A2FC82FE1");

        byte[] hello = PairingPayloadCodec.EncodeHello(
            new PairingHelloPayload(
                AndroidPeer,
                TransportCapabilities.Wifi |
                TransportCapabilities.Bluetooth |
                TransportCapabilities.UsbDirect,
                androidNonce,
                androidKey));

        byte[] response = PairingPayloadCodec.EncodeResponse(
            new PairingResponsePayload(
                WindowsPeer,
                TransportCapabilities.Wifi |
                TransportCapabilities.Bluetooth |
                TransportCapabilities.UsbDirect,
                windowsNonce,
                windowsKey,
                responseProof));

        byte[] confirm = PairingPayloadCodec.EncodeConfirmation(
            new PairingConfirmationPayload(AndroidPeer, androidProof));

        byte[] complete = PairingPayloadCodec.EncodeConfirmation(
            new PairingConfirmationPayload(WindowsPeer, windowsProof));

        Assert.Equal(
            "000102030405060708090A0B0C0D0E0F07000000" +
            "202122232425262728292A2B2C2D2E2F303132333435363738393A3B3C3D3E3F" +
            "04606162636465666768696A6B6C6D6E6F707172737475767778797A7B7C7D7E7F" +
            "808182838485868788898A8B8C8D8E8F909192939495969798999A9B9C9D9E9F",
            Convert.ToHexString(hello));

        Assert.Equal(
            "101112131415161718191A1B1C1D1E1F07000000" +
            "404142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F" +
            "04A0A1A2A3A4A5A6A7A8A9AAABACADAEAFB0B1B2B3B4B5B6B7B8B9BABBBCBDBEBF" +
            "C0C1C2C3C4C5C6C7C8C9CACBCCCDCECFD0D1D2D3D4D5D6D7D8D9DADBDCDDDEDF" +
            "69168C6FB8AB71E31E4CABD994C045261BBBA267427408BFC3AD11A9E445DC6B",
            Convert.ToHexString(response));

        Assert.Equal(
            "000102030405060708090A0B0C0D0E0F5901A5130C82C7FC1655C5504BF00AEB5C1E0678058F6B5B43D20161B1B02B7F",
            Convert.ToHexString(confirm));

        Assert.Equal(
            "101112131415161718191A1B1C1D1E1F5B85B56DA7B3D414441A0D22138E9334B99AB5CB448DE7C316AC9B0A2FC82FE1",
            Convert.ToHexString(complete));
    }

    [Fact]
    public void ConfirmationProofsMatchCanonicalVectorAndAreRoleBound()
    {
        byte[] pairingKey = Convert.FromHexString(
            "8A12E9DE1C2DF8D5E83D7AB02EFFFFC2D543D52AF0DA156FD912138DC8C791CB");
        byte[] transcriptHash = Convert.FromHexString(
            "AE3A44B49AA7A34878ED64EFA63CFB5D3A06B041515FE3FB77A93F14377A6BB0");
        SessionId sessionId = SessionId.FromBytes(
            Convert.FromHexString("E0E1E2E3E4E5E6E7E8E9EAEBECEDEEEF"));

        byte[] androidProof = PairingCrypto.ComputePairingConfirmationProof(
            PairingConfirmationRole.Android,
            pairingKey,
            transcriptHash,
            sessionId);

        byte[] windowsProof = PairingCrypto.ComputePairingConfirmationProof(
            PairingConfirmationRole.Windows,
            pairingKey,
            transcriptHash,
            sessionId);

        Assert.Equal(
            "5901A5130C82C7FC1655C5504BF00AEB5C1E0678058F6B5B43D20161B1B02B7F",
            Convert.ToHexString(androidProof));
        Assert.Equal(
            "5B85B56DA7B3D414441A0D22138E9334B99AB5CB448DE7C316AC9B0A2FC82FE1",
            Convert.ToHexString(windowsProof));
        Assert.NotEqual(androidProof, windowsProof);
        Assert.False(
            PairingCrypto.VerifyPairingConfirmationProof(
                PairingConfirmationRole.Windows,
                pairingKey,
                transcriptHash,
                sessionId,
                androidProof));
    }
}
