package com.natsx.controller.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.connection.GamepadStateSender
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.ControlPayloadCodec
import com.natsx.controller.core.protocol.EstablishedTrustedSession
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolVersion
import java.io.EOFException
import java.io.IOException
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

class BluetoothControllerTransport(
    private val connection: BluetoothRfcommConnection,
    private val session: EstablishedTrustedSession,
) : GamepadStateSender, AutoCloseable {
    private val running = AtomicBoolean(false)
    private val sequence = AtomicLong(0)

    @Volatile
    private var receiverThread: Thread? = null

    @Volatile
    var onFrameReceived: ((ProtocolFrame) -> Unit)? = null

    @Volatile
    var onTransportError: ((Throwable) -> Unit)? = null

    val isRunning: Boolean
        get() = running.get()

    fun start() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        receiverThread = Thread(
            { receiveLoop() },
            "natsx-bluetooth-receive",
        ).apply {
            isDaemon = true
            start()
        }
    }

    override fun send(state: GamepadState) {
        check(running.get()) {
            "Bluetooth controller transport is not running."
        }

        connection.write(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.GAMEPAD_STATE,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = session.sessionId,
                sequence =
                    sequence.incrementAndGet().toUInt(),
                monotonicTimestampMicros =
                    monotonicMicros(),
                payload =
                    GamepadStateCodec.encode(state),
            ),
            session.sessionKey,
        )
    }

    fun sendFrame(frame: ProtocolFrame) {
        check(frame.sessionId == session.sessionId)
        connection.write(
            frame,
            session.sessionKey,
        )
    }

    private fun receiveLoop() {
        while (running.get()) {
            val frame = try {
                connection.read(
                    session.sessionKey,
                )
            } catch (throwable: Throwable) {
                if (running.get() &&
                    throwable !is EOFException
                ) {
                    onTransportError?.invoke(
                        throwable,
                    )
                }
                break
            }

            if (frame.sessionId != session.sessionId) {
                continue
            }

            if (frame.messageType ==
                MessageType.HEARTBEAT
            ) {
                acknowledgeHeartbeat(frame)
            }

            onFrameReceived?.invoke(frame)
        }

        running.set(false)
    }

    private fun acknowledgeHeartbeat(
        request: ProtocolFrame,
    ) {
        val heartbeat = runCatching {
            ControlPayloadCodec.decodeHeartbeat(
                request.payload,
            )
        }.getOrNull() ?: return

        runCatching {
            sendFrame(
                ProtocolFrame(
                    version =
                        ProtocolVersion.Current,
                    messageType =
                        MessageType.HEARTBEAT_ACK,
                    flags =
                        FrameFlags.AUTHENTICATED,
                    sessionId =
                        session.sessionId,
                    sequence = 0u,
                    monotonicTimestampMicros =
                        monotonicMicros(),
                    payload =
                        ControlPayloadCodec
                            .encodeHeartbeat(
                                heartbeat,
                            ),
                ),
            )
        }.onFailure {
            if (running.get()) {
                onTransportError?.invoke(it)
            }
        }
    }

    override fun close() {
        if (!running.getAndSet(false)) {
            connection.close()
            session.sessionKey.fill(0)
            return
        }

        connection.close()
        receiverThread?.interrupt()
        receiverThread = null
        session.sessionKey.fill(0)
    }

    private fun monotonicMicros(): ULong =
        (
            SystemClock.elapsedRealtimeNanos() /
                1_000L
        ).toULong()
}
