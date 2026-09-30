package com.natsx.controller.core.protocol

class DynamicTrustedReconnectClientHandshake(
    private val androidHello: HelloPayload,
    private val trustResolver: (SessionId) -> ByteArray?,
    private val monotonicMicros: () -> ULong = { 0uL },
) {
    private val androidHelloBytes =
        ControlPayloadCodec.encodeHello(androidHello)

    private var windowsHello: HelloPayload? = null
    private var windowsHelloBytes: ByteArray? = null
    private var challenge: ByteArray? = null
    private var pairingRootKey: ByteArray? = null
    private var sessionKey: ByteArray? = null
    private var sessionId = SessionId.Zero

    var state: TrustedReconnectClientState =
        TrustedReconnectClientState.IDLE
        private set

    init {
        require(
            androidHello.role ==
                DeviceRole.ANDROID_CONTROLLER,
        )
        require(androidHello.deviceId != SessionId.Zero)
    }

    fun createHelloFrame(): ProtocolFrame {
        check(
            state ==
                TrustedReconnectClientState.IDLE,
        )

        state =
            TrustedReconnectClientState.HELLO_SENT

        return ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.HELLO,
            flags = FrameFlags.NONE,
            sessionId = SessionId.Zero,
            sequence = 0u,
            monotonicTimestampMicros =
                monotonicMicros(),
            payload = androidHelloBytes.copyOf(),
        )
    }

    fun handleWindowsHello(
        frame: ProtocolFrame,
    ): SessionId {
        check(
            state ==
                TrustedReconnectClientState.HELLO_SENT,
        )

        requireHandshakeFrame(
            frame,
            MessageType.HELLO,
            authenticated = false,
        )

        require(frame.sessionId == SessionId.Zero)

        val payload =
            ControlPayloadCodec.decodeHello(
                frame.payload,
            )

        require(
            payload.role ==
                DeviceRole.WINDOWS_RECEIVER,
        )
        require(
            payload.trustState ==
                TrustState.PAIRED,
        )
        require(
            payload.minimumMajor <=
                ProtocolVersion.Current.major &&
                payload.maximumMajor >=
                ProtocolVersion.Current.major,
        )

        val rootKey =
            trustResolver(payload.deviceId)
                ?: throw SecurityException(
                    "Windows receiver is not trusted.",
                )

        require(
            rootKey.size ==
                TrustedSessionCrypto.PAIRING_ROOT_KEY_SIZE,
        ) {
            rootKey.fill(0)
            "Trusted receiver root key has invalid length."
        }

        pairingRootKey?.fill(0)
        pairingRootKey = rootKey.copyOf()
        rootKey.fill(0)

        windowsHello = payload
        windowsHelloBytes =
            frame.payload.copyOf()

        state =
            TrustedReconnectClientState
                .WINDOWS_HELLO_RECEIVED

        return payload.deviceId
    }

    fun handleChallenge(
        frame: ProtocolFrame,
    ): ProtocolFrame {
        check(
            state ==
                TrustedReconnectClientState
                    .WINDOWS_HELLO_RECEIVED,
        )

        requireHandshakeFrame(
            frame,
            MessageType.AUTH_CHALLENGE,
            authenticated = false,
        )

        require(frame.sessionId != SessionId.Zero)

        val receiver =
            requireNotNull(windowsHello)

        val receiverHelloBytes =
            requireNotNull(windowsHelloBytes)

        val rootKey =
            requireNotNull(pairingRootKey)

        val challengeBytes =
            ControlPayloadCodec
                .validateAuthChallenge(
                    frame.payload,
                )

        val key =
            TrustedSessionCrypto
                .deriveSessionKey(
                    pairingRootKey = rootKey,
                    challenge = challengeBytes,
                    androidDeviceId =
                        androidHello.deviceId,
                    windowsDeviceId =
                        receiver.deviceId,
                    sessionId = frame.sessionId,
                )

        val proof =
            TrustedSessionCrypto
                .computeAndroidProof(
                    sessionKey = key,
                    androidHelloPayload =
                        androidHelloBytes,
                    windowsHelloPayload =
                        receiverHelloBytes,
                    challenge = challengeBytes,
                    sessionId = frame.sessionId,
                )

        challenge?.fill(0)
        sessionKey?.fill(0)

        challenge = challengeBytes
        sessionKey = key
        sessionId = frame.sessionId

        state =
            TrustedReconnectClientState.PROOF_SENT

        return ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.AUTH_RESPONSE,
            flags = FrameFlags.NONE,
            sessionId = sessionId,
            sequence = 0u,
            monotonicTimestampMicros =
                monotonicMicros(),
            payload = proof,
        )
    }

    fun sessionKeyForAuthenticatedDecode():
        ByteArray =
        requireNotNull(sessionKey).copyOf()

    fun handleSessionReady(
        frame: ProtocolFrame,
    ): EstablishedTrustedSession {
        check(
            state ==
                TrustedReconnectClientState.PROOF_SENT,
        )

        requireHandshakeFrame(
            frame,
            MessageType.SESSION_READY,
            authenticated = true,
        )

        require(frame.sessionId == sessionId)

        val ready =
            ControlPayloadCodec
                .decodeSessionReady(
                    frame.payload,
                )

        require(
            ready.selectedMajor ==
                ProtocolVersion.Current.major,
        )

        val receiver =
            requireNotNull(windowsHello)

        val key =
            requireNotNull(sessionKey)

        val negotiated =
            androidHello.capabilities and
                receiver.capabilities

        require(
            ready.negotiatedCapabilities ==
                negotiated,
        )

        state =
            TrustedReconnectClientState.ACTIVE

        return EstablishedTrustedSession(
            androidDeviceId =
                androidHello.deviceId,
            windowsDeviceId =
                receiver.deviceId,
            sessionId = sessionId,
            sessionKey = key.copyOf(),
            negotiatedCapabilities = negotiated,
        )
    }

    fun close() {
        pairingRootKey?.fill(0)
        challenge?.fill(0)
        sessionKey?.fill(0)
        windowsHelloBytes?.fill(0)

        pairingRootKey = null
        challenge = null
        sessionKey = null
        windowsHelloBytes = null
        windowsHello = null
        sessionId = SessionId.Zero

        state =
            TrustedReconnectClientState.IDLE
    }

    private fun requireHandshakeFrame(
        frame: ProtocolFrame,
        type: MessageType,
        authenticated: Boolean,
    ) {
        require(frame.messageType == type)

        val hasAuthentication =
            frame.flags and
                FrameFlags.AUTHENTICATED != 0

        require(
            hasAuthentication ==
                authenticated,
        )
    }
}
