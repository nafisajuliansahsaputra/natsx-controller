using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;
using Natsx.Controller.Core;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.GamepadHost;

internal sealed class GamepadHostServer
{
    private static readonly TimeSpan StateWatchdogTimeout =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan StateWatchdogPollInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly string _expectedReceiverPath =
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "Natsx.Controller.Receiver.exe"));

    public async Task RunAsync(
        CancellationToken cancellationToken)
    {
        await using var backend =
            new HidMaestroVirtualGamepadBackend();

        await backend
            .StartAsync(
                cancellationToken)
            .ConfigureAwait(false);

        backend.Submit(
            GamepadState.Neutral);

        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream pipe =
                CreateServerPipe();

            try
            {
                await pipe
                    .WaitForConnectionAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                AuthenticateClient(
                    pipe);

                await HandleClientAsync(
                        pipe,
                        backend,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    Win32Exception or
                    FormatException or
                    InvalidOperationException)
            {
                GamepadHostLog.Write(
                    "client-session",
                    exception);
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        HidMaestroVirtualGamepadBackend backend,
        CancellationToken cancellationToken)
    {
        byte[] header =
            new byte[
                GamepadHostProtocol.HeaderSize];

        byte[] payload =
            new byte[
                GamepadHostProtocol.MaximumPayloadSize];

        (
            GamepadHostMessageType firstType,
            ushort firstLength) =
            await ReadFrameAsync(
                    pipe,
                    header,
                    payload,
                    cancellationToken)
                .ConfigureAwait(false);

        if (firstType !=
                GamepadHostMessageType.Hello ||
            firstLength != 0)
        {
            throw new FormatException(
                "Gamepad-host clients must begin with an empty HELLO frame.");
        }

        object writeGate =
            new();

        void ForwardRumble(
            RumbleState rumble)
        {
            try
            {
                Span<byte> frame =
                    stackalloc byte[
                        GamepadHostProtocol.HeaderSize +
                        GamepadHostProtocol.RumblePayloadSize];

                GamepadHostProtocol.EncodeHeader(
                    frame,
                    GamepadHostMessageType.Rumble,
                    GamepadHostProtocol.RumblePayloadSize);

                GamepadHostProtocol.EncodeRumble(
                    rumble,
                    frame[
                        GamepadHostProtocol.HeaderSize..]);

                lock (writeGate)
                {
                    pipe.Write(
                        frame);
                    pipe.Flush();
                }
            }
            catch
            {
            }
        }

        backend.RumbleReceived +=
            ForwardRumble;

        await WriteEmptyFrameAsync(
                pipe,
                GamepadHostMessageType.Ready,
                cancellationToken)
            .ConfigureAwait(false);

        long lastStateTimestamp =
            Stopwatch.GetTimestamp();

        using var watchdogLifetime =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        Task watchdogTask =
            WatchStateFreshnessAsync(
                pipe,
                backend,
                () =>
                    Volatile.Read(
                        ref lastStateTimestamp),
                watchdogLifetime.Token);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                (
                    GamepadHostMessageType messageType,
                    ushort payloadLength) =
                    await ReadFrameAsync(
                            pipe,
                            header,
                            payload,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (messageType !=
                    GamepadHostMessageType.GamepadState)
                {
                    throw new FormatException(
                        $"Unexpected client message {messageType}.");
                }

                GamepadState state =
                    GamepadHostProtocol.DecodeGamepadState(
                        payload.AsSpan(
                            0,
                            payloadLength));

                backend.Submit(
                    state);

                Volatile.Write(
                    ref lastStateTimestamp,
                    Stopwatch.GetTimestamp());
            }
        }
        finally
        {
            backend.RumbleReceived -=
                ForwardRumble;

            watchdogLifetime.Cancel();

            try
            {
                await watchdogTask
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                backend.Submit(
                    GamepadState.Neutral);
            }
            catch
            {
            }
        }
    }

    private static async Task WatchStateFreshnessAsync(
        NamedPipeServerStream pipe,
        HidMaestroVirtualGamepadBackend backend,
        Func<long> lastStateTimestamp,
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(
                StateWatchdogPollInterval);

        while (await timer
            .WaitForNextTickAsync(
                cancellationToken)
            .ConfigureAwait(false))
        {
            TimeSpan silence =
                Stopwatch.GetElapsedTime(
                    lastStateTimestamp(),
                    Stopwatch.GetTimestamp());

            if (silence <
                StateWatchdogTimeout)
            {
                continue;
            }

            try
            {
                backend.Submit(
                    GamepadState.Neutral);
            }
            catch
            {
            }

            try
            {
                pipe.Dispose();
            }
            catch
            {
            }

            return;
        }
    }

    private void AuthenticateClient(
        NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(
                pipe.SafePipeHandle.DangerousGetHandle(),
                out uint processId))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to resolve the named-pipe client process.");
        }

        using Process process =
            Process.GetProcessById(
                checked((int)processId));

        string actualPath =
            process.MainModule?.FileName ??
            throw new UnauthorizedAccessException(
                "Unable to resolve the named-pipe client executable.");

        actualPath =
            Path.GetFullPath(
                actualPath);

        if (!string.Equals(
                actualPath,
                _expectedReceiverPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Rejected gamepad-host client '{actualPath}'.");
        }
    }

    private static NamedPipeServerStream CreateServerPipe()
    {
        const string sddl =
            "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;AU)";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                1,
                out IntPtr securityDescriptor,
                out _))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to create the gamepad-host pipe security descriptor.");
        }

        try
        {
            var securityAttributes =
                new SecurityAttributes
                {
                    Length =
                        Marshal.SizeOf<SecurityAttributes>(),
                    SecurityDescriptor =
                        securityDescriptor,
                    InheritHandle =
                        false,
                };

            string path =
                @"\\.\pipe\" +
                GamepadHostProtocol.PipeName;

            SafePipeHandle handle =
                CreateNamedPipe(
                    path,
                    PipeAccessDuplex |
                    FileFlagOverlapped,
                    PipeTypeByte |
                    PipeReadModeByte |
                    PipeWait |
                    PipeRejectRemoteClients,
                    1,
                    4096,
                    4096,
                    0,
                    ref securityAttributes);

            if (handle.IsInvalid)
            {
                int error =
                    Marshal.GetLastWin32Error();

                handle.Dispose();

                throw new Win32Exception(
                    error,
                    "Unable to create the privileged gamepad-host named pipe.");
            }

            return new NamedPipeServerStream(
                PipeDirection.InOut,
                isAsync: true,
                isConnected: false,
                handle);
        }
        finally
        {
            LocalFree(
                securityDescriptor);
        }
    }

    private static async Task<(
        GamepadHostMessageType MessageType,
        ushort PayloadLength)> ReadFrameAsync(
        Stream pipe,
        byte[] header,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await pipe
            .ReadExactlyAsync(
                header,
                cancellationToken)
            .ConfigureAwait(false);

        (
            GamepadHostMessageType messageType,
            ushort payloadLength) =
            GamepadHostProtocol.DecodeHeader(
                header);

        if (payloadLength > 0)
        {
            await pipe
                .ReadExactlyAsync(
                    payload.AsMemory(
                        0,
                        payloadLength),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return (
            messageType,
            payloadLength);
    }

    private static async Task WriteEmptyFrameAsync(
        Stream pipe,
        GamepadHostMessageType messageType,
        CancellationToken cancellationToken)
    {
        byte[] frame =
            new byte[
                GamepadHostProtocol.HeaderSize];

        GamepadHostProtocol.EncodeHeader(
            frame,
            messageType,
            0);

        await pipe
            .WriteAsync(
                frame,
                cancellationToken)
            .ConfigureAwait(false);

        await pipe
            .FlushAsync(
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task TrySendErrorAsync(
        Stream pipe,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] encoded =
                Encoding.UTF8.GetBytes(
                    message);

            if (encoded.Length >
                GamepadHostProtocol.MaximumPayloadSize)
            {
                Array.Resize(
                    ref encoded,
                    GamepadHostProtocol.MaximumPayloadSize);
            }

            byte[] frame =
                new byte[
                    GamepadHostProtocol.HeaderSize +
                    encoded.Length];

            GamepadHostProtocol.EncodeHeader(
                frame,
                GamepadHostMessageType.Error,
                checked((ushort)encoded.Length));

            encoded.CopyTo(
                frame,
                GamepadHostProtocol.HeaderSize);

            await pipe
                .WriteAsync(
                    frame,
                    cancellationToken)
                .ConfigureAwait(false);

            await pipe
                .FlushAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private const uint PipeAccessDuplex =
        0x00000003;

    private const uint FileFlagOverlapped =
        0x40000000;

    private const uint PipeTypeByte =
        0x00000000;

    private const uint PipeReadModeByte =
        0x00000000;

    private const uint PipeWait =
        0x00000000;

    private const uint PipeRejectRemoteClients =
        0x00000008;

    [StructLayout(
        LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;

        [MarshalAs(
            UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "CreateNamedPipeW")]
    private static extern SafePipeHandle CreateNamedPipe(
        string name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        ref SecurityAttributes securityAttributes);

    [DllImport(
        "advapi32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true,
        EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport(
        "kernel32.dll")]
    private static extern IntPtr LocalFree(
        IntPtr memory);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        IntPtr pipe,
        out uint clientProcessId);
}
