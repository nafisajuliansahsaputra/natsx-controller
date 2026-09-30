package com.natsx.controller.core.protocol

enum class TrustedReconnectClientState {
    IDLE,
    HELLO_SENT,
    WINDOWS_HELLO_RECEIVED,
    PROOF_SENT,
    ACTIVE,
}

data class EstablishedTrustedSession(
    val androidDeviceId: SessionId,
    val windowsDeviceId: SessionId,
    val sessionId: SessionId,
    val sessionKey: ByteArray,
    val negotiatedCapabilities: Int,
)

class TrustedReconnectClientHandshake(
    private val androidHello: HelloPayload,
    private val expectedWindowsDeviceId: SessionId,
    pairingRootKey: ByteArray,
    private val monotonicMicros: () -> ULong = { 0uL },
) {
    private val pairingRootKey = pairingRootKey.copyOf()
    private val androidHelloBytes = ControlPayloadCodec.encodeHello(androidHello)

    private var windowsHello: HelloPayload? = null
    private var windowsHelloBytes: ByteArray? = null
    private var challenge: ByteArray? = null
    private var sessionId: SessionId = SessionId.Zero
    private var sessionKey: ByteArray? = null

    var state: TrustedReconnectClientState = TrustedReconnectClientState.IDLE
        private set

    var establishedSession: EstablishedTrustedSession? = null
        private set

    init {
        require(androidHello.role == DeviceRole.ANDROID_CONTROLLER)
        require(androidHello.deviceId != SessionId.Zero)
        require(pairingRootKey.size == TrustedSessionCrypto.PAIRING_ROOT_KEY_SIZE)
    }

    fun createHelloFrame(): ProtocolFrame {
        check(
            state == TrustedReconnectClientState.IDLE ||
                state == TrustedReconnectClientState.HELLO_SENT,
        ) {
            "Client handshake cannot send HELLO in state " + state + "."
        }

        state = TrustedReconnectClientState.HELLO_SENT

        return ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.HELLO,
            flags = FrameFlags.NONE,
            sessionId = SessionId.Zero,
            sequence = 0u,
            monotonicTimestampMicros = monotonicMicros(),
            payload = androidHelloBytes.copyOf(),
        )
    }

    fun handleWindowsHello(frame: ProtocolFrame) {
        check(state == TrustedReconnectClientState.HELLO_SENT) {
            "Client handshake is not waiting for Windows HELLO."
        }

        requireHandshakeFrame(
            frame = frame,
            expected = MessageType.HELLO,
            authenticated = false,
        )

        require(frame.sessionId == SessionId.Zero) {
            "Windows HELLO must use zero Session ID."
        }

        val payload = ControlPayloadCodec.decodeHello(frame.payload)

        require(payload.role == DeviceRole.WINDOWS_RECEIVER) {
            "Peer HELLO does not advertise Windows receiver role."
        }

        require(payload.deviceId == expectedWindowsDeviceId) {
            "Discovered Windows identity does not match trusted receiver."
        }

        require(payload.trustState == TrustState.PAIRED) {
            "Windows receiver is not in paired trusted-reconnect mode."
        }

        require(
            payload.minimumMajor <= ProtocolVersion.Current.major &&
                payload.maximumMajor >= ProtocolVersion.Current.major,
        ) {
            "No compatible protocol major version."
        }

        windowsHello = payload
        windowsHelloBytes = frame.payload.copyOf()
        state = TrustedReconnectClientState.WINDOWS_HELLO_RECEIVED
    }

    fun handleChallenge(frame: ProtocolFrame): ProtocolFrame {
        check(state == TrustedReconnectClientState.WINDOWS_HELLO_RECEIVED) {
            "Client handshake is not waiting for AUTH_CHALLENGE."
        }

        requireHandshakeFrame(
            frame = frame,
            expected = MessageType.AUTH_CHALLENGE,
            authenticated = false,
        )

        require(frame.sessionId != SessionId.Zero) {
            "AUTH_CHALLENGE must carry a non-zero Session ID."
        }

        val currentWindowsHello =
            requireNotNull(windowsHello) {
                "Windows HELLO is missing."
            }

        val currentWindowsHelloBytes =
            requireNotNull(windowsHelloBytes) {
                "Windows HELLO payload is missing."
            }

        val challengeBytes =
            ControlPayloadCodec.validateAuthChallenge(frame.payload)

        val key = TrustedSessionCrypto.deriveSessionKey(
            pairingRootKey = pairingRootKey,
            challenge = challengeBytes,
            androidDeviceId = androidHello.deviceId,
            windowsDeviceId = currentWindowsHello.deviceId,
            sessionId = frame.sessionId,
        )

        val proof = TrustedSessionCrypto.computeAndroidProof(
            sessionKey = key,
            androidHelloPayload = androidHelloBytes,
            windowsHelloPayload = currentWindowsHelloBytes,
            challenge = challengeBytes,
            sessionId = frame.sessionId,
        )

        challenge?.fill(0)
        sessionKey?.fill(0)

        challenge = challengeBytes
        sessionKey = key
        sessionId = frame.sessionId
        state = TrustedReconnectClientState.PROOF_SENT

        return ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.AUTH_RESPONSE,
            flags = FrameFlags.NONE,
            sessionId = sessionId,
            sequence = 0u,
            monotonicTimestampMicros = monotonicMicros(),
            payload = proof,
        )
    }

    fun sessionKeyForAuthenticatedDecode(): ByteArray =
        requireNotNull(sessionKey) {
            "Session key is not available yet."
        }.copyOf()

    fun handleSessionReady(frame: ProtocolFrame): EstablishedTrustedSession {
        check(state == TrustedReconnectClientState.PROOF_SENT) {
            "Client handshake is not waiting for SESSION_READY."
        }

        requireHandshakeFrame(
            frame = frame,
            expected = MessageType.SESSION_READY,
            authenticated = true,
        )

        require(frame.sessionId == sessionId) {
            "SESSION_READY Session ID does not match active handshake."
        }

        val ready = ControlPayloadCodec.decodeSessionReady(frame.payload)

        require(ready.selectedMajor == ProtocolVersion.Current.major) {
            "Receiver selected unsupported protocol major."
        }

        val currentWindowsHello =
            requireNotNull(windowsHello)

        val key =
            requireNotNull(sessionKey) {
                "Session key is missing."
            }

        val negotiated =
            androidHello.capabilities and currentWindowsHello.capabilities

        require(ready.negotiatedCapabilities == negotiated) {
            "SESSION_READY capability negotiation does not match HELLO transcript."
        }

        val established = EstablishedTrustedSession(
            androidDeviceId = androidHello.deviceId,
            windowsDeviceId = currentWindowsHello.deviceId,
            sessionId = sessionId,
            sessionKey = key.copyOf(),
            negotiatedCapabilities = negotiated,
        )

        establishedSession = established
        state = TrustedReconnectClientState.ACTIVE
        return established
    }

    fun close() {
        pairingRootKey.fill(0)
        challenge?.fill(0)
        sessionKey?.fill(0)
        windowsHelloBytes?.fill(0)
        establishedSession?.sessionKey?.fill(0)

        challenge = null
        sessionKey = null
        windowsHelloBytes = null
        establishedSession = null
        sessionId = SessionId.Zero
        state = TrustedReconnectClientState.IDLE
    }

    private fun requireHandshakeFrame(
        frame: ProtocolFrame,
        expected: MessageType,
        authenticated: Boolean,
    ) {
        require(frame.messageType == expected) {
            "Expected " + expected + ", got " + frame.messageType + "."
        }

        val hasAuthentication =
            frame.flags and FrameFlags.AUTHENTICATED != 0

        require(hasAuthentication == authenticated) {
            expected.toString() + " authentication flag is invalid for this handshake stage."
        }
    }
}
