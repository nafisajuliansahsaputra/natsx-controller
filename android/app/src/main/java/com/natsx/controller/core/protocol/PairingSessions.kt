package com.natsx.controller.core.protocol

import java.security.GeneralSecurityException

class PairingEstablishedMaterial(
    val remotePeerId: PeerId,
    trustSecret: ByteArray,
) : AutoCloseable {
    private var secret: ByteArray? = trustSecret.copyOf()

    init {
        require(trustSecret.size == PairingCrypto.DERIVED_KEY_SIZE)
    }

    fun copyTrustSecret(): ByteArray =
        checkNotNull(secret) {
            "Pairing material is closed."
        }.copyOf()

    override fun close() {
        secret?.fill(0)
        secret = null
    }
}

class PairingInitiatorSession(
    private val localPeerId: PeerId,
) : AutoCloseable {
    private val keyAgreement =
        P256EphemeralKeyAgreement()

    private val offerInternal =
        PairingOfferPayload(
            androidPeerId = localPeerId,
            androidNonce =
                PairingPayloadCodec.createNonce(),
            androidPublicKey =
                keyAgreement.exportPublicKey(),
        )

    private var responseInternal:
        PairingResponsePayload? = null

    private var transcriptHash:
        ByteArray? = null

    private var pairingKey:
        ByteArray? = null

    private var trustSecret:
        ByteArray? = null

    private var localApproved = false
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
        get() = responseInternal?.windowsPeerId

    fun acceptResponse(
        response: PairingResponsePayload,
    ): String {
        ensureOpen()
        check(responseInternal == null) {
            "Pairing response has already been accepted."
        }

        val sharedSecret =
            keyAgreement.deriveSharedSecret(
                response.windowsPublicKey,
            )

        try {
            val hash =
                PairingCrypto
                    .computePairingTranscriptHash(
                        androidPeerId =
                            localPeerId,
                        windowsPeerId =
                            response.windowsPeerId,
                        androidNonce =
                            offerInternal.androidNonce,
                        windowsNonce =
                            response.windowsNonce,
                        androidPublicKey =
                            offerInternal.androidPublicKey,
                        windowsPublicKey =
                            response.windowsPublicKey,
                    )

            val key =
                PairingCrypto
                    .derivePairingKey(
                        ecdhSharedSecret =
                            sharedSecret,
                        pairingTranscriptHash =
                            hash,
                    )

            val secret =
                PairingCrypto
                    .deriveTrustSecret(
                        pairingKey =
                            key,
                        pairingTranscriptHash =
                            hash,
                    )

            val code =
                PairingCrypto
                    .deriveSixDigitSas(
                        pairingKey =
                            key,
                        pairingTranscriptHash =
                            hash,
                    )

            responseInternal =
                copyResponse(response)
            transcriptHash =
                hash
            pairingKey =
                key
            trustSecret =
                secret
            comparisonCode =
                code

            return code
        } finally {
            sharedSecret.fill(0)
        }
    }

    fun approveDisplayedCode():
        PairingConfirmPayload {
        ensureOpen()
        ensureContextReady()
        check(!localApproved) {
            "Pairing code has already been approved."
        }

        localApproved = true

        return PairingConfirmPayload(
            peerId = localPeerId,
            role =
                PairingRole.ANDROID_CONTROLLER,
            proof =
                PairingCrypto
                    .computePairingConfirmationProof(
                        pairingKey =
                            checkNotNull(pairingKey),
                        pairingTranscriptHash =
                            checkNotNull(transcriptHash),
                        role =
                            PairingRole
                                .ANDROID_CONTROLLER,
                    ),
        )
    }

    fun acceptRemoteConfirmation(
        confirmation: PairingConfirmPayload,
    ): PairingEstablishedMaterial {
        ensureOpen()
        ensureContextReady()

        check(localApproved) {
            "Local pairing code must be approved before completion."
        }
        check(!completed) {
            "Pairing has already completed."
        }

        val response =
            checkNotNull(responseInternal)

        if (
            confirmation.peerId !=
            response.windowsPeerId ||
            confirmation.role !=
            PairingRole.WINDOWS_RECEIVER
        ) {
            throw GeneralSecurityException(
                "Pairing confirmation identity or role does not match the Windows responder.",
            )
        }

        if (
            !PairingCrypto
                .verifyPairingConfirmationProof(
                    pairingKey =
                        checkNotNull(pairingKey),
                    pairingTranscriptHash =
                        checkNotNull(transcriptHash),
                    role =
                        PairingRole.WINDOWS_RECEIVER,
                    suppliedProof =
                        confirmation.proof,
                )
        ) {
            throw GeneralSecurityException(
                "Windows pairing confirmation proof is invalid.",
            )
        }

        completed = true

        return PairingEstablishedMaterial(
            remotePeerId =
                response.windowsPeerId,
            trustSecret =
                checkNotNull(trustSecret),
        )
    }

    override fun close() {
        if (closed) return

        keyAgreement.close()
        offerInternal.androidNonce.fill(0)
        offerInternal.androidPublicKey.fill(0)
        responseInternal
            ?.windowsNonce
            ?.fill(0)
        responseInternal
            ?.windowsPublicKey
            ?.fill(0)
        transcriptHash?.fill(0)
        pairingKey?.fill(0)
        trustSecret?.fill(0)

        responseInternal = null
        transcriptHash = null
        pairingKey = null
        trustSecret = null
        closed = true
    }

    private fun ensureContextReady() {
        check(
            responseInternal != null &&
                transcriptHash != null &&
                pairingKey != null &&
                trustSecret != null &&
                comparisonCode != null,
        ) {
            "Pairing response must be accepted before confirmation."
        }
    }

    private fun ensureOpen() {
        check(!closed) {
            "Pairing session is closed."
        }
    }
}

