package com.natsx.controller.core.transport.usb

import android.os.SystemClock
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class UsbRealtimeSender(
    private val inputStream: InputStream,
    private val outputStream: OutputStream,
    private val trustedSession: UsbTrustedSession,
    private val nowNanos: () -> Long =
        SystemClock::elapsedRealtimeNanos,
) : RealtimeStateSink, Closeable {
    private val realtimeExecutor =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-usb-realtime").apply {
                isDaemon = true
                priority = Thread.NORM_PRIORITY + 1
            }
        }

    private val controlExecutor =
        Executors.newSingleThreadExecutor { runnable ->
            Thread(runnable, "natsx-usb-control").apply {
                isDaemon = true
            }
        }

    private val outputLock = Any()
    private val pending =
        AtomicReference<RealtimeStateEnvelope?>(null)
    private val drainScheduled = AtomicBoolean(false)
    private val closed = AtomicBoolean(false)

    @Volatile
    var lastHeartbeatReceivedNanos: Long = 0
        private set

    init {
        controlExecutor.execute(::controlLoop)
    }

    override fun publish(
        envelope: RealtimeStateEnvelope,
    ) {
        check(!closed.get()) {
            "USB realtime sender is closed."
        }

        pending.set(envelope)

        if (
            drainScheduled.compareAndSet(
                false,
                true,
            )
        ) {
            realtimeExecutor.execute(::drainLatest)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) {
            return
        }

        pending.set(null)
        realtimeExecutor.shutdownNow()
        controlExecutor.shutdownNow()

        runCatching {
            inputStream.close()
        }

        synchronized(outputLock) {
            runCatching {
                outputStream.close()
            }
        }
    }

    private fun drainLatest() {
        while (!closed.get()) {
            pending.getAndSet(null)
                ?.let(::send)

            drainScheduled.set(false)

            if (
                pending.get() != null &&
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
            val frame =
                ProtocolFrameCodec.encode(
                    ProtocolFrame(
                        version = ProtocolVersion.Current,
                        messageType = MessageType.GAMEPAD_STATE,
                        flags = FrameFlags.AUTHENTICATED,
                        sessionId = trustedSession.sessionId,
                        sequence = envelope.sequence,
                        monotonicTimestampMicros =
                            envelope.monotonicTimestampMicros,
                        payload =
                            GamepadStateCodec.encode(
                                envelope.state,
                            ),
                    ),
                    authenticationKey =
                        trustedSession.authenticationKey(),
                )

            write(
                UsbStreamFrameCodec.encode(frame),
            )
        } catch (_: IOException) {
        } catch (_: RuntimeException) {
        }
    }

    private fun controlLoop() {
        while (!closed.get()) {
            try {
                val frame =
                    UsbStreamFrameCodec.readFrame(
                        inputStream,
                    )

                val probeTimestamp =
                    UsbControlFrameCodec.decodeHeartbeat(
                        frame,
                        trustedSession,
                    )

                lastHeartbeatReceivedNanos =
                    nowNanos()

                val ack =
                    UsbControlFrameCodec
                        .encodeHeartbeatAck(
                            trustedSession,
                            responderTimestampMicros =
                                nowNanos().toULong() /
                                    1_000uL,
                            echoedProbeTimestampMicros =
                                probeTimestamp,
                        )

                write(
                    UsbStreamFrameCodec.encode(ack),
                )
            } catch (_: IOException) {
                return
            } catch (_: IllegalArgumentException) {
            } catch (_: IllegalStateException) {
                return
            }
        }
    }

    private fun write(
        bytes: ByteArray,
    ) {
        synchronized(outputLock) {
            if (closed.get()) {
                return
            }

            outputStream.write(bytes)
            outputStream.flush()
        }
    }
}
