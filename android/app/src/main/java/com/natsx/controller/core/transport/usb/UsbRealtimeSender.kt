package com.natsx.controller.core.transport.usb

import android.os.SystemClock
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolConstants
import com.natsx.controller.core.protocol.RumblePayload
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

class UsbRealtimeSender(
    private val outputStream: OutputStream,
    private val trustedSession: UsbTrustedSession,
    private val inputStream: InputStream? = null,
    private val nowNanos: () -> Long =
        SystemClock::elapsedRealtimeNanos,
    private val rumbleSink: (RumblePayload) -> Unit = {},
) : RealtimeStateSink, Closeable {
    private val executor: ExecutorService =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(
                runnable,
                "natsx-usb-realtime",
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
                    "natsx-usb-control",
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
            "Usb realtime sender is closed."
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

        // The UsbAccessoryConnection owns the shared accessory file
        // descriptor and its stream wrappers. Closing either stream here can
        // invalidate the same descriptor before the connection owner tears it
        // down, which is especially fragile during physical detach on OEM
        // Android builds.
        //
        // UsbTrustedSession belongs to the logical controller session and may
        // be shared with a reconnecting transport.
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
                UsbRealtimeStreamEncoder
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
                    UsbStreamFrameCodec
                        .readFrame(activeInput)

                when (readMessageType(frame)) {
                    MessageType.HEARTBEAT -> {
                        val echoedProbe =
                            UsbControlFrameCodec
                                .decodeHeartbeat(
                                    frame,
                                    trustedSession,
                                )

                        heartbeatsReceived += 1
                        lastHeartbeatReceivedNanos =
                            nowNanos()

                        val ack =
                            UsbControlFrameCodec
                                .encodeHeartbeatAck(
                                    trustedSession =
                                        trustedSession,
                                    responderTimestampMicros =
                                        monotonicMicroseconds(),
                                    echoedProbeTimestampMicros =
                                        echoedProbe,
                                )

                        writePacket(
                            UsbStreamFrameCodec
                                .encode(ack),
                        )

                        heartbeatAcksSent += 1
                    }

                    MessageType.RUMBLE -> {
                        val rumble =
                            UsbControlFrameCodec
                                .decodeRumble(
                                    frame,
                                    trustedSession,
                                )

                        rumbleSink(rumble)
                        rumblesReceived += 1
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
            "Usb control frame is shorter than the protocol header."
        }

        return requireNotNull(
            MessageType.fromWireValue(
                frame[6].toInt() and 0xFF,
            ),
        ) {
            "Usb control frame has an unknown message type."
        }
    }

    private fun writePacket(
        bytes: ByteArray,
    ) {
        synchronized(outputLock) {
            check(!closed.get()) {
                "Usb realtime sender is closed."
            }

            outputStream.write(bytes)
            outputStream.flush()
        }
    }

    private fun monotonicMicroseconds(): ULong =
        nowNanos().toULong() / 1_000uL
}
