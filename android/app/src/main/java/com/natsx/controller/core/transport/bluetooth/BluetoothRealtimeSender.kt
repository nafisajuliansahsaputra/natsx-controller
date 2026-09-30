package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.session.RealtimeStateEnvelope
import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable
import java.io.IOException
import java.io.OutputStream
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference

class BluetoothRealtimeSender(
    private val outputStream: OutputStream,
    private val trustedSession: BluetoothTrustedSession,
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

        try {
            outputStream.close()
        } catch (_: IOException) {
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

            outputStream.write(bytes)
            outputStream.flush()
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
}
