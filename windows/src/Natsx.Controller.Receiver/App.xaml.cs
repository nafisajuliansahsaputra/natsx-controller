using System.IO;
using System.Windows;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.Receiver;

public partial class App : System.Windows.Application
{
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
                "--uninstall-cleanup"))
        {
            int exitCode =
                CleanupUserSettingsForUninstall();

            Shutdown(
                exitCode);
            return;
        }

        var window =
            new MainWindow();

        MainWindow =
            window;

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
                new HidMaestroVirtualGamepadBackend();

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
