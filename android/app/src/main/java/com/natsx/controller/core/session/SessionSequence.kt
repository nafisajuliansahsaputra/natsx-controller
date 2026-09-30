package com.natsx.controller.core.session

import java.util.concurrent.atomic.AtomicInteger

/**
 * Global realtime sequence source owned by one logical controller session.
 *
 * The same instance must be shared by Wi-Fi, Bluetooth, and USB transports so
 * sequence continuity survives transport handover.
 */
class SessionSequence(
    initialValue: UInt = 0u,
) {
    private val value = AtomicInteger(initialValue.toInt())

    fun next(): UInt = value.getAndIncrement().toUInt()

    fun peek(): UInt = value.get().toUInt()
}
