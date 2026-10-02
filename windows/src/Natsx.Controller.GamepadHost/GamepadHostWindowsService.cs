using System.ServiceProcess;

namespace Natsx.Controller.GamepadHost;

internal sealed class GamepadHostWindowsService : ServiceBase
{
    public const string WindowsServiceName =
        "NatsxControllerGamepadHost";

    private CancellationTokenSource? _lifetime;
    private Task? _serverTask;

    public GamepadHostWindowsService()
    {
        ServiceName =
            WindowsServiceName;

        CanStop =
            true;
        CanShutdown =
            true;
        AutoLog =
            false;
    }

    protected override void OnStart(
        string[] args)
    {
        var lifetime =
            new CancellationTokenSource();

        _lifetime =
            lifetime;

        var server =
            new GamepadHostServer();

        _serverTask =
            Task.Run(
                () =>
                    server.RunAsync(
                        lifetime.Token));

        _ =
            _serverTask
                .ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            GamepadHostLog.Write(
                                "service-fatal",
                                task.Exception ??
                                new InvalidOperationException(
                                    "The gamepad-host service stopped unexpectedly."));

                            Environment.FailFast(
                                "NATSX Gamepad Host service failed.",
                                task.Exception);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
    }

    protected override void OnStop()
    {
        StopServer();
    }

    protected override void OnShutdown()
    {
        StopServer();
        base.OnShutdown();
    }

    protected override void Dispose(
        bool disposing)
    {
        if (disposing)
        {
            StopServer();
        }

        base.Dispose(
            disposing);
    }

    private void StopServer()
    {
        CancellationTokenSource? lifetime =
            Interlocked.Exchange(
                ref _lifetime,
                null);

        Task? serverTask =
            Interlocked.Exchange(
                ref _serverTask,
                null);

        if (lifetime is null)
        {
            return;
        }

        lifetime.Cancel();

        if (serverTask is not null)
        {
            try
            {
                serverTask.Wait(
                    TimeSpan.FromSeconds(10));
            }
            catch
            {
            }
        }

        lifetime.Dispose();
    }
}
