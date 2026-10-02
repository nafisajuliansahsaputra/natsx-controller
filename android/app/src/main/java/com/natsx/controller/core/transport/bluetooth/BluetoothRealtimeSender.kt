package com.natsx.controller.core.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolConstants
import com.natsx.controller.core.protocol.RumblePayload
import com.natsx.controller.core.protocol.TransportPreferencePayload
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class BluetoothRealtimeSender(
    private val outputStream: OutputStream,
    private val trustedSession: BluetoothTrustedSession,
    private val inputStream: InputStream? = null,
    private val nowNanos: () -> Long =
        SystemClock::elapsedRealtimeNanos,
    private val rumbleSink: (RumblePayload) -> Unit = {},
    private val handoverSink: (HandoverPayload) -> Unit = {},
) : RealtimeStateSink, Closeable {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(
                runnable,
                "natsx-bluetooth-realtime",
            ).apply {
                isDaemon = true
                priority = Thread.NORM_PRIORITY + 1
            }
        }

    private val controlExecutor: ExecutorService? =
        inputStream?.let {
            Executors.newSingleThreadExecutor { runnable ->
                Thread(
                    runnable,
                    "natsx-bluetooth-control",
                ).apply {
                    isDaemon = true
                }
            }
        }

    private val outputLock = Any()

    private val pendingEnvelope =
        AtomicReference<RealtimeStateEnvelope?>(null)

    private val drainScheduled =
        AtomicBoolean(false)

    private val closed =
        AtomicBoolean(false)

    private val lastTransportPreference =
        AtomicReference<TransportPreferencePayload?>(null)

    @Volatile
    var sentFrames: Long = 0
        private set

    @Volatile
    var sendFailures: Long = 0
        private set

    @Volatile
    var heartbeatsReceived: Long = 0
        private set

    @Volatile
    var heartbeatAcksSent: Long = 0
        private set

    @Volatile
    var rumblesReceived: Long = 0
        private set

    @Volatile
    var handoverCommitsReceived: Long = 0
        private set

    @Volatile
    var controlFailures: Long = 0
        private set

    @Volatile
    var lastHeartbeatReceivedNanos: Long = 0
        private set

    init {
        controlExecutor?.execute(
            ::receiveControlLoop,
        )
    }

    override fun publish(
        envelope: RealtimeStateEnvelope,
    ) {
        check(!closed.get()) {
            "Bluetooth realtime sender is closed."
        }

        pendingEnvelope.set(envelope)

        if (drainScheduled.compareAndSet(
                false,
                true,
            )
        ) {
            executor.execute(::drainLatest)
        }
    }

    fun trySendTransportPreference(
        payload: TransportPreferencePayload,
    ): Boolean {
        if (closed.get()) {
            return false
        }

        if (lastTransportPreference.get() == payload) {
            return true
        }

        return try {
            val frame =
                BluetoothControlFrameCodec
                    .encodeTransportPreference(
                        trustedSession = trustedSession,
                        payload = payload,
                        monotonicTimestampMicros =
                            monotonicMicroseconds(),
                    )

            writePacket(
                BluetoothStreamFrameCodec
                    .encode(frame),
            )

            lastTransportPreference.set(payload)
            true
        } catch (_: IOException) {
            if (!closed.get()) {
                controlFailures += 1
            }
            false
        } catch (_: RuntimeException) {
            if (!closed.get()) {
                controlFailures += 1
            }
            false
        }
    }

    override fun close() {
        if (!closed.compareAndSet(
                false,
                true,
            )
        ) {
            return
        }

        pendingEnvelope.set(null)
        executor.shutdownNow()
        controlExecutor?.shutdownNow()

        try {
            inputStream?.close()
        } catch (_: IOException) {
        }

        synchronized(outputLock) {
            try {
                outputStream.close()
            } catch (_: IOException) {
            }
        }

        // BluetoothTrustedSession belongs to the logical controller
        // session and may be shared with a reconnecting transport.
    }

    private fun drainLatest() {
        while (!closed.get()) {
            val envelope =
                pendingEnvelope.getAndSet(null)

            if (envelope != null) {
                send(envelope)
            }

            drainScheduled.set(false)

            if (
                pendingEnvelope.get() != null &&
                drainScheduled.compareAndSet(
                    false,
                    true,
                )
            ) {
                continue
            }

            return
        }

        drainScheduled.set(false)
    }

    private fun send(
        envelope: RealtimeStateEnvelope,
    ) {
        try {
            val bytes =
                BluetoothRealtimeStreamEncoder
                    .encodeGamepadState(
                        state = envelope.state,
                        trustedSession = trustedSession,
                        sequence = envelope.sequence,
                        monotonicTimestampMicros =
                            envelope.monotonicTimestampMicros,
                    )

            writePacket(bytes)
            sentFrames += 1
        } catch (_: IOException) {
            if (!closed.get()) {
                sendFailures += 1
            }
        } catch (_: RuntimeException) {
            if (!closed.get()) {
                sendFailures += 1
            }
        }
    }

    private fun receiveControlLoop() {
        val activeInput = inputStream ?: return

        while (!closed.get()) {
            try {
                val frame =
                    BluetoothStreamFrameCodec
                        .readFrame(activeInput)

                when (readMessageType(frame)) {
                    MessageType.HEARTBEAT -> {
                        val echoedProbe =
                            BluetoothControlFrameCodec
                                .decodeHeartbeat(
                                    frame,
                                    trustedSession,
                                )

                        heartbeatsReceived += 1
                        lastHeartbeatReceivedNanos =
                            nowNanos()

                        val ack =
                            BluetoothControlFrameCodec
                                .encodeHeartbeatAck(
                                    trustedSession =
                                        trustedSession,
                                    responderTimestampMicros =
                                        monotonicMicroseconds(),
                                    echoedProbeTimestampMicros =
                                        echoedProbe,
                                )

                        writePacket(
                            BluetoothStreamFrameCodec
                                .encode(ack),
                        )

                        heartbeatAcksSent += 1
                    }

                    MessageType.RUMBLE -> {
                        val rumble =
                            BluetoothControlFrameCodec
                                .decodeRumble(
                                    frame,
                                    trustedSession,
                                )

                        rumbleSink(rumble)
                        rumblesReceived += 1
                    }

                    MessageType.HANDOVER_COMMIT -> {
                        val handover =
                            BluetoothControlFrameCodec
                                .decodeHandoverCommit(
                                    frame,
                                    trustedSession,
                                )

                        handoverSink(handover)
                        handoverCommitsReceived += 1
                    }

                    else ->
                        controlFailures += 1
                }
            } catch (_: IOException) {
                if (!closed.get()) {
                    controlFailures += 1
                }
                return
            } catch (_: IllegalArgumentException) {
                if (!closed.get()) {
                    controlFailures += 1
                }
            } catch (_: IllegalStateException) {
                if (!closed.get()) {
                    controlFailures += 1
                }
            }
        }
    }

    private fun readMessageType(
        frame: ByteArray,
    ): MessageType {
        require(
            frame.size >=
                ProtocolConstants.HEADER_SIZE,
        ) {
            "Bluetooth control frame is shorter than the protocol header."
        }

        return requireNotNull(
            MessageType.fromWireValue(
                frame[6].toInt() and 0xFF,
            ),
        ) {
            "Bluetooth control frame has an unknown message type."
        }
    }

    private fun writePacket(
        bytes: ByteArray,
    ) {
        synchronized(outputLock) {
            check(!closed.get()) {
                "Bluetooth realtime sender is closed."
            }

            outputStream.write(bytes)
            outputStream.flush()
        }
    }

    private fun monotonicMicroseconds(): ULong =
        nowNanos().toULong() / 1_000uL
}
