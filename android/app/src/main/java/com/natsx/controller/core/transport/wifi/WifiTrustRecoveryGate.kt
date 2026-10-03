package com.natsx.controller.core.transport.wifi

/** Recovery only offers a new SAS-confirmed exchange; it never erases existing trust. */
class WifiTrustRecoveryGate(
    private val nowMillis: () -> Long,
) {
    private var inFlight = false
    private var nextAttemptMillis = 0L

    @Synchronized
    fun tryBegin(failures: Int, hasLiveSession: Boolean): Boolean {
        if (failures < 3 || hasLiveSession || inFlight || nowMillis() < nextAttemptMillis) return false
        inFlight = true
        return true
    }

    @Synchronized
    fun finish() {
        inFlight = false
        nextAttemptMillis = nowMillis() + 60_000L
    }
}
