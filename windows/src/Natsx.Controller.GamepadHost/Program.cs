using System.ServiceProcess;

namespace Natsx.Controller.GamepadHost;

internal static class Program
{
    public static int Main(
        string[] args)
    {
        if (args.Any(
                argument =>
                    string.Equals(
                        argument,
                        "--console",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return RunConsoleAsync()
                .GetAwaiter()
                .GetResult();
        }

        if (!args.Any(
                argument =>
                    string.Equals(
                        argument,
                        "--service",
                        StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine(
                "NATSX Gamepad Host must be started by the Windows Service Control Manager.");
            return 2;
        }

        ServiceBase.Run(
            new GamepadHostWindowsService());

        return 0;
    }

    private static async Task<int> RunConsoleAsync()
    {
        using var cancellation =
            new CancellationTokenSource();

        Console.CancelKeyPress +=
            (_, eventArgs) =>
            {
                eventArgs.Cancel =
                    true;

                cancellation.Cancel();
            };

        try
        {
            var server =
                new GamepadHostServer();

            await server
                .RunAsync(
                    cancellation.Token)
                .ConfigureAwait(false);

            return 0;
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            GamepadHostLog.Write(
                "console-fatal",
                exception);

            Console.Error.WriteLine(
                exception);

            return 1;
        }
    }
}
