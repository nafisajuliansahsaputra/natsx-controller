package com.natsx.controller.transport.wifi

import android.os.SystemClock
import com.natsx.controller.core.protocol.ControlPayloadCodec
import com.natsx.controller.core.protocol.FrameFlags
import com.natsx.controller.core.protocol.MessageType
import com.natsx.controller.core.protocol.ProtocolFrame
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.ProtocolVersion
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TrustedSessionCrypto
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.SocketException
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

class WifiControllerTransport(
    private val remoteAddress: InetAddress,
    private val remotePort: Int = DEFAULT_PORT,
    private val sessionId: SessionId,
    sessionKey: ByteArray,
) : AutoCloseable {
    private val sessionKey = sessionKey.copyOf()
    private val running = AtomicBoolean(false)
    private val sequence = AtomicLong(0)
    private var socket: DatagramSocket? = null
    private var receiveThread: Thread? = null

    @Volatile
    var onFrameReceived: ((ProtocolFrame) -> Unit)? = null

    @Volatile
    var onTransportError: ((Throwable) -> Unit)? = null

    init {
        require(remotePort in 1..65535)
        require(this.sessionKey.size == TrustedSessionCrypto.SESSION_KEY_SIZE)
    }

    val isRunning: Boolean
        get() = running.get()

    fun start(localPort: Int = 0) {
        if (!running.compareAndSet(false, true)) {
            return
        }

        try {
            socket = DatagramSocket(null).apply {
                reuseAddress = true
                bind(InetSocketAddress(localPort))
                connect(remoteAddress, remotePort)
            }
        } catch (throwable: Throwable) {
            running.set(false)
            throw throwable
        }

        receiveThread = Thread(
            { receiveLoop() },
            "natsx-wifi-receive",
        ).apply {
            isDaemon = true
            start()
        }
    }

    fun nextSequence(): UInt = sequence.incrementAndGet().toUInt()

    @Synchronized
    fun send(frame: ProtocolFrame) {
        require(frame.sessionId == sessionId) {
            "Frame Session ID does not match active Wi-Fi session."
        }

        val activeSocket = socket ?: error("Wi-Fi transport is not running.")
        val encoded = ProtocolFrameCodec.encode(frame, sessionKey)
        val packet = DatagramPacket(encoded, encoded.size)

        activeSocket.send(packet)
    }

    override fun close() {
        if (!running.getAndSet(false)) {
            return
        }

        socket?.close()
        socket = null

        receiveThread?.interrupt()
        receiveThread = null

        sessionKey.fill(0)
    }

    private fun receiveLoop() {
        val buffer = ByteArray(MAX_DATAGRAM_SIZE)

        while (running.get()) {
            val activeSocket = socket ?: break
            val packet = DatagramPacket(buffer, buffer.size)

            try {
                activeSocket.receive(packet)

                val bytes = packet.data.copyOfRange(
                    packet.offset,
                    packet.offset + packet.length,
                )

                val frame = ProtocolFrameCodec.decode(bytes, sessionKey)

                if (frame.sessionId != sessionId) {
                    continue
                }

                if (frame.messageType == MessageType.HEARTBEAT) {
                    sendHeartbeatAck(frame)
                }

                onFrameReceived?.invoke(frame)
            } catch (_: SocketException) {
                if (running.get()) {
                    onTransportError?.invoke(
                        IllegalStateException("Wi-Fi socket closed unexpectedly."),
                    )
                }
                break
            } catch (throwable: Throwable) {
                if (running.get()) {
                    onTransportError?.invoke(throwable)
                }
            }
        }
    }

    private fun sendHeartbeatAck(request: ProtocolFrame) {
        val heartbeat = ControlPayloadCodec.decodeHeartbeat(request.payload)

        send(
            ProtocolFrame(
                version = ProtocolVersion.Current,
                messageType = MessageType.HEARTBEAT_ACK,
                flags = FrameFlags.AUTHENTICATED,
                sessionId = sessionId,
                sequence = 0u,
                monotonicTimestampMicros = (
                    SystemClock.elapsedRealtimeNanos() / 1_000L
                ).toULong(),
                payload = ControlPayloadCodec.encodeHeartbeat(heartbeat),
            ),
        )
    }

    companion object {
        const val DEFAULT_PORT = 37074
        private const val MAX_DATAGRAM_SIZE = 8192
    }
}
