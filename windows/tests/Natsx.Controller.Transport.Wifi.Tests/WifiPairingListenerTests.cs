using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Natsx.Controller.Protocol;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiPairingListenerTests
{
    [Fact]
    public async Task FailedAttemptsDoNotDisableNextPairing()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var phone = new UdpClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int attempts = 0;
        int failures = 0;
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task listener = WifiPairingListener.RunAsync(receiver, (packet, token) =>
        {
            int attempt = Interlocked.Increment(ref attempts);
            if (attempt == 1) throw new OperationCanceledException("Attempt expired", token);
            if (attempt == 2) { ProtocolFrameCodec.Decode(packet.Buffer); }
            if (attempt == 3) throw new CryptographicException("Invalid confirmation proof");
            complete.TrySetResult();
            return Task.CompletedTask;
        }, _ => Interlocked.Increment(ref failures), cancellation.Token);
        var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
        for (int i = 0; i < 4; i++)
            await phone.SendAsync(new byte[] { 1 }, endpoint, cancellation.Token);
        await complete.Task.WaitAsync(cancellation.Token);
        Assert.Equal(4, attempts);
        Assert.Equal(3, failures);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener);
    }
}
