using System.IO;
using System.Windows;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public partial class App : System.Windows.Application
{
    private ReceiverInstance? _instance;

    protected override async void OnStartup(
        StartupEventArgs eventArgs)
    {
        base.OnStartup(
            eventArgs);

        LocalCrashLog.Install(
            this);

        if (HasArgument(
                eventArgs.Args,
                "--install-driver"))
        {
            int exitCode =
                await BootstrapVirtualControllerAsync();

            Shutdown(
                exitCode);
            return;
        }

        if (HasArgument(
                eventArgs.Args,
                "--verify-gamepad-host-reconnect"))
        {
            int exitCode =
                await VerifyGamepadHostReconnectAsync();

            Shutdown(
                exitCode);
            return;
        }

        if (HasArgument(
                eventArgs.Args,
                "--verify-gamepad-host"))
        {
            int exitCode =
                await VerifyGamepadHostAsync();

            Shutdown(
                exitCode);
            return;
        }

        if (HasArgument(
                eventArgs.Args,
                "--uninstall-cleanup"))
        {
            int exitCode =
                CleanupUserSettingsForUninstall();

            Shutdown(
                exitCode);
            return;
        }

        bool exitRequested = HasArgument(eventArgs.Args, "--exit");
        _instance = new ReceiverInstance();
        if (!_instance.IsPrimary)
        {
            _instance.Signal(exitRequested);
            Shutdown(0);
            return;
        }
        if (exitRequested)
        {
            Shutdown(0);
            return;
        }

        var window =
            new MainWindow();

        MainWindow =
            window;

        _instance.Listen(
            () => Dispatcher.BeginInvoke(new Action(window.ShowAndActivate)),
            () => Dispatcher.BeginInvoke(new Action(window.ExitReceiver)));

        bool background =
            HasArgument(
                eventArgs.Args,
                "--background");

        _ =
            window.StartReceiverAsync();

        if (!background)
        {
            window.Show();
        }
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _instance?.Dispose();
        base.OnExit(eventArgs);
    }

    private static bool HasArgument(
        IEnumerable<string> arguments,
        string expected)
    {
        return arguments.Any(
            argument =>
                string.Equals(
                    argument,
                    expected,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> BootstrapVirtualControllerAsync()
    {
        try
        {
            await using var backend =
                new HidMaestroVirtualGamepadBackend(
                    ensureDriverVersion: true);

            await backend.StartAsync();
            return 0;
        }
        catch (Exception exception)
        {
            LocalCrashLog.Write(
                "installer-driver-bootstrap",
                exception);

            TryWriteSetupError(
                exception);
            return 1;
        }
    }

    private static async Task<int> VerifyGamepadHostAsync()
    {
        try
        {
            await using var backend =
                new PipeVirtualGamepadBackend();

            await backend.StartAsync();
            backend.Submit(
                Natsx.Controller.Core.GamepadState.Neutral);
            await backend.StopAsync();
            return 0;
        }
        catch (Exception exception)
        {
            LocalCrashLog.Write(
                "installer-gamepad-host-smoke",
                exception);

            TryWriteSetupError(
                exception);
            return 1;
        }
    }

    private static async Task<int> VerifyGamepadHostReconnectAsync()
    {
        try
        {
            await using var backend =
                new PipeVirtualGamepadBackend();

            await backend.StartAsync();

            bool sawDisconnect =
                false;

            bool sawReconnect =
                false;

            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddSeconds(
                    15);

            while (DateTimeOffset.UtcNow <
                deadline)
            {
                bool connected =
                    backend.IsStarted;

                if (!connected)
                {
                    sawDisconnect =
                        true;
                }

                if (sawDisconnect &&
                    connected)
                {
                    sawReconnect =
                        true;
                    break;
                }

                backend.Submit(
                    Natsx.Controller.Core.GamepadState.Neutral);

                await Task.Delay(
                    50);
            }

            await backend.StopAsync();

            if (!sawDisconnect ||
                !sawReconnect)
            {
                throw new InvalidOperationException(
                    "The Receiver did not observe a full gamepad-host disconnect and automatic reconnect cycle.");
            }

            return 0;
        }
        catch (Exception exception)
        {
            LocalCrashLog.Write(
                "gamepad-host-reconnect-smoke",
                exception);

            TryWriteSetupError(
                exception);

            return 1;
        }
    }

    private static int CleanupUserSettingsForUninstall()
    {
        try
        {
            ReceiverUserSettings.CleanupForUninstall();
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void TryWriteSetupError(
        Exception exception)
    {
        try
        {
            string directory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.CommonApplicationData),
                    "NATSX",
                    "Controller");

            Directory.CreateDirectory(
                directory);

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "setup-driver-error.log"),
                exception.ToString());
        }
        catch
        {
            // Setup logging must never mask the original bootstrap failure.
        }
    }
}
