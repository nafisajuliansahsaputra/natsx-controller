using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Natsx.Controller.Protocol;
using Natsx.Controller.Receiver;
using Xunit;

namespace Natsx.Controller.Receiver.Tests;

public sealed class ReceiverPairingServerIntegrationTests
{
    private static readonly SessionId AndroidId = SessionId.FromBytes(
        Convert.FromHexString("11223344556677889900AABBCCDDEEFF"));

    private static readonly SessionId WindowsId = SessionId.FromBytes(
        Convert.FromHexString("FFEEDDCCBBAA00998877665544332211"));

    [Fact]
    public async Task UserConfirmedPairing_PersistsMatchingProtectedTrustKey()
    {
        string root =
            Path.Combine(
                Path.GetTempPath(),
                "natsx-controller-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var trustStore =
                new WindowsTrustedPeerStore(root);

            await using var server =
                new ReceiverPairingServer(
                    WindowsId,
                    trustStore,
                    "TEST-PC");

            var promptSource =
                new TaskCompletionSource<PairingPrompt>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            var completedSource =
                new TaskCompletionSource<SessionId>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            server.PromptReady += prompt =>
            {
                promptSource.TrySetResult(prompt);
                server.Confirm();
            };

            server.PairingCompleted += (deviceId, _) =>
                completedSource.TrySetResult(deviceId);

            server.StartPairing();

            using var client = new TcpClient();
            await client.ConnectAsync(
                IPAddress.Loopback,
                ReceiverPairingServer.PairingPort);

            client.NoDelay = true;
            NetworkStream stream = client.GetStream();

            using ECDiffieHellman androidKey =
                PairingCrypto.CreateEphemeralKey();

            byte[] request =
                PairingCodec.EncodeRequest(
                    new PairingHelloPayload(
                        AndroidId,
                        PairingCrypto.ExportPublicKey(
                            androidKey),
                        "TEST-ANDROID"));

            await WriteMessageAsync(stream, request);

            byte[] responseBytes =
                await ReadMessageAsync(stream);

            PairingHelloPayload response =
                PairingCodec.DecodeResponse(
                    responseBytes);

            Assert.Equal(WindowsId, response.DeviceId);

            byte[] transcriptHash =
                PairingCrypto.ComputeTranscriptHash(
                    request,
                    responseBytes);

            byte[] rootKey =
                PairingCrypto.DerivePairingRootKey(
                    androidKey,
                    response.PublicKeyDer,
                    transcriptHash);

            PairingPrompt prompt =
                await promptSource.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));

            Assert.Equal(
                PairingCrypto.ComputeSas(
                    rootKey,
                    transcriptHash),
                prompt.SasCode);

            byte[] androidTag =
                PairingCrypto.ComputeConfirmationTag(
                    PairingRole.Android,
                    rootKey,
                    transcriptHash);

            await WriteMessageAsync(
                stream,
                PairingCodec.EncodeConfirm(
                    new PairingConfirmPayload(
                        PairingRole.Android,
                        androidTag)));

            byte[] windowsConfirmBytes =
                await ReadMessageAsync(stream);

            PairingConfirmPayload windowsConfirm =
                PairingCodec.DecodeConfirm(
                    windowsConfirmBytes);

            Assert.Equal(
                PairingRole.Windows,
                windowsConfirm.Role);

            byte[] expectedWindowsTag =
                PairingCrypto.ComputeConfirmationTag(
                    PairingRole.Windows,
                    rootKey,
                    transcriptHash);

            Assert.True(
                PairingCrypto.VerifyTag(
                    expectedWindowsTag,
                    windowsConfirm.Tag));

            byte[] completeBytes =
                await ReadMessageAsync(stream);

            byte[] completeTag =
                PairingCodec.DecodeComplete(
                    completeBytes);

            byte[] expectedComplete =
                PairingCrypto.ComputeCompletionTag(
                    rootKey,
                    transcriptHash);

            Assert.True(
                PairingCrypto.VerifyTag(
                    expectedComplete,
                    completeTag));

            SessionId completed =
                await completedSource.Task.WaitAsync(
                    TimeSpan.FromSeconds(5));

            Assert.Equal(AndroidId, completed);

            TrustedPeerCredentials? stored =
                trustStore.TryGet(AndroidId);

            Assert.NotNull(stored);
            Assert.Equal(
                rootKey,
                stored!.PairingRootKey);

            CryptographicOperations.ZeroMemory(
                stored.PairingRootKey);
            CryptographicOperations.ZeroMemory(rootKey);
            CryptographicOperations.ZeroMemory(
                transcriptHash);
            CryptographicOperations.ZeroMemory(
                androidTag);
            CryptographicOperations.ZeroMemory(
                expectedWindowsTag);
            CryptographicOperations.ZeroMemory(
                windowsConfirm.Tag);
            CryptographicOperations.ZeroMemory(
                completeTag);
            CryptographicOperations.ZeroMemory(
                expectedComplete);
            CryptographicOperations.ZeroMemory(
                response.PublicKeyDer);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<byte[]> ReadMessageAsync(
        Stream stream)
    {
        byte[] prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix);

        uint length =
            BinaryPrimitives.ReadUInt32LittleEndian(
                prefix);

        Assert.InRange(
            length,
            (uint)PairingCodec.HeaderSize,
            (uint)PairingCodec.MaximumPayloadSize);

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload);
        return payload;
    }

    private static async Task WriteMessageAsync(
        Stream stream,
        byte[] payload)
    {
        byte[] prefix = new byte[4];

        BinaryPrimitives.WriteUInt32LittleEndian(
            prefix,
            checked((uint)payload.Length));

        await stream.WriteAsync(prefix);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
