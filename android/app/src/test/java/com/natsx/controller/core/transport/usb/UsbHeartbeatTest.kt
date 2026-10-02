package com.natsx.controller.core.transport.usb

import com.natsx.controller.core.protocol.HandoverPayload
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.protocol.RumblePayload
import com.natsx.controller.core.protocol.SessionId
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class UsbHeartbeatTest {
    @Test
    fun senderAcknowledgesAuthenticatedHeartbeat() {
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        UsbTrustedSession(
            sessionId = SessionId.createRandom(),
            sessionKey = sessionKey,
        ).use { session ->
            val probeTimestamp = 123_456uL

            val framedHeartbeat =
                UsbStreamFrameCodec.encode(
                    UsbControlFrameCodec
                        .encodeHeartbeat(
                            trustedSession = session,
                            monotonicTimestampMicros =
                                probeTimestamp,
                        ),
                )

            val input =
                ByteArrayInputStream(
                    framedHeartbeat,
                )
            val output =
                ByteArrayOutputStream()

            val sender =
                UsbRealtimeSender(
                    outputStream = output,
                    trustedSession = session,
                    inputStream = input,
                    nowNanos = {
                        987_654_321L
                    },
                )

            waitUntil {
                sender.heartbeatAcksSent == 1L
            }

            sender.close()

            assertEquals(
                1L,
                sender.heartbeatsReceived,
            )

            val ackFrame =
                UsbStreamFrameCodec
                    .readFrame(
                        ByteArrayInputStream(
                            output.toByteArray(),
                        ),
                    )

            assertEquals(
                probeTimestamp,
                UsbControlFrameCodec
                    .decodeHeartbeatAck(
                        ackFrame,
                        session,
                    ),
            )

            assertTrue(
                sender.lastHeartbeatReceivedNanos >
                    0L,
            )
        }

        sessionKey.fill(0)
    }

    @Test
    fun senderDispatchesAuthenticatedRumble() {
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        UsbTrustedSession(
            sessionId = SessionId.createRandom(),
            sessionKey = sessionKey,
        ).use { session ->
            val expected =
                RumblePayload(
                    lowFrequencyMotor = 255,
                    highFrequencyMotor = 42,
                )

            val framedRumble =
                UsbStreamFrameCodec.encode(
                    UsbControlFrameCodec
                        .encodeRumble(
                            trustedSession = session,
                            payload = expected,
                            monotonicTimestampMicros = 123uL,
                        ),
                )

            var observed: RumblePayload? = null

            val sender =
                UsbRealtimeSender(
                    outputStream =
                        ByteArrayOutputStream(),
                    trustedSession = session,
                    inputStream =
                        ByteArrayInputStream(
                            framedRumble,
                        ),
                    rumbleSink = {
                        observed = it
                    },
                )

            waitUntil {
                sender.rumblesReceived == 1L
            }

            sender.close()

            assertEquals(
                expected,
                observed,
            )
        }

        sessionKey.fill(0)
    }

    @Test
    fun senderDispatchesAuthenticatedHandoverCommit() {
        val sessionKey =
            ByteArray(32) { index ->
                index.toByte()
            }

        UsbTrustedSession(
            sessionId = SessionId.createRandom(),
            sessionKey = sessionKey,
        ).use { session ->
            val expected =
                HandoverPayload(
                    transport =
                        ProtocolTransport.USB_DIRECT,
                    stateSequence = 0xCAFE_BABEu,
                )

            val framedCommit =
                UsbStreamFrameCodec.encode(
                    UsbControlFrameCodec
                        .encodeHandoverCommit(
                            trustedSession = session,
                            payload = expected,
                            monotonicTimestampMicros = 456uL,
                        ),
                )

            var observed: HandoverPayload? = null

            val sender =
                UsbRealtimeSender(
                    outputStream =
                        ByteArrayOutputStream(),
                    trustedSession = session,
                    inputStream =
                        ByteArrayInputStream(
                            framedCommit,
                        ),
                    handoverSink = {
                        observed = it
                    },
                )

            waitUntil {
                sender.handoverCommitsReceived == 1L
            }

            sender.close()

            assertEquals(
                expected,
                observed,
            )
        }

        sessionKey.fill(0)
    }

    private fun waitUntil(
        timeoutMillis: Long = 1_000,
        condition: () -> Boolean,
    ) {
        val deadline =
            System.nanoTime() +
                timeoutMillis * 1_000_000

        while (
            !condition() &&
            System.nanoTime() < deadline
        ) {
            Thread.sleep(5)
        }

        check(condition()) {
            "Timed out waiting for Usb heartbeat acknowledgement."
        }
    }
}
