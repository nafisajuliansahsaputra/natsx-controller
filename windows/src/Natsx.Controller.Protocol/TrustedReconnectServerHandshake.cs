using System.Security.Cryptography;

namespace Natsx.Controller.Protocol;

public enum TrustedReconnectServerState
{
    WaitingHello,
    ChallengeSent,
    Authenticated,
}

public sealed record TrustedPeerCredentials(
    SessionId DeviceId,
    byte[] PairingRootKey);

public sealed record EstablishedTrustedSession(
    SessionId AndroidDeviceId,
    SessionId WindowsDeviceId,
    SessionId SessionId,
    byte[] SessionKey,
    CapabilityFlags NegotiatedCapabilities);

public sealed class TrustedReconnectServerHandshake
{
    private readonly SessionId _windowsDeviceId;
    private readonly HelloPayload _windowsHello;
    private readonly Func<SessionId, TrustedPeerCredentials?> _trustResolver;
    private readonly Func<int, byte[]> _randomBytes;
    private readonly Func<ulong> _monotonicMicros;

    private HelloPayload? _androidHello;
    private byte[]? _androidHelloBytes;
    private byte[]? _windowsHelloBytes;
    private byte[]? _challenge;
    private byte[]? _sessionKey;
    private SessionId _sessionId;

    public TrustedReconnectServerHandshake(
        SessionId windowsDeviceId,
        HelloPayload windowsHello,
        Func<SessionId, TrustedPeerCredentials?> trustResolver,
        Func<int, byte[]>? randomBytes = null,
        Func<ulong>? monotonicMicros = null)
    {
        if (windowsHello.DeviceId != windowsDeviceId)
            throw new ArgumentException("Windows HELLO device ID does not match receiver identity.", nameof(windowsHello));

        if (windowsHello.Role != DeviceRole.WindowsReceiver)
            throw new ArgumentException("Windows HELLO must advertise WindowsReceiver role.", nameof(windowsHello));

        _windowsDeviceId = windowsDeviceId;
        _windowsHello = windowsHello;
        _trustResolver = trustResolver ?? throw new ArgumentNullException(nameof(trustResolver));
        _randomBytes = randomBytes ?? RandomNumberGenerator.GetBytes;
        _monotonicMicros = monotonicMicros ?? (() => 0);
    }

    public TrustedReconnectServerState State { get; private set; } =
        TrustedReconnectServerState.WaitingHello;

    public EstablishedTrustedSession? EstablishedSession { get; private set; }

    public IReadOnlyList<ProtocolFrame> HandleHello(ProtocolFrame frame)
    {
        if (State != TrustedReconnectServerState.WaitingHello)
            throw new InvalidOperationException("Server handshake is not waiting for HELLO.");

        RequireFrame(frame, MessageType.Hello, authenticated: false);

        if (frame.SessionId != SessionId.Zero)
            throw new FormatException("HELLO must use zero Session ID.");

        HelloPayload androidHello = ControlPayloadCodec.DecodeHello(frame.Payload);

        if (androidHello.Role != DeviceRole.AndroidController)
            throw new FormatException("Peer HELLO does not advertise Android controller role.");

        if (androidHello.TrustState != TrustState.Paired)
            throw new UnauthorizedAccessException("Peer is not in paired trusted-reconnect mode.");

        if (androidHello.MinimumMajor > ProtocolVersion.Current.Major ||
            androidHello.MaximumMajor < ProtocolVersion.Current.Major)
        {
            throw new FormatException("No compatible protocol major version.");
        }

        TrustedPeerCredentials trust =
            _trustResolver(androidHello.DeviceId)
            ?? throw new UnauthorizedAccessException("Android peer is not trusted.");

        if (trust.DeviceId != androidHello.DeviceId)
            throw new InvalidOperationException("Trust resolver returned mismatched device identity.");

        if (trust.PairingRootKey.Length != TrustedSessionCrypto.PairingRootKeySize)
            throw new InvalidOperationException("Trusted peer root key has an invalid length.");

        _androidHello = androidHello;
        _androidHelloBytes = frame.Payload.ToArray();
        _windowsHelloBytes = ControlPayloadCodec.EncodeHello(_windowsHello);
        _challenge = _randomBytes(TrustedSessionCrypto.ChallengeSize);

        if (_challenge.Length != TrustedSessionCrypto.ChallengeSize)
            throw new InvalidOperationException("Random provider returned an invalid challenge length.");

        _sessionId = CreateNonZeroSessionId();

        _sessionKey = TrustedSessionCrypto.DeriveSessionKey(
            trust.PairingRootKey,
            _challenge,
            androidHello.DeviceId,
            _windowsDeviceId,
            _sessionId);

        CapabilityFlags negotiated =
            androidHello.Capabilities & _windowsHello.Capabilities;

        var helloReply = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.Hello,
            FrameFlags.None,
            SessionId.Zero,
            0,
            _monotonicMicros(),
            _windowsHelloBytes.ToArray());

