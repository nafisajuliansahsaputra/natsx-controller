package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.SocketException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicReference
import java.util.concurrent.locks.LockSupport

enum class WifiTransportState {
    DISCONNECTED,
    CONNECTING,
    READY,
    ACTIVE,
    FAILED,
}

class WifiControllerTransport(
    private val stateStore: GamepadStateStore,
    private val config: WifiTransportConfig,
) : AutoCloseable {
    private val running = AtomicBoolean(false)
    private val state = AtomicReference(WifiTransportState.DISCONNECTED)
    private val executor = Executors.newFixedThreadPool(2)
    private val sendLock = Any()

    @Volatile
    private var socket: DatagramSocket? = null

    fun state(): WifiTransportState = state.get()

    fun connect() {
        if (!running.compareAndSet(false, true)) {
            return
        }

        state.set(WifiTransportState.CONNECTING)

        try {
            val nextSocket = DatagramSocket()
            nextSocket.connect(config.endpoint)
            nextSocket.soTimeout = 250
            socket = nextSocket
            state.set(WifiTransportState.READY)

            executor.execute(::sendLoop)
            executor.execute(::receiveLoop)
        } catch (exception: RuntimeException) {
            running.set(false)
            state.set(WifiTransportState.FAILED)
            throw exception
        } catch (exception: SocketException) {
            running.set(false)
            state.set(WifiTransportState.FAILED)
            throw IllegalStateException("Unable to open Wi-Fi controller socket.", exception)
        }
    }

    private fun sendLoop() {
        var sequence = 1u
        val periodNanos = 1_000_000_000L / config.inputRateHz
        var nextDeadline = System.nanoTime()

        try {
            while (running.get()) {
                val now = System.nanoTime()
                val waitNanos = nextDeadline - now

                if (waitNanos > 0) {
                    LockSupport.parkNanos(waitNanos)
                    continue
                }

                val frame = ProtocolFrame(
                    version = ProtocolVersion.Current,
                    messageType = MessageType.GAMEPAD_STATE,
                    flags = FrameFlags.AUTHENTICATED,
                    sessionId = config.sessionId,
                    sequence = sequence,
                    monotonicTimestampMicros = (System.nanoTime() / 1_000L).toULong(),
                    payload = GamepadStateCodec.encode(stateStore.snapshot()),
                )

                sendFrame(frame)
                state.compareAndSet(WifiTransportState.READY, WifiTransportState.ACTIVE)

                sequence += 1u
                nextDeadline += periodNanos

                val behind = System.nanoTime() - nextDeadline
                if (behind > periodNanos * 4) {
                    nextDeadline = System.nanoTime() + periodNanos
                }
            }
        } catch (_: SocketException) {
            if (running.get()) {
                fail()
            }
        } catch (_: RuntimeException) {
            if (running.get()) {
                fail()
            }
        }
    }

    private fun receiveLoop() {
        val buffer = ByteArray(4096 + 64)

        try {
            while (running.get()) {
                val packet = DatagramPacket(buffer, buffer.size)

                try {
                    socket?.receive(packet) ?: break
                } catch (_: java.net.SocketTimeoutException) {
                    continue
                }

                val bytes =
                    packet.data.copyOfRange(
                        packet.offset,
                        packet.offset + packet.length,
                    )

                val frame = runCatching {
                    ProtocolFrameCodec.decode(
                        bytes,
                        config.authenticationKey,
                    )
                }.getOrNull() ?: continue

                if (frame.flags and FrameFlags.AUTHENTICATED == 0 ||
                    frame.sessionId != config.sessionId
                ) {
                    continue
                }

                when (frame.messageType) {
                    MessageType.HEARTBEAT -> sendHeartbeatAck(frame)
                    else -> Unit
                }
            }
        } catch (_: SocketException) {
            if (running.get()) {
                fail()
            }
        } catch (_: RuntimeException) {
            if (running.get()) {
                fail()
            }
        }
    }

    private fun sendHeartbeatAck(request: ProtocolFrame) {
        if (request.payload.size != 8) {
            return
        }

        val frame = ProtocolFrame(
            version = ProtocolVersion.Current,
            messageType = MessageType.HEARTBEAT_ACK,
            flags = FrameFlags.AUTHENTICATED,
            sessionId = config.sessionId,
            sequence = 0u,
            monotonicTimestampMicros = (System.nanoTime() / 1_000L).toULong(),
            payload = request.payload.copyOf(),
        )

        sendFrame(frame)
    }

    private fun sendFrame(frame: ProtocolFrame) {
        val encoded = ProtocolFrameCodec.encode(
            frame,
            config.authenticationKey,
        )

        synchronized(sendLock) {
            val activeSocket = socket ?: return
            activeSocket.send(DatagramPacket(encoded, encoded.size))
        }
    }

    private fun fail() {
        state.set(WifiTransportState.FAILED)
        running.set(false)
        socket?.close()
    }

    override fun close() {
        running.set(false)
        socket?.close()
        socket = null

        executor.shutdown()
        executor.awaitTermination(1, TimeUnit.SECONDS)

        stateStore.neutralize()
        state.set(WifiTransportState.DISCONNECTED)
    }
}