class PairingResponderSession(
    private val localPeerId: PeerId,
    offer: PairingOfferPayload,
) : AutoCloseable {
    private val offerInternal =
        copyOffer(offer)

    private val keyAgreement =
        P256EphemeralKeyAgreement()

    private val responseInternal =
        PairingResponsePayload(
            windowsPeerId = localPeerId,
            windowsNonce =
                PairingPayloadCodec.createNonce(),
            windowsPublicKey =
                keyAgreement.exportPublicKey(),
        )

    private var transcriptHash:
        ByteArray? = null

    private var pairingKey:
        ByteArray? = null

    private var trustSecret:
        ByteArray? = null

    private var localApproved = false
    private var completed = false
    private var closed = false

    val comparisonCode: String

    val remotePeerId: PeerId
        get() = offerInternal.androidPeerId

    init {
        val sharedSecret =
            keyAgreement.deriveSharedSecret(
                offerInternal.androidPublicKey,
            )

        try {
            val hash =
                PairingCrypto
                    .computePairingTranscriptHash(
                        androidPeerId =
                            offerInternal.androidPeerId,
                        windowsPeerId =
                            localPeerId,
                        androidNonce =
                            offerInternal.androidNonce,
                        windowsNonce =
                            responseInternal.windowsNonce,
                        androidPublicKey =
                            offerInternal.androidPublicKey,
                        windowsPublicKey =
                            responseInternal.windowsPublicKey,
                    )

            val key =
                PairingCrypto
                    .derivePairingKey(
                        ecdhSharedSecret =
                            sharedSecret,
                        pairingTranscriptHash =
                            hash,
                    )

            transcriptHash =
                hash
            pairingKey =
                key
            trustSecret =
                PairingCrypto
                    .deriveTrustSecret(
                        pairingKey =
                            key,
                        pairingTranscriptHash =
                            hash,
                    )
            comparisonCode =
                PairingCrypto
                    .deriveSixDigitSas(
                        pairingKey =
                            key,
                        pairingTranscriptHash =
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

    fun approveDisplayedCode():
        PairingConfirmPayload {
        ensureOpen()
        check(!localApproved) {
            "Pairing code has already been approved."
        }

        localApproved = true

        return PairingConfirmPayload(
            peerId = localPeerId,
            role =
                PairingRole.WINDOWS_RECEIVER,
            proof =
                PairingCrypto
                    .computePairingConfirmationProof(
                        pairingKey =
                            checkNotNull(pairingKey),
                        pairingTranscriptHash =
                            checkNotNull(transcriptHash),
                        role =
                            PairingRole
                                .WINDOWS_RECEIVER,
                    ),
        )
    }

    fun acceptRemoteConfirmation(
        confirmation: PairingConfirmPayload,
    ): PairingEstablishedMaterial {
        ensureOpen()
        check(localApproved) {
            "Local pairing code must be approved before completion."
        }
        check(!completed) {
            "Pairing has already completed."
        }

        if (
            confirmation.peerId !=
            offerInternal.androidPeerId ||
            confirmation.role !=
            PairingRole.ANDROID_CONTROLLER
        ) {
            throw GeneralSecurityException(
                "Pairing confirmation identity or role does not match the Android initiator.",
            )
        }

        if (
            !PairingCrypto
                .verifyPairingConfirmationProof(
                    pairingKey =
                        checkNotNull(pairingKey),
                    pairingTranscriptHash =
                        checkNotNull(transcriptHash),
                    role =
                        PairingRole.ANDROID_CONTROLLER,
                    suppliedProof =
                        confirmation.proof,
                )
        ) {
            throw GeneralSecurityException(
                "Android pairing confirmation proof is invalid.",
            )
        }

        completed = true

        return PairingEstablishedMaterial(
            remotePeerId =
                offerInternal.androidPeerId,
            trustSecret =
                checkNotNull(trustSecret),
        )
    }

    override fun close() {
        if (closed) return

        keyAgreement.close()
        offerInternal.androidNonce.fill(0)
        offerInternal.androidPublicKey.fill(0)
        responseInternal.windowsNonce.fill(0)
        responseInternal.windowsPublicKey.fill(0)
        transcriptHash?.fill(0)
        pairingKey?.fill(0)
        trustSecret?.fill(0)

        transcriptHash = null
        pairingKey = null
        trustSecret = null
        closed = true
    }

    private fun ensureOpen() {
        check(!closed) {
            "Pairing session is closed."
        }
    }
}

private fun copyOffer(
    value: PairingOfferPayload,
): PairingOfferPayload =
    PairingOfferPayload(
        androidPeerId =
            value.androidPeerId,
        androidNonce =
            value.androidNonce.copyOf(),
        androidPublicKey =
            value.androidPublicKey.copyOf(),
    )

private fun copyResponse(
    value: PairingResponsePayload,
): PairingResponsePayload =
    PairingResponsePayload(
        windowsPeerId =
            value.windowsPeerId,
        windowsNonce =
            value.windowsNonce.copyOf(),
        windowsPublicKey =
            value.windowsPublicKey.copyOf(),
    )
