using HIDMaestro;

namespace Natsx.Controller.VirtualGamepad;

public static class HidMaestroDriverInstaller
{
    public static void Install()
    {
        using var context = new HMContext();
        context.InstallDriver();
    }
}
