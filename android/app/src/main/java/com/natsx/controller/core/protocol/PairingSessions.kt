package com.natsx.controller.core.protocol

import java.security.GeneralSecurityException

class PairingEstablishedMaterial(
    val remotePeerId: PeerId,
    trustKey: ByteArray,
) : AutoCloseable {
    private var key: ByteArray? = trustKey.copyOf()

    init {
        require(trustKey.size == PairingCrypto.TRUST_KEY_SIZE) {
            "Trust key must be exactly ${PairingCrypto.TRUST_KEY_SIZE} bytes."
        }
    }

    fun exportTrustKey(): ByteArray =
        checkNotNull(key) { "Pairing material is closed." }.copyOf()

    override fun close() {
        key?.fill(0)
        key = null
    }
}

class PairingInitiatorSession(
    private val localPeerId: PeerId,
) : AutoCloseable {
    private val keyAgreement = PairingKeyAgreement()
    private val offerInternal = PairingOfferPayload(
        initiatorPeerId = localPeerId,
        initiatorNonce = PairingExchangePayloadCodec.createNonce(),
        initiatorPublicKey = keyAgreement.publicKey.copyOf(),
    )

    private var responseInternal: PairingResponsePayload? = null
    private var transcriptHash: ByteArray? = null
    private var trustKey: ByteArray? = null
    private var approved = false
    private var completed = false
    private var closed = false

    var comparisonCode: String? = null
        private set

    val offer: PairingOfferPayload
        get() {
            ensureOpen()
            return copyOffer(offerInternal)
        }

    val remotePeerId: PeerId?
        get() = responseInternal?.responderPeerId

    fun acceptResponse(response: PairingResponsePayload): String {
        ensureOpen()
        check(responseInternal == null) {
            "Pairing response has already been accepted."
        }

        val sharedSecret =
            keyAgreement.deriveSharedSecret(response.responderPublicKey)

        try {
            val hash =
                PairingCrypto.computeTranscriptHash(
                    offerInternal,
                    response,
                )
            val key =
                PairingCrypto.deriveTrustKey(sharedSecret, hash)
            val code =
                PairingCrypto.deriveSixDigitCode(sharedSecret, hash)

            responseInternal = copyResponse(response)
            transcriptHash = hash
            trustKey = key
            comparisonCode = code
            return code
        } finally {
            sharedSecret.fill(0)
        }
    }

    fun approveDisplayedCode(): PairingConfirmPayload {
        ensureOpen()
        ensureContextReady()
        check(!approved) {
            "Pairing code has already been approved."
        }

        approved = true

        val proof = PairingCrypto.createConfirmationProof(
            trustKey = checkNotNull(trustKey),
            transcriptHash = checkNotNull(transcriptHash),
            role = PairingRole.INITIATOR,
        )

        return PairingConfirmPayload(
            peerId = localPeerId,
            role = PairingRole.INITIATOR,
            proof = proof,
        )
    }

    fun acceptRemoteConfirmation(
        confirmation: PairingConfirmPayload,
    ): PairingEstablishedMaterial {
        ensureOpen()
        ensureContextReady()
        check(approved) {
            "Local pairing code must be approved before completing pairing."
        }
        check(!completed) {
            "Pairing has already completed."
        }

        val response = checkNotNull(responseInternal)

        if (
            confirmation.peerId != response.responderPeerId ||
            confirmation.role != PairingRole.RESPONDER
        ) {
            throw GeneralSecurityException(
                "Pairing confirmation identity or role does not match the responder.",
            )
        }

        val valid = PairingCrypto.verifyConfirmationProof(
            trustKey = checkNotNull(trustKey),
            transcriptHash = checkNotNull(transcriptHash),
            role = PairingRole.RESPONDER,
            suppliedProof = confirmation.proof,
        )

        if (!valid) {
            throw GeneralSecurityException(
                "Responder pairing confirmation proof is invalid.",
            )
        }

        completed = true
        return PairingEstablishedMaterial(
            remotePeerId = response.responderPeerId,
            trustKey = checkNotNull(trustKey),
        )
    }

    override fun close() {
        if (closed) return

        keyAgreement.close()
        transcriptHash?.fill(0)
        trustKey?.fill(0)
        offerInternal.initiatorNonce.fill(0)
        offerInternal.initiatorPublicKey.fill(0)
        responseInternal?.responderNonce?.fill(0)
        responseInternal?.responderPublicKey?.fill(0)

        transcriptHash = null
        trustKey = null
        closed = true
    }

    private fun ensureContextReady() {
        check(
            responseInternal != null &&
                transcriptHash != null &&
                trustKey != null &&
                comparisonCode != null,
        ) {
            "Pairing response must be accepted before confirmation."
        }
    }

    private fun ensureOpen() {
        check(!closed) { "Pairing session is closed." }
    }
}

