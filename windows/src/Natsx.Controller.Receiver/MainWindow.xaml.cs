using System.Windows;

namespace Natsx.Controller.Receiver;

public partial class MainWindow : Window
{
    private readonly ReceiverRuntime _runtime =
        new();

    public MainWindow()
    {
        InitializeComponent();

        _runtime.StatusChanged +=
            OnRuntimeStatusChanged;

        Loaded +=
            OnLoaded;

        Closed +=
            OnClosed;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs eventArgs)
    {
        try
        {
            await _runtime.StartAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text =
                exception.Message;
        }
    }

    private void OnRuntimeStatusChanged(
        string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () =>
                    StatusText.Text =
                        status);
            return;
        }

        StatusText.Text =
            status;
    }

    private async void OnClosed(
        object? sender,
        EventArgs eventArgs)
    {
        _runtime.StatusChanged -=
            OnRuntimeStatusChanged;

        await _runtime
            .DisposeAsync();
    }
}
