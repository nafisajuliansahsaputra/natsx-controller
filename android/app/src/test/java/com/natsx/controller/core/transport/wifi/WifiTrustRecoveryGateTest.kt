package com.natsx.controller.core.transport.wifi

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WifiTrustRecoveryGateTest {
    @Test
    fun recoveryIsBoundedAndNeverInterruptsLiveTransport() {
        var now = 100L
        val gate = WifiTrustRecoveryGate { now }
        assertFalse(gate.tryBegin(2, false))
        assertFalse(gate.tryBegin(20, true))
        assertTrue(gate.tryBegin(3, false))
        assertFalse(gate.tryBegin(4, false))
        gate.finish()
        assertFalse(gate.tryBegin(5, false))
        now += 60_000L
        assertTrue(gate.tryBegin(6, false))
    }
}
