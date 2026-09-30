package com.natsx.controller.pairing

import android.os.Build
import com.natsx.controller.core.protocol.PairingCodec
import com.natsx.controller.core.protocol.PairingConfirmPayload
import com.natsx.controller.core.protocol.PairingCrypto
import com.natsx.controller.core.protocol.PairingHelloPayload
import com.natsx.controller.core.protocol.PairingMessageType
import com.natsx.controller.core.protocol.PairingRole
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.security.AndroidTrustedReceiverStore
import com.natsx.controller.transport.wifi.DiscoveredReceiver
import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.MessageDigest
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

data class AndroidPairingPrompt(
    val sasCode: String,
    val receiverName: String,
    val windowsDeviceId: SessionId,
    val hostAddress: String,
)

class AndroidPairingClient(
    private val androidDeviceId: SessionId,
    private val trustedReceivers: AndroidTrustedReceiverStore,
) : AutoCloseable {
    private val running = AtomicBoolean(false)

    @Volatile
    private var worker: Thread? = null

    @Volatile
    private var activeSocket: Socket? = null

    @Volatile
    private var decisionLatch: CountDownLatch? = null

    @Volatile
    private var decision: Boolean? = null

    @Volatile
    var onPromptReady: ((AndroidPairingPrompt) -> Unit)? = null

    @Volatile
    var onCompleted: ((AndroidPairingPrompt) -> Unit)? = null

    @Volatile
    var onFailed: ((String) -> Unit)? = null

    fun start(receiver: DiscoveredReceiver) {
        if (!running.compareAndSet(false, true)) {
            return
        }

        decision = null
        decisionLatch = CountDownLatch(1)

        worker = Thread(
            {
                runPairing(receiver)
            },
            "natsx-pairing-client",
        ).apply {
            isDaemon = true
            start()
        }
    }

    fun confirm() {
        decision = true
        decisionLatch?.countDown()
    }

    fun cancel() {
        decision = false
        decisionLatch?.countDown()
        activeSocket?.close()
    }

    override fun close() {
        if (!running.getAndSet(false)) {
            return
        }

        decision = false
        decisionLatch?.countDown()
        activeSocket?.close()
        activeSocket = null

        worker?.interrupt()
        worker = null
    }

    private fun runPairing(receiver: DiscoveredReceiver) {
        var pairingRootKey: ByteArray? = null
        var transcriptHash: ByteArray? = null

        try {
            Socket().use { socket ->
                activeSocket = socket
                socket.tcpNoDelay = true
                socket.soTimeout = PAIRING_TIMEOUT_MS
                socket.connect(
                    InetSocketAddress(
                        receiver.address,
                        PAIRING_PORT,
                    ),
                    CONNECT_TIMEOUT_MS,
                )

                val keyPair =
                    PairingCrypto.createEphemeralKeyPair()

                val requestBytes =
                    PairingCodec.encodeRequest(
                        PairingHelloPayload(
                            deviceId = androidDeviceId,
                            publicKeyDer =
                                PairingCrypto.exportPublicKey(
                                    keyPair,
                                ),
                            displayName = Build.MODEL
                                .take(
                                    PairingCodec
                                        .MAXIMUM_DISPLAY_NAME_BYTES,
                                ),
                        ),
                    )

                writeMessage(
                    socket.getOutputStream(),
                    requestBytes,
                )

                val responseBytes =
                    readMessage(socket.getInputStream())

                val response =
                    PairingCodec.decodeResponse(
                        responseBytes,
                    )

                transcriptHash =
                    PairingCrypto.computeTranscriptHash(
                        requestBytes,
                        responseBytes,
                    )

                pairingRootKey =
                    PairingCrypto.derivePairingRootKey(
                        localEphemeralKey = keyPair,
                        peerPublicKeyDer =
                            response.publicKeyDer,
                        transcriptHash =
                            requireNotNull(transcriptHash),
                    )

                response.publicKeyDer.fill(0)

                val prompt = AndroidPairingPrompt(
                    sasCode =
                        PairingCrypto.computeSas(
                            requireNotNull(pairingRootKey),
                            requireNotNull(transcriptHash),
                        ),
                    receiverName =
                        response.displayName.ifBlank {
                            receiver.response.receiverName
                        },
                    windowsDeviceId = response.deviceId,
                    hostAddress =
                        receiver.address.hostAddress.orEmpty(),
                )

                onPromptReady?.invoke(prompt)

                val latch =
                    requireNotNull(decisionLatch)

                val gotDecision =
                    latch.await(
                        PAIRING_TIMEOUT_MS.toLong(),
                        TimeUnit.MILLISECONDS,
                    )

                if (!gotDecision || decision != true) {
                    runCatching {
                        writeMessage(
                            socket.getOutputStream(),
                            PairingCodec.encodeCancel(1),
                        )
                    }
                    throw PairingCancelledException()
                }

                val androidTag =
                    PairingCrypto.computeConfirmationTag(
                        PairingRole.ANDROID,
                        requireNotNull(pairingRootKey),
                        requireNotNull(transcriptHash),
                    )

                try {
                    writeMessage(
                        socket.getOutputStream(),
                        PairingCodec.encodeConfirm(
                            PairingConfirmPayload(
                                PairingRole.ANDROID,
                                androidTag,
                            ),
                        ),
                    )
                } finally {
                    androidTag.fill(0)
                }

                val windowsConfirmBytes =
                    readMessage(socket.getInputStream())

                if (PairingCodec.peekType(
                        windowsConfirmBytes,
                    ) == PairingMessageType.PAIR_CANCEL
                ) {
                    throw PairingCancelledException()
                }

                val windowsConfirm =
                    PairingCodec.decodeConfirm(
                        windowsConfirmBytes,
                    )

                require(
                    windowsConfirm.role ==
                        PairingRole.WINDOWS,
                ) {
                    "Windows pairing confirmation role is invalid."
                }

                val expectedWindowsTag =
                    PairingCrypto.computeConfirmationTag(
                        PairingRole.WINDOWS,
                        requireNotNull(pairingRootKey),
                        requireNotNull(transcriptHash),
                    )

                try {
                    require(
                        PairingCrypto.verifyTag(
                            expectedWindowsTag,
                            windowsConfirm.tag,
                        ),
                    ) {
                        "Windows pairing confirmation failed."
                    }
                } finally {
                    expectedWindowsTag.fill(0)
                    windowsConfirm.tag.fill(0)
                }

                val completeBytes =
                    readMessage(socket.getInputStream())

                val completeTag =
                    PairingCodec.decodeComplete(
                        completeBytes,
                    )

                val expectedComplete =
                    PairingCrypto.computeCompletionTag(
                        requireNotNull(pairingRootKey),
                        requireNotNull(transcriptHash),
                    )

                try {
                    require(
                        MessageDigest.isEqual(
                            expectedComplete,
                            completeTag,
                        ),
                    ) {
                        "Pairing completion authentication failed."
                    }
                } finally {
                    expectedComplete.fill(0)
                    completeTag.fill(0)
                }

                trustedReceivers.save(
                    windowsDeviceId =
                        response.deviceId,
                    pairingRootKey =
                        requireNotNull(pairingRootKey),
                    displayName = prompt.receiverName,
                    lastHostAddress =
                        receiver.address.hostAddress,
                    lastPort =
                        receiver.response.controllerPort,
                )

                onCompleted?.invoke(prompt)
            }
        } catch (_: PairingCancelledException) {
            onFailed?.invoke("Pairing cancelled.")
        } catch (throwable: Throwable) {
            onFailed?.invoke(
                "Pairing failed: " +
                    (throwable.message
                        ?: throwable.javaClass.simpleName),
            )
        } finally {
            pairingRootKey?.fill(0)
            transcriptHash?.fill(0)
            activeSocket = null
            decisionLatch = null
            decision = null
            running.set(false)
        }
    }

    private fun readMessage(input: InputStream): ByteArray {
        val prefix = ByteArray(4)
        readExactly(input, prefix)

        val length =
            ByteBuffer.wrap(prefix)
                .order(ByteOrder.LITTLE_ENDIAN)
                .int

        require(
            length in
                PairingCodec.HEADER_SIZE..
                    PairingCodec.MAXIMUM_PAYLOAD_SIZE,
        ) {
            "Pairing message length is invalid."
        }

        return ByteArray(length).also {
            readExactly(input, it)
        }
    }

    private fun writeMessage(
        output: OutputStream,
        payload: ByteArray,
    ) {
        require(
            payload.size in
                PairingCodec.HEADER_SIZE..
                    PairingCodec.MAXIMUM_PAYLOAD_SIZE,
        )

        val prefix =
            ByteBuffer.allocate(4)
                .order(ByteOrder.LITTLE_ENDIAN)
                .putInt(payload.size)
                .array()

        output.write(prefix)
        output.write(payload)
        output.flush()
    }

    private fun readExactly(
        input: InputStream,
        destination: ByteArray,
    ) {
        var offset = 0

        while (offset < destination.size) {
            val read =
                input.read(
                    destination,
                    offset,
                    destination.size - offset,
                )

            if (read < 0) {
                throw EOFException(
                    "Pairing connection closed early.",
                )
            }

            offset += read
        }
    }

    private class PairingCancelledException :
        Exception()

    private companion object {
        const val PAIRING_PORT = 37072
        const val CONNECT_TIMEOUT_MS = 3_000
        const val PAIRING_TIMEOUT_MS = 60_000
    }
}
