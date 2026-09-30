package com.natsx.controller.core.transport.wifi

import com.natsx.controller.core.protocol.PeerId

fun interface WifiReceiverDiscovery {
    fun discover(expectedReceiverPeerId: PeerId?): WifiDiscoveredReceiver?
}
