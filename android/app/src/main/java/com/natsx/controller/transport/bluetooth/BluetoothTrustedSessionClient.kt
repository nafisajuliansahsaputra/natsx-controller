package com.natsx.controller.transport.bluetooth

import android.os.SystemClock
import com.natsx.controller.core.protocol.CapabilityFlags
import com.natsx.controller.core.protocol.DeviceRole
import com.natsx.controller.core.protocol.DynamicTrustedReconnectClientHandshake
import com.natsx.controller.core.protocol.EstablishedTrustedSession
import com.natsx.controller.core.protocol.HelloPayload
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.protocol.TransportMask
import com.natsx.controller.core.protocol.TrustState

data class EstablishedBluetoothSession(
    val connection: BluetoothRfcommConnection,
    val trustedSession: EstablishedTrustedSession,
)

class BluetoothTrustedSessionClient(
    private val androidDeviceId: SessionId,
    private val trustResolver: (SessionId) -> ByteArray?,
) {
    fun authenticate(
        connection: BluetoothRfcommConnection,
    ): EstablishedBluetoothSession {
        val hello = HelloPayload(
            deviceId = androidDeviceId,
            role = DeviceRole.ANDROID_CONTROLLER,
            transports =
                TransportMask.USB or
                    TransportMask.WIFI or
                    TransportMask.BLUETOOTH,
            capabilities =
                CapabilityFlags.RUMBLE or
                    CapabilityFlags.GUIDE or
                    CapabilityFlags.COMPETITIVE_240_HZ or
                    CapabilityFlags.WARM_STANDBY,
            minimumMajor = 1,
            maximumMajor = 1,
            maximumMinor = 0,
            trustState = TrustState.PAIRED,
        )

        val handshake =
            DynamicTrustedReconnectClientHandshake(
                androidHello = hello,
                trustResolver = trustResolver,
                monotonicMicros = {
                    (
                        SystemClock
                            .elapsedRealtimeNanos() /
                            1_000L
                    ).toULong()
                },
            )

        try {
            connection.write(
                handshake.createHelloFrame(),
            )

            val windowsHello =
                connection.read()

            handshake.handleWindowsHello(
                windowsHello,
            )

            val challenge =
                connection.read()

            val response =
                handshake.handleChallenge(
                    challenge,
                )

            connection.write(response)

            val sessionKey =
                handshake
                    .sessionKeyForAuthenticatedDecode()

            try {
                val ready =
                    connection.read(sessionKey)

                val established =
                    handshake
                        .handleSessionReady(ready)

                return EstablishedBluetoothSession(
                    connection = connection,
                    trustedSession = established,
                )
            } finally {
                sessionKey.fill(0)
            }
        } catch (throwable: Throwable) {
            connection.close()
            throw throwable
        } finally {
            handshake.close()
        }
    }
}
