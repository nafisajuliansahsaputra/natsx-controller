using System.Text;

namespace Natsx.Controller.Receiver;

internal static class LocalCrashLog
{
    private const long MaxLogBytes =
        256 * 1024;
    private const int MaxArchives =
        3;

    private static readonly object Gate =
        new();

    private static bool _installed;

    public static void Install(
        System.Windows.Application application)
    {
        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            application.DispatcherUnhandledException +=
                (_, eventArgs) =>
                    Write(
                        "dispatcher-unhandled",
                        eventArgs.Exception);

            AppDomain.CurrentDomain.UnhandledException +=
                (_, eventArgs) =>
                {
                    if (eventArgs.ExceptionObject is Exception exception)
                    {
                        Write(
                            "appdomain-unhandled",
                            exception);
                    }
                };

            TaskScheduler.UnobservedTaskException +=
                (_, eventArgs) =>
                    Write(
                        "task-unobserved",
                        eventArgs.Exception);

            _installed =
                true;
        }
    }

    public static void Write(
        string category,
        Exception exception)
    {
        try
        {
            lock (Gate)
            {
                string directory =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "NATSX",
                        "Controller",
                        "diagnostics");

                Directory.CreateDirectory(
                    directory);

                string path =
                    Path.Combine(
                        directory,
                        "receiver-crash.log");

                RotateIfNeeded(
                    path);

                string entry =
                    BuildEntry(
                        category,
                        exception);

                File.AppendAllText(
                    path,
                    entry,
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never become a new failure source.
        }
    }

    private static string BuildEntry(
        string category,
        Exception exception)
    {
        string message =
            exception.Message
                .Replace(
                    "\r",
                    " ",
                    StringComparison.Ordinal)
                .Replace(
                    "\n",
                    " ",
                    StringComparison.Ordinal);

        if (message.Length > 2048)
        {
            message =
                message[..2048] +
                "…";
        }

        string stack =
            exception.StackTrace ??
            "(no stack trace)";

        if (stack.Length > 32 * 1024)
        {
            stack =
                stack[..(32 * 1024)] +
                Environment.NewLine +
                "(stack trace truncated)";
        }

        return string.Join(
                Environment.NewLine,
                $"[{DateTimeOffset.UtcNow:O}] {category}",
                $"Exception: {exception.GetType().FullName}",
                $"Message: {message}",
                stack,
                "---") +
            Environment.NewLine;
    }

    private static void RotateIfNeeded(
        string path)
    {
        var current =
            new FileInfo(
                path);

        if (!current.Exists ||
            current.Length <
            MaxLogBytes)
        {
            return;
        }

        string oldest =
            $"{path}.{MaxArchives}";

        if (File.Exists(
                oldest))
        {
            File.Delete(
                oldest);
        }

        for (int index =
                 MaxArchives - 1;
             index >= 1;
             index--)
        {
            string source =
                $"{path}.{index}";

            if (!File.Exists(
                    source))
            {
                continue;
            }

            File.Move(
                source,
                $"{path}.{index + 1}");
        }

        File.Move(
            path,
            $"{path}.1");
    }
}
