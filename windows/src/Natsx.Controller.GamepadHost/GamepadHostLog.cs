namespace Natsx.Controller.GamepadHost;

internal static class GamepadHostLog
{
    private static readonly object Gate =
        new();

    private const long MaximumBytes =
        256 * 1024;

    public static void Write(
        string category,
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

            string path =
                Path.Combine(
                    directory,
                    "gamepad-host.log");

            lock (Gate)
            {
                if (File.Exists(
                        path) &&
                    new FileInfo(
                        path).Length >
                    MaximumBytes)
                {
                    File.Delete(
                        path);
                }

                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.UtcNow:O}] {category}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
