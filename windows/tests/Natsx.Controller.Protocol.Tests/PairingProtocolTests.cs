using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Protocol.Tests;

public sealed class PairingProtocolTests
{
    private const string AndroidPrivateKeyHex =
        "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104200000000000000000000000000000000000000000000000000000000000000001a144034200046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5";

    private const string WindowsPrivateKeyHex =
        "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b02010104200000000000000000000000000000000000000000000000000000000000000002a144034200047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d1";

    private static readonly SessionId AndroidId = SessionId.FromBytes(
        Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

    private static readonly SessionId WindowsId = SessionId.FromBytes(
        Convert.FromHexString("FFEEDDCCBBAA99887766554433221100"));

    [Fact]
    public void PairingCodec_MatchesSharedRequestAndResponseVectors()
    {
        using ECDiffieHellman android = ImportPrivate(AndroidPrivateKeyHex);
        using ECDiffieHellman windows = ImportPrivate(WindowsPrivateKeyHex);

        byte[] request = PairingCodec.EncodeRequest(
            new PairingHelloPayload(
                AndroidId,
                PairingCrypto.ExportPublicKey(android),
                "Android"));

        byte[] response = PairingCodec.EncodeResponse(
            new PairingHelloPayload(
                WindowsId,
                PairingCrypto.ExportPublicKey(windows),
                "LEGION"));

        Assert.Equal(
            "4e5850310100010000112233445566778899aabbccddeeff5b0007003059301306072a8648ce3d020106082a8648ce3d030107034200046b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c2964fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5416e64726f6964",
            Convert.ToHexString(request).ToLowerInvariant());

        Assert.Equal(
            "4e58503101000200ffeeddccbbaa998877665544332211005b0006003059301306072a8648ce3d020106082a8648ce3d030107034200047cf27b188d034f7e8a52380304b51ac3c08969e277f21b35a60b48fc4766997807775510db8ed040293d9ac69f7430dbba7dade63ce982299e04b79d227873d14c4547494f4e",
            Convert.ToHexString(response).ToLowerInvariant());

        PairingHelloPayload decodedRequest = PairingCodec.DecodeRequest(request);
        PairingHelloPayload decodedResponse = PairingCodec.DecodeResponse(response);

        Assert.Equal(AndroidId, decodedRequest.DeviceId);
        Assert.Equal("Android", decodedRequest.DisplayName);
        Assert.Equal(WindowsId, decodedResponse.DeviceId);
        Assert.Equal("LEGION", decodedResponse.DisplayName);
    }

    [Fact]
    public void PairingCrypto_MatchesSharedRootSasAndTags()
    {
        using ECDiffieHellman android = ImportPrivate(AndroidPrivateKeyHex);
        using ECDiffieHellman windows = ImportPrivate(WindowsPrivateKeyHex);

        byte[] request = PairingCodec.EncodeRequest(
            new PairingHelloPayload(
                AndroidId,
                PairingCrypto.ExportPublicKey(android),
                "Android"));

        byte[] response = PairingCodec.EncodeResponse(
            new PairingHelloPayload(
                WindowsId,
                PairingCrypto.ExportPublicKey(windows),
                "LEGION"));

        byte[] transcriptHash =
            PairingCrypto.ComputeTranscriptHash(request, response);

        Assert.Equal(
            "e8a010dbb7b3bf43804ab65332deb4ad064b1d9f70454678dfc348993ff5d4ef",
            Convert.ToHexString(transcriptHash).ToLowerInvariant());

        byte[] androidRoot = PairingCrypto.DerivePairingRootKey(
            android,
            PairingCrypto.ExportPublicKey(windows),
            transcriptHash);

        byte[] windowsRoot = PairingCrypto.DerivePairingRootKey(
            windows,
            PairingCrypto.ExportPublicKey(android),
            transcriptHash);

        Assert.Equal(androidRoot, windowsRoot);
        Assert.Equal(
            "d0b7e0a0fb04b38d46e439369ebdabaee772e5b623a121e4b16a2cf19928c00a",
            Convert.ToHexString(androidRoot).ToLowerInvariant());

        Assert.Equal(
            "609252",
            PairingCrypto.ComputeSas(androidRoot, transcriptHash));

        Assert.Equal(
            "516a3d71a88c2b821ca714984076bcf0",
            Convert.ToHexString(
                PairingCrypto.ComputeConfirmationTag(
                    PairingRole.Android,
                    androidRoot,
                    transcriptHash))
                .ToLowerInvariant());

        Assert.Equal(
            "5b1126444b5610ed17dbc7ae0fa87906",
            Convert.ToHexString(
                PairingCrypto.ComputeConfirmationTag(
                    PairingRole.Windows,
                    androidRoot,
                    transcriptHash))
                .ToLowerInvariant());

        Assert.Equal(
            "b54dd1f9d7b392a8a86492dd6424f019",
            Convert.ToHexString(
                PairingCrypto.ComputeCompletionTag(
                    androidRoot,
                    transcriptHash))
                .ToLowerInvariant());

        CryptographicOperations.ZeroMemory(androidRoot);
        CryptographicOperations.ZeroMemory(windowsRoot);
        CryptographicOperations.ZeroMemory(transcriptHash);
    }

    private static ECDiffieHellman ImportPrivate(string hex)
    {
        ECDiffieHellman key = ECDiffieHellman.Create();
        key.ImportPkcs8PrivateKey(
            Convert.FromHexString(hex),
            out int bytesRead);

        Assert.Equal(hex.Length / 2, bytesRead);
        return key;
    }
}
