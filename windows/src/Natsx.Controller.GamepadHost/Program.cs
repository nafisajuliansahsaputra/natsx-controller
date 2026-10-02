using System.ServiceProcess;

namespace Natsx.Controller.GamepadHost;

internal static class Program
{
    public static int Main(
        string[] args)
    {
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

}
