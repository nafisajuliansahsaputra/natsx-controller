using System.Net.Sockets;

namespace Natsx.Controller.Transport.Wifi;

/// <summary>One failed or expired pairing attempt must never disable subsequent pairing.</summary>
public static class WifiPairingListener
{
    public static async Task RunAsync(
        UdpClient client,
        Func<UdpReceiveResult, CancellationToken, Task> handleAttempt,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var datagram = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await handleAttempt(datagram, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                reportFailure(exception);
            }
        }
    }
}
