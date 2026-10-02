using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Natsx.Controller.Core;
using Natsx.Controller.VirtualGamepad;

namespace Natsx.Controller.GamepadHost;

internal sealed class GamepadHostServer
{
    private readonly string _expectedReceiverPath =
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "Natsx.Controller.Receiver.exe"));

    public async Task RunAsync(
        CancellationToken cancellationToken)
    {
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

        await using var backend =
            new HidMaestroVirtualGamepadBackend();

        try
        {
            await backend
                .StartAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await TrySendErrorAsync(
                    pipe,
                    "Unable to create the privileged Xbox 360 virtual controller.",
                    cancellationToken)
                .ConfigureAwait(false);

            throw new InvalidOperationException(
                "HIDMaestro virtual-controller startup failed.",
                exception);
        }

        object writeGate =
            new();

        backend.RumbleReceived +=
            rumble =>
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
            };

        await WriteEmptyFrameAsync(
                pipe,
                GamepadHostMessageType.Ready,
                cancellationToken)
            .ConfigureAwait(false);

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
            }
        }
        finally
        {
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
        var security =
            new PipeSecurity();

        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(
                    WellKnownSidType.LocalSystemSid,
                    null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(
                    WellKnownSidType.BuiltinAdministratorsSid,
                    null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

        security.AddAccessRule(
            new PipeAccessRule(
                new SecurityIdentifier(
                    WellKnownSidType.AuthenticatedUserSid,
                    null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            GamepadHostProtocol.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous |
            PipeOptions.WriteThrough,
            0,
            0,
            security);
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

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        IntPtr pipe,
        out uint clientProcessId);
}
