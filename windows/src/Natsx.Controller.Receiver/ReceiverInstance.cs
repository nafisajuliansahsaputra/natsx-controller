using System.Security.Principal;

namespace Natsx.Controller.Receiver;

// One gameplay receiver per interactive user/session, independent of the EXE path.
internal sealed class ReceiverInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _exit;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;

    public ReceiverInstance()
    {
        string identity = WindowsIdentity.GetCurrent().User?.Value ??
            throw new InvalidOperationException("Cannot resolve the Receiver user identity.");
        string prefix = @"Local\NATSX.Controller.Receiver." + identity;
        _mutex = new Mutex(true, prefix + ".Instance", out bool created);
        IsPrimary = created;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".Activate");
        _exit = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".Exit");
    }

    public bool IsPrimary { get; }

    public void Signal(bool exit) => (exit ? _exit : _activate).Set();

    public void Listen(Action activate, Action exit)
    {
        if (!IsPrimary) throw new InvalidOperationException("Only the primary Receiver can listen.");
        _listener = Task.Run(() =>
        {
            WaitHandle[] signals = [_activate, _exit, _lifetime.Token.WaitHandle];
            while (true)
            {
                int signal = WaitHandle.WaitAny(signals);
                if (signal == 2) return;
                if (signal == 1) { exit(); return; }
                activate();
            }
        });
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _listener?.GetAwaiter().GetResult();
        _activate.Dispose();
        _exit.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _lifetime.Dispose();
    }
}
