package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.SessionId
import java.net.InetSocketAddress

class WifiTransportConfig(
    val endpoint: InetSocketAddress,
    val sessionId: SessionId,
    authenticationKey: ByteArray,
    val inputRateHz: Int = 120,
) {
    val authenticationKey: ByteArray = authenticationKey.copyOf()

    init {
        require(!endpoint.isUnresolved) {
            "Wi-Fi receiver endpoint must be resolved."
        }

        require(authenticationKey.size >= 16) {
            "Wi-Fi session authentication key must be at least 16 bytes."
        }

        require(inputRateHz in 30..240) {
            "Input rate must be between 30 and 240 Hz."
        }
    }
}
