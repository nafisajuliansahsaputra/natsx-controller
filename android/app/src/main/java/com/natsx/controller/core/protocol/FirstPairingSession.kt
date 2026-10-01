package com.natsx.controller.core.protocol

import java.security.GeneralSecurityException
import java.security.MessageDigest
import java.security.SecureRandom

class FirstPairingResult(
    val remotePeerId: PeerId,
    val capabilities: Int,
    trustSecret: ByteArray,
) : AutoCloseable {
    private val secret = trustSecret.copyOf()
    private var closed = false

    init {
        require(secret.size == PairingCrypto.DERIVED_KEY_SIZE)
    }

    fun copyTrustSecret(): ByteArray {
        check(!closed)
        return secret.copyOf()
    }

    override fun close() {
        if (closed) return
        secret.fill(0)
        closed = true
    }
}

data class WindowsPairingResponse(
    val frameBytes: ByteArray,
    val sas: String,
    val androidPeerId: PeerId,
    val sessionId: SessionId,
)

data class WindowsPairingCompletion(
    val frameBytes: ByteArray,
    val result: FirstPairingResult,
)

class AndroidFirstPairingSession(
    private val localPeerId: PeerId,
    private val capabilities: Int,
) : AutoCloseable {
    private val keyAgreement =
        P256EphemeralKeyAgreement()
    private val nonce =
        ByteArray(PairingCrypto.NONCE_SIZE).also {
            SecureRandom().nextBytes(it)
        }
    private val publicKey =
        keyAgreement.exportPublicKey()

    private var windowsPeerId: PeerId? = null
    private var windowsCapabilities: Int = 0
    private var sessionId: SessionId? = null
    private var transcriptHash: ByteArray? = null
    private var pairingKey: ByteArray? = null
    private var responseAccepted = false
    private var confirmCreated = false
    private var closed = false

    init {
        require(localPeerId != PeerId.Zero)
    }

    fun createHelloFrame(
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        ensureOpen()

        return ProtocolFrameCodec.encode(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.PAIRING_HELLO,
                flags = FrameFlags.NONE,
                sessionId = SessionId.Zero,
                sequence = 0u,
                monotonicTimestampMicros =
                    monotonicTimestampMicros,
                payload =
                    PairingPayloadCodec.encodeHello(
                        PairingHelloPayload(
                            localPeerId,
                            capabilities,
                            nonce.copyOf(),
                            publicKey.copyOf(),
                        ),
                    ),
            ),
        )
    }

    fun acceptResponseFrame(
        frameBytes: ByteArray,
    ): String {
        ensureOpen()
        check(!responseAccepted)

        val frame =
            ProtocolFrameCodec.decode(frameBytes)

        if (
            frame.messageType != MessageType.PAIRING_RESPONSE ||
            frame.flags != FrameFlags.NONE ||
            frame.sessionId == SessionId.Zero
        ) {
            throw GeneralSecurityException(
                "Invalid PAIRING_RESPONSE envelope.",
            )
        }

        val response =
            PairingPayloadCodec.decodeResponse(
                frame.payload,
            )

        val sharedSecret =
            keyAgreement.deriveSharedSecret(
                response.publicKey,
            )
        var hash: ByteArray? =
            PairingCrypto.computePairingTranscriptHash(
                androidPeerId = localPeerId,
                windowsPeerId = response.windowsPeerId,
                androidNonce = nonce,
                windowsNonce = response.nonce,
                androidPublicKey = publicKey,
                windowsPublicKey = response.publicKey,
            )
        var key: ByteArray? =
            PairingCrypto.derivePairingKey(
                sharedSecret,
                hash!!,
            )
        val expectedProof =
            PairingCrypto.computePairingResponseProof(
                pairingKey = key!!,
                pairingTranscriptHash = hash,
                sessionId = frame.sessionId,
            )

        try {
            if (
                !MessageDigest.isEqual(
                    expectedProof,
                    response.responseProof,
                )
            ) {
                throw GeneralSecurityException(
                    "PAIRING_RESPONSE proof is invalid.",
                )
            }

            windowsPeerId =
                response.windowsPeerId
            windowsCapabilities =
                response.capabilities
            sessionId =
                frame.sessionId
            transcriptHash =
                hash
            pairingKey =
                key
            hash = null
            key = null
            responseAccepted = true

            return PairingCrypto.deriveSixDigitSas(
                pairingKey!!,
                transcriptHash!!,
            )
        } finally {
            sharedSecret.fill(0)
            expectedProof.fill(0)
            response.nonce.fill(0)
            response.publicKey.fill(0)
            response.responseProof.fill(0)
            hash?.fill(0)
            key?.fill(0)
        }
    }

    fun createConfirmFrame(
        userConfirmedSas: Boolean,
        monotonicTimestampMicros: ULong,
    ): ByteArray {
        ensureOpen()
        ensureResponseAccepted()

        if (!userConfirmedSas) {
            throw PairingRejectedException(
                "Android user rejected the pairing SAS.",
            )
        }

        check(!confirmCreated)

        val proof =
            PairingCrypto.computePairingConfirmationProof(
                role =
                    PairingConfirmationRole.ANDROID,
                pairingKey = pairingKey!!,
                pairingTranscriptHash =
                    transcriptHash!!,
                sessionId = sessionId!!,
            )

        return try {
            confirmCreated = true

            ProtocolFrameCodec.encode(
                ProtocolFrame(
                    version = ProtocolVersion.Current,
                    messageType =
                        MessageType.PAIRING_CONFIRM,
                    flags = FrameFlags.NONE,
                    sessionId = sessionId!!,
                    sequence = 0u,
                    monotonicTimestampMicros =
                        monotonicTimestampMicros,
                    payload =
                        PairingPayloadCodec
                            .encodeConfirmation(
                                PairingConfirmationPayload(
                                    localPeerId,
                                    proof,
                                ),
                            ),
                ),
            )
        } finally {
            proof.fill(0)
        }
    }

    fun acceptCompleteFrame(
        frameBytes: ByteArray,
    ): FirstPairingResult {
        ensureOpen()
        ensureResponseAccepted()
        check(confirmCreated)

        val frame =
            ProtocolFrameCodec.decode(frameBytes)

        if (
            frame.messageType !=
                MessageType.PAIRING_COMPLETE ||
            frame.flags != FrameFlags.NONE ||
            frame.sessionId != sessionId
        ) {
            throw GeneralSecurityException(
                "Invalid PAIRING_COMPLETE envelope.",
            )
        }

        val complete =
            PairingPayloadCodec
                .decodeConfirmation(
                    frame.payload,
                )

        val expectedWindowsPeer =
            windowsPeerId!!

        val valid =
            complete.peerId ==
                expectedWindowsPeer &&
                PairingCrypto
                    .verifyPairingConfirmationProof(
                        role =
                            PairingConfirmationRole.WINDOWS,
                        pairingKey = pairingKey!!,
                        pairingTranscriptHash =
                            transcriptHash!!,
                        sessionId = sessionId!!,
                        suppliedProof =
                            complete.proof,
                    )

        complete.proof.fill(0)

        if (!valid) {
            throw GeneralSecurityException(
                "Windows pairing confirmation is invalid.",
            )
        }

        val trustSecret =
            PairingCrypto.deriveTrustSecret(
                pairingKey!!,
                transcriptHash!!,
            )

        return try {
            FirstPairingResult(
                expectedWindowsPeer,
                windowsCapabilities,
                trustSecret,
            )
        } finally {
            trustSecret.fill(0)
        }
    }

    override fun close() {
        if (closed) return
        keyAgreement.close()
        nonce.fill(0)
        publicKey.fill(0)
        transcriptHash?.fill(0)
        pairingKey?.fill(0)
        transcriptHash = null
        pairingKey = null
        closed = true
    }

    private fun ensureResponseAccepted() {
        check(responseAccepted) {
            "PAIRING_RESPONSE must be accepted first."
        }
    }

    private fun ensureOpen() {
        check(!closed) {
            "First pairing session is closed."
        }
    }
}