        var challengeFrame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.AuthChallenge,
            FrameFlags.None,
            _sessionId,
            0,
            _monotonicMicros(),
            ControlPayloadCodec.ValidateAuthChallenge(_challenge));

        EstablishedSession = new EstablishedTrustedSession(
            androidHello.DeviceId,
            _windowsDeviceId,
            _sessionId,
            _sessionKey.ToArray(),
            negotiated);

        State = TrustedReconnectServerState.ChallengeSent;

        return new[] { helloReply, challengeFrame };
    }

    public ProtocolFrame HandleAuthResponse(ProtocolFrame frame, byte currentTransport)
    {
        if (State != TrustedReconnectServerState.ChallengeSent)
            throw new InvalidOperationException("Server handshake is not waiting for AUTH_RESPONSE.");

        RequireFrame(frame, MessageType.AuthResponse, authenticated: false);

        if (frame.SessionId != _sessionId)
            throw new UnauthorizedAccessException("AUTH_RESPONSE Session ID does not match challenge.");

        if (_sessionKey is null ||
            _challenge is null ||
            _androidHelloBytes is null ||
            _windowsHelloBytes is null ||
            EstablishedSession is null)
        {
            throw new InvalidOperationException("Handshake state is incomplete.");
        }

        byte[] suppliedProof = ControlPayloadCodec.ValidateAuthResponse(frame.Payload);

        byte[] expectedProof = TrustedSessionCrypto.ComputeAndroidProof(
            _sessionKey,
            _androidHelloBytes,
            _windowsHelloBytes,
            _challenge,
            _sessionId);

        bool valid = TrustedSessionCrypto.VerifyAndroidProof(
            expectedProof,
            suppliedProof);

        CryptographicOperations.ZeroMemory(expectedProof);
        CryptographicOperations.ZeroMemory(suppliedProof);

        if (!valid)
        {
            ResetSecrets();
            State = TrustedReconnectServerState.WaitingHello;
            EstablishedSession = null;
            throw new UnauthorizedAccessException("Trusted reconnect proof validation failed.");
        }

        byte[] readyPayload = ControlPayloadCodec.EncodeSessionReady(
            new SessionReadyPayload(
                ProtocolVersion.Current.Major,
                ProtocolVersion.Current.Minor,
                currentTransport,
                EstablishedSession.NegotiatedCapabilities));

        var readyFrame = new ProtocolFrame(
            ProtocolVersion.Current,
            MessageType.SessionReady,
            FrameFlags.Authenticated,
            _sessionId,
            0,
            _monotonicMicros(),
            readyPayload);

        State = TrustedReconnectServerState.Authenticated;
        return readyFrame;
    }

    public byte[] GetSessionKey()
    {
        if (_sessionKey is null)
            throw new InvalidOperationException("Session key is not available.");

        return _sessionKey.ToArray();
    }

    public void Reset()
    {
        ResetSecrets();
        _androidHello = null;
        _androidHelloBytes = null;
        _windowsHelloBytes = null;
        _sessionId = SessionId.Zero;
        EstablishedSession = null;
        State = TrustedReconnectServerState.WaitingHello;
    }

    private SessionId CreateNonZeroSessionId()
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            byte[] bytes = _randomBytes(16);

            if (bytes.Length != 16)
                throw new InvalidOperationException("Random provider returned an invalid Session ID length.");

            SessionId sessionId = SessionId.FromBytes(bytes);

            if (sessionId != SessionId.Zero)
                return sessionId;
        }

        throw new CryptographicException("Unable to generate a non-zero Session ID.");
    }

    private static void RequireFrame(
        ProtocolFrame frame,
        MessageType expected,
        bool authenticated)
    {
        if (frame.MessageType != expected)
            throw new FormatException($"Expected {expected}, got {frame.MessageType}.");

        bool hasAuthentication = frame.Flags.HasFlag(FrameFlags.Authenticated);

        if (hasAuthentication != authenticated)
            throw new FormatException($"{expected} authentication flag is invalid for this handshake stage.");
    }

    private void ResetSecrets()
    {
        if (_challenge is not null)
            CryptographicOperations.ZeroMemory(_challenge);

        if (_sessionKey is not null)
            CryptographicOperations.ZeroMemory(_sessionKey);

        _challenge = null;
        _sessionKey = null;
    }
}
