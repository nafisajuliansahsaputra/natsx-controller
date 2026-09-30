package com.natsx.controller.core.session

import org.junit.Assert.assertEquals
import org.junit.Test

class SessionSequenceTest {
    @Test
    fun nextAdvancesAcrossSharedConsumers() {
        val sequence = SessionSequence(initialValue = 41u)

        val wifi = sequence.next()
        val bluetooth = sequence.next()
        val usb = sequence.next()

        assertEquals(41u, wifi)
        assertEquals(42u, bluetooth)
        assertEquals(43u, usb)
        assertEquals(44u, sequence.peek())
    }

    @Test
    fun wrapsUsingUnsigned32BitSerialSpace() {
        val sequence = SessionSequence(initialValue = UInt.MAX_VALUE)

        assertEquals(UInt.MAX_VALUE, sequence.next())
        assertEquals(0u, sequence.next())
        assertEquals(1u, sequence.peek())
    }
}