class WindowsFirstPairingSession(
    private val localPeerId: PeerId,
    private val capabilities: Int,
) : AutoCloseable {
    private val keyAgreement =
        P256EphemeralKeyAgreement()
    private val nonce =
        ByteArray(PairingCrypto.NONCE_SIZE).also {
            SecureRandom().nextBytes(it)
        }
    private val publicKey =
        keyAgreement.exportPublicKey()

    private var androidPeerId: PeerId? = null
    private var androidCapabilities: Int = 0
    private var sessionId: SessionId? = null
    private var transcriptHash: ByteArray? = null
    private var pairingKey: ByteArray? = null
    private var helloAccepted = false
    private var closed = false

    init {
        require(localPeerId != PeerId.Zero)
    }

    fun acceptHelloFrame(
        frameBytes: ByteArray,
        monotonicTimestampMicros: ULong,
    ): WindowsPairingResponse {
        ensureOpen()
        check(!helloAccepted)

        val frame =
            ProtocolFrameCodec.decode(frameBytes)

        if (
            frame.messageType != MessageType.PAIRING_HELLO ||
            frame.flags != FrameFlags.NONE ||
            frame.sessionId != SessionId.Zero
        ) {
            throw GeneralSecurityException(
                "Invalid PAIRING_HELLO envelope.",
            )
        }

        val hello =
            PairingPayloadCodec.decodeHello(
                frame.payload,
            )

        val sharedSecret =
            keyAgreement.deriveSharedSecret(
                hello.publicKey,
            )
        var hash: ByteArray? =
            PairingCrypto.computePairingTranscriptHash(
                androidPeerId = hello.androidPeerId,
                windowsPeerId = localPeerId,
                androidNonce = hello.nonce,
                windowsNonce = nonce,
                androidPublicKey = hello.publicKey,
                windowsPublicKey = publicKey,
            )
        var key: ByteArray? =
            PairingCrypto.derivePairingKey(
                sharedSecret,
                hash!!,
            )
        val newSessionId =
            SessionId.createRandom()
        val responseProof =
            PairingCrypto.computePairingResponseProof(
                pairingKey = key!!,
                pairingTranscriptHash = hash,
                sessionId = newSessionId,
            )

        try {
            val responseFrame =
                ProtocolFrameCodec.encode(
                    ProtocolFrame(
                        version =
                            ProtocolVersion.Current,
                        messageType =
                            MessageType.PAIRING_RESPONSE,
                        flags = FrameFlags.NONE,
                        sessionId = newSessionId,
                        sequence = 0u,
                        monotonicTimestampMicros =
                            monotonicTimestampMicros,
                        payload =
                            PairingPayloadCodec
                                .encodeResponse(
                                    PairingResponsePayload(
                                        windowsPeerId =
                                            localPeerId,
                                        capabilities =
                                            capabilities,
                                        nonce =
                                            nonce.copyOf(),
                                        publicKey =
                                            publicKey.copyOf(),
                                        responseProof =
                                            responseProof,
                                    ),
                                ),
                    ),
                )

            androidPeerId =
                hello.androidPeerId
            androidCapabilities =
                hello.capabilities
            sessionId =
                newSessionId
            transcriptHash =
                hash
            pairingKey =
                key
            hash = null
            key = null
            helloAccepted = true

            return WindowsPairingResponse(
                frameBytes = responseFrame,
                sas =
                    PairingCrypto
                        .deriveSixDigitSas(
                            pairingKey!!,
                            transcriptHash!!,
                        ),
                androidPeerId =
                    androidPeerId!!,
                sessionId =
                    newSessionId,
            )
        } finally {
            sharedSecret.fill(0)
            responseProof.fill(0)
            hello.nonce.fill(0)
            hello.publicKey.fill(0)
            hash?.fill(0)
            key?.fill(0)
        }
    }

    fun acceptConfirmFrame(
        frameBytes: ByteArray,
        userConfirmedSas: Boolean,
        monotonicTimestampMicros: ULong,
    ): WindowsPairingCompletion {
        ensureOpen()
        check(helloAccepted)

        if (!userConfirmedSas) {
            throw PairingRejectedException(
                "Windows user rejected the pairing SAS.",
            )
        }

        val frame =
            ProtocolFrameCodec.decode(frameBytes)

        if (
            frame.messageType !=
                MessageType.PAIRING_CONFIRM ||
            frame.flags != FrameFlags.NONE ||
            frame.sessionId != sessionId
        ) {
            throw GeneralSecurityException(
                "Invalid PAIRING_CONFIRM envelope.",
            )
        }

        val confirm =
            PairingPayloadCodec
                .decodeConfirmation(
                    frame.payload,
                )

        val expectedAndroidPeer =
            androidPeerId!!

        val valid =
            confirm.peerId ==
                expectedAndroidPeer &&
                PairingCrypto
                    .verifyPairingConfirmationProof(
                        role =
                            PairingConfirmationRole.ANDROID,
                        pairingKey = pairingKey!!,
                        pairingTranscriptHash =
                            transcriptHash!!,
                        sessionId = sessionId!!,
                        suppliedProof =
                            confirm.proof,
                    )

        confirm.proof.fill(0)

        if (!valid) {
            throw GeneralSecurityException(
                "Android pairing confirmation is invalid.",
            )
        }

        val windowsProof =
            PairingCrypto.computePairingConfirmationProof(
                role =
                    PairingConfirmationRole.WINDOWS,
                pairingKey = pairingKey!!,
                pairingTranscriptHash =
                    transcriptHash!!,
                sessionId = sessionId!!,
            )
        val trustSecret =
            PairingCrypto.deriveTrustSecret(
                pairingKey!!,
                transcriptHash!!,
            )

        return try {
            val completeFrame =
                ProtocolFrameCodec.encode(
                    ProtocolFrame(
                        version =
                            ProtocolVersion.Current,
                        messageType =
                            MessageType.PAIRING_COMPLETE,
                        flags = FrameFlags.NONE,
                        sessionId = sessionId!!,
                        sequence = 0u,
                        monotonicTimestampMicros =
                            monotonicTimestampMicros,
                        payload =
                            PairingPayloadCodec
                                .encodeConfirmation(
                                    PairingConfirmationPayload(
                                        localPeerId,
                                        windowsProof,
                                    ),
                                ),
                    ),
                )

            WindowsPairingCompletion(
                frameBytes = completeFrame,
                result =
                    FirstPairingResult(
                        expectedAndroidPeer,
                        androidCapabilities,
                        trustSecret,
                    ),
            )
        } finally {
            windowsProof.fill(0)
            trustSecret.fill(0)
        }
    }

    override fun close() {
        if (closed) return
        keyAgreement.close()
        nonce.fill(0)
        publicKey.fill(0)
        transcriptHash?.fill(0)
        pairingKey?.fill(0)
        transcriptHash = null
        pairingKey = null
        closed = true
    }

    private fun ensureOpen() {
        check(!closed) {
            "First pairing session is closed."
        }
    }
}

class PairingRejectedException(
    message: String,
) : Exception(message)
