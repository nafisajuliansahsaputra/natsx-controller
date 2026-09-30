using System.Net;
using System.Net.Sockets;
using Natsx.Controller.Connection;
using Natsx.Controller.Core;
using Natsx.Controller.Protocol;
using Xunit;

namespace Natsx.Controller.Transport.Wifi.Tests;

public sealed class WifiTrustedSessionIntegrationTests
{
    private static readonly SessionId AndroidId = SessionId.FromBytes(
        Convert.FromHexString("00112233445566778899AABBCCDDEEFF"));

    private static readonly SessionId WindowsId = SessionId.FromBytes(
        Convert.FromHexString("FFEEDDCCBBAA99887766554433221100"));

    private static readonly byte[] RootKey =
        Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task TrustedReconnect_StreamsAuthenticatedGamepadStateOverUdp()
    {
        int port = FindFreeUdpPort();

        HelloPayload receiverHello = new(
            WindowsId,
            DeviceRole.WindowsReceiver,
            TransportMask.Wifi,
            CapabilityFlags.Rumble |
                CapabilityFlags.Competitive240Hz |
                CapabilityFlags.WarmStandby,
            1,
            1,
            0,
            TrustState.Paired);

        await using var transport = new WifiControllerTransport(
            new WifiTransportOptions
            {
                BindAddress = IPAddress.Loopback,
                ListenPort = port,
            },
            handshakeFactory: () =>
                new TrustedReconnectServerHandshake(
                    WindowsId,
                    receiverHello,
                    deviceId => deviceId == AndroidId
                        ? new TrustedPeerCredentials(
                            AndroidId,
                            RootKey.ToArray())
                        : null));

        var backend = new RecordingBackend();
        var controllerSession = new ControllerSession();
        controllerSession.BeginNewSession(TransportKind.Wifi);

        var router = new WifiGamepadInputRouter(
            new InputSafetyEngine(
                controllerSession,
                backend,
                ConnectionPolicy.Competitive));

        var acceptedState =
            new TaskCompletionSource<GamepadState>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        transport.FrameReceived += (frame, _) =>
        {
            if (router.TryHandle(frame))
                acceptedState.TrySetResult(backend.LastState);
        };

        await transport.ConnectAsync(CancellationToken.None);

        using var client = new UdpClient(0);
        client.Connect(IPAddress.Loopback, port);

        HelloPayload androidHello = new(
            AndroidId,
            DeviceRole.AndroidController,
            TransportMask.Wifi,
            CapabilityFlags.Rumble |
                CapabilityFlags.Competitive240Hz |
                CapabilityFlags.WarmStandby,
            1,
            1,
            0,
            TrustState.Paired);

        byte[] androidHelloBytes =
            ControlPayloadCodec.EncodeHello(androidHello);

        await SendAsync(
            client,
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.Hello,
                FrameFlags.None,
                SessionId.Zero,
                0,
                1,
                androidHelloBytes),
            authenticationKey: null);

        ProtocolFrame windowsHelloFrame =
            ProtocolFrameCodec.Decode(
                (await client.ReceiveAsync()).Buffer);

        ProtocolFrame challengeFrame =
            ProtocolFrameCodec.Decode(
                (await client.ReceiveAsync()).Buffer);

        Assert.Equal(MessageType.Hello, windowsHelloFrame.MessageType);
        Assert.Equal(MessageType.AuthChallenge, challengeFrame.MessageType);
        Assert.NotEqual(SessionId.Zero, challengeFrame.SessionId);

        byte[] challenge =
            ControlPayloadCodec.ValidateAuthChallenge(
                challengeFrame.Payload);

        byte[] sessionKey =
            TrustedSessionCrypto.DeriveSessionKey(
                RootKey,
                challenge,
                AndroidId,
                WindowsId,
                challengeFrame.SessionId);

        byte[] proof =
            TrustedSessionCrypto.ComputeAndroidProof(
                sessionKey,
                androidHelloBytes,
                windowsHelloFrame.Payload,
                challenge,
                challengeFrame.SessionId);

        await SendAsync(
            client,
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.AuthResponse,
                FrameFlags.None,
                challengeFrame.SessionId,
                0,
                2,
                proof),
            authenticationKey: null);

        byte[] readyBytes =
            (await client.ReceiveAsync()).Buffer;

        ProtocolFrame ready =
            ProtocolFrameCodec.Decode(
                readyBytes,
                sessionKey);

        Assert.Equal(MessageType.SessionReady, ready.MessageType);
        Assert.True(
            ready.Flags.HasFlag(FrameFlags.Authenticated));

        var state = GamepadState.Neutral with
        {
            Buttons =
                GamepadButtons.A |
                GamepadButtons.RightShoulder,
            LeftX = 14500,
            LeftY = -22000,
            RightTrigger = 255,
        };

        await SendAsync(
            client,
            new ProtocolFrame(
                ProtocolVersion.Current,
                MessageType.GamepadState,
                FrameFlags.Authenticated,
                challengeFrame.SessionId,
                1,
                3,
                GamepadStateCodec.Encode(state)),
            sessionKey);

        GamepadState actual =
            await acceptedState.Task.WaitAsync(
                TimeSpan.FromSeconds(3));

        Assert.Equal(state, actual);
        Assert.True(transport.HasAuthenticatedSession);
        Assert.Equal(
            challengeFrame.SessionId,
            transport.ActiveSessionId);

        Array.Clear(sessionKey);
        Array.Clear(challenge);
        Array.Clear(proof);
    }

    private static async Task SendAsync(
        UdpClient client,
        ProtocolFrame frame,
        byte[]? authenticationKey)
    {
        byte[] bytes = authenticationKey is null
            ? ProtocolFrameCodec.Encode(frame)
            : ProtocolFrameCodec.Encode(
                frame,
                authenticationKey);

        await client.SendAsync(bytes);
    }

    private static int FindFreeUdpPort()
    {
        using var socket =
            new UdpClient(
                new IPEndPoint(
                    IPAddress.Loopback,
                    0));

        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private sealed class RecordingBackend :
        IVirtualGamepadBackend
    {
        public bool IsStarted => true;

        public GamepadState LastState { get; private set; } =
            GamepadState.Neutral;

        public event Action<RumbleState>? RumbleReceived
        {
            add { }
            remove { }
        }

        public ValueTask StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask StopAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public void Submit(GamepadState state)
        {
            LastState = state;
        }

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
