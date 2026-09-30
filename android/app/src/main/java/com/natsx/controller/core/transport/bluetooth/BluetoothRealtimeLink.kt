package com.natsx.controller.core.transport.bluetooth

import com.natsx.controller.core.session.RealtimeStateSink
import java.io.Closeable

interface BluetoothRealtimeLink : Closeable, RealtimeStateSink {
    val lastHeartbeatReceivedNanos: Long
}

fun interface BluetoothRealtimeLinkFactory {
    fun create(): BluetoothRealtimeLink
}
