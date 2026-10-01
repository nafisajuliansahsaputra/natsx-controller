using System.IO;
using Microsoft.Win32;

namespace Natsx.Controller.Receiver;

internal sealed class ReceiverUserSettings
{
    private const string SettingsKeyPath =
        @"Software\NATSX\Controller Receiver";
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName =
        "NATSX Controller Receiver";
    private const string CloseToTrayValueName =
        "CloseToTray";

    private ReceiverUserSettings(
        bool closeToTray)
    {
        CloseToTray =
            closeToTray;
    }

    public bool CloseToTray
    {
        get;
        private set;
    }

    public bool StartWithWindows
    {
        get
        {
            using RegistryKey? run =
                Registry.CurrentUser
                    .OpenSubKey(
                        RunKeyPath,
                        writable: false);

            return run?.GetValue(
                    RunValueName)
                is string value &&
                !string.IsNullOrWhiteSpace(
                    value);
        }
    }

    public static ReceiverUserSettings Load()
    {
        using RegistryKey? key =
            Registry.CurrentUser
                .OpenSubKey(
                    SettingsKeyPath,
                    writable: false);

        bool closeToTray =
            key?.GetValue(
                CloseToTrayValueName)
            is int value
                ? value != 0
                : true;

        return new ReceiverUserSettings(
            closeToTray);
    }

    public void SetCloseToTray(
        bool enabled)
    {
        using RegistryKey key =
            Registry.CurrentUser
                .CreateSubKey(
                    SettingsKeyPath,
                    writable: true);

        key.SetValue(
            CloseToTrayValueName,
            enabled ? 1 : 0,
            RegistryValueKind.DWord);

        CloseToTray =
            enabled;
    }

    public void SetStartWithWindows(
        bool enabled)
    {
        using RegistryKey run =
            Registry.CurrentUser
                .CreateSubKey(
                    RunKeyPath,
                    writable: true);

        if (!enabled)
        {
            run.DeleteValue(
                RunValueName,
                throwOnMissingValue: false);
            return;
        }

        run.SetValue(
            RunValueName,
            BuildStartupCommand(),
            RegistryValueKind.String);
    }

    private static string BuildStartupCommand()
    {
        string executable =
            Environment.ProcessPath ??
            throw new InvalidOperationException(
                "Could not resolve the Receiver executable path.");

        string[] commandLine =
            Environment.GetCommandLineArgs();

        if (string.Equals(
                Path.GetFileNameWithoutExtension(
                    executable),
                "dotnet",
                StringComparison.OrdinalIgnoreCase) &&
            commandLine.Length > 0 &&
            commandLine[0]
                .EndsWith(
                    ".dll",
                    StringComparison.OrdinalIgnoreCase))
        {
            return $"{Quote(executable)} {Quote(Path.GetFullPath(commandLine[0]))} --background";
        }

        return $"{Quote(executable)} --background";
    }

    private static string Quote(
        string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