class PairingResponderSession(
    private val localPeerId: PeerId,
    offer: PairingOfferPayload,
) : AutoCloseable {
    private val offerInternal = copyOffer(offer)
    private val keyAgreement = PairingKeyAgreement()
    private val responseInternal = PairingResponsePayload(
        responderPeerId = localPeerId,
        responderNonce = PairingExchangePayloadCodec.createNonce(),
        responderPublicKey = keyAgreement.publicKey.copyOf(),
    )

    private var transcriptHash: ByteArray?
    private var trustKey: ByteArray?
    private var approved = false
    private var completed = false
    private var closed = false

    val comparisonCode: String
    val remotePeerId: PeerId
        get() = offerInternal.initiatorPeerId

    init {
        val sharedSecret =
            keyAgreement.deriveSharedSecret(
                offerInternal.initiatorPublicKey,
            )

        try {
            val hash =
                PairingCrypto.computeTranscriptHash(
                    offerInternal,
                    responseInternal,
                )

            transcriptHash = hash
            trustKey =
                PairingCrypto.deriveTrustKey(
                    sharedSecret,
                    hash,
                )
            comparisonCode =
                PairingCrypto.deriveSixDigitCode(
                    sharedSecret,
                    hash,
                )
        } finally {
            sharedSecret.fill(0)
        }
    }

    val response: PairingResponsePayload
        get() {
            ensureOpen()
            return copyResponse(responseInternal)
        }

    fun approveDisplayedCode(): PairingConfirmPayload {
        ensureOpen()
        check(!approved) {
            "Pairing code has already been approved."
        }

        approved = true

        val proof = PairingCrypto.createConfirmationProof(
            trustKey = checkNotNull(trustKey),
            transcriptHash = checkNotNull(transcriptHash),
            role = PairingRole.RESPONDER,
        )

        return PairingConfirmPayload(
            peerId = localPeerId,
            role = PairingRole.RESPONDER,
            proof = proof,
        )
    }

    fun acceptRemoteConfirmation(
        confirmation: PairingConfirmPayload,
    ): PairingEstablishedMaterial {
        ensureOpen()
        check(approved) {
            "Local pairing code must be approved before completing pairing."
        }
        check(!completed) {
            "Pairing has already completed."
        }

        if (
            confirmation.peerId != offerInternal.initiatorPeerId ||
            confirmation.role != PairingRole.INITIATOR
        ) {
            throw GeneralSecurityException(
                "Pairing confirmation identity or role does not match the initiator.",
            )
        }

        val valid = PairingCrypto.verifyConfirmationProof(
            trustKey = checkNotNull(trustKey),
            transcriptHash = checkNotNull(transcriptHash),
            role = PairingRole.INITIATOR,
            suppliedProof = confirmation.proof,
        )

        if (!valid) {
            throw GeneralSecurityException(
                "Initiator pairing confirmation proof is invalid.",
            )
        }

        completed = true
        return PairingEstablishedMaterial(
            remotePeerId = offerInternal.initiatorPeerId,
            trustKey = checkNotNull(trustKey),
        )
    }

    override fun close() {
        if (closed) return

        keyAgreement.close()
        transcriptHash?.fill(0)
        trustKey?.fill(0)
        offerInternal.initiatorNonce.fill(0)
        offerInternal.initiatorPublicKey.fill(0)
        responseInternal.responderNonce.fill(0)
        responseInternal.responderPublicKey.fill(0)

        transcriptHash = null
        trustKey = null
        closed = true
    }

    private fun ensureOpen() {
        check(!closed) { "Pairing session is closed." }
    }
}

private fun copyOffer(value: PairingOfferPayload): PairingOfferPayload =
    PairingOfferPayload(
        initiatorPeerId = value.initiatorPeerId,
        initiatorNonce = value.initiatorNonce.copyOf(),
        initiatorPublicKey = value.initiatorPublicKey.copyOf(),
    )

private fun copyResponse(
    value: PairingResponsePayload,
): PairingResponsePayload =
    PairingResponsePayload(
        responderPeerId = value.responderPeerId,
        responderNonce = value.responderNonce.copyOf(),
        responderPublicKey = value.responderPublicKey.copyOf(),
    )
