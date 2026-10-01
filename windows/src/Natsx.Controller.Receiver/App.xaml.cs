using System.Windows;

namespace Natsx.Controller.Receiver;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(
        StartupEventArgs eventArgs)
    {
        base.OnStartup(
            eventArgs);

        var window =
            new MainWindow();

        MainWindow =
            window;

        bool background =
            eventArgs.Args.Any(
                argument =>
                    string.Equals(
                        argument,
                        "--background",
                        StringComparison.OrdinalIgnoreCase));

        _ =
            window.StartReceiverAsync();

        if (!background)
        {
            window.Show();
        }
    }
}
