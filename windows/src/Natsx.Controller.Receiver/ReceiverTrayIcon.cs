using System.Drawing;
using Forms = System.Windows.Forms;

namespace Natsx.Controller.Receiver;

internal sealed class ReceiverTrayIcon : IDisposable
{
    private const int MaxToolTipLength =
        63;

    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _disposed;

    public ReceiverTrayIcon()
    {
        var menu =
            new Forms.ContextMenuStrip();

        var open =
            new Forms.ToolStripMenuItem(
                "Open NATSX Controller");

        open.Click +=
            (_, _) =>
                OpenRequested?.Invoke();

        var exit =
            new Forms.ToolStripMenuItem(
                "Exit");

        exit.Click +=
            (_, _) =>
                ExitRequested?.Invoke();

        menu.Items.Add(
            open);
        menu.Items.Add(
            new Forms.ToolStripSeparator());
        menu.Items.Add(
            exit);

        _notifyIcon =
            new Forms.NotifyIcon
            {
                Icon =
                    SystemIcons.Application,
                Text =
                    "NATSX Controller Receiver",
                Visible =
                    true,
                ContextMenuStrip =
                    menu,
            };

        _notifyIcon.DoubleClick +=
            (_, _) =>
                OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? ExitRequested;

    public void UpdateStatus(
        string status)
    {
        if (_disposed ||
            string.IsNullOrWhiteSpace(
                status))
        {
            return;
        }

        string text =
            $"NATSX — {status}"
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ');

        _notifyIcon.Text =
            text.Length <=
                MaxToolTipLength
                ? text
                : text[..MaxToolTipLength];
    }

    public void ShowError(
        string message)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(
            5000,
            "NATSX Controller",
            message,
            Forms.ToolTipIcon.Error);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible =
            false;

        _notifyIcon.ContextMenuStrip?
            .Dispose();

        _notifyIcon.Dispose();

        _disposed =
            true;
    }
}
