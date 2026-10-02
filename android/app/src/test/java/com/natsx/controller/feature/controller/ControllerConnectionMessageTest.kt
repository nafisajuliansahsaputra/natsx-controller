package com.natsx.controller.feature.controller

import com.natsx.controller.core.connection.AndroidConnectionStatus
import com.natsx.controller.core.connection.AndroidLinkState
import com.natsx.controller.core.protocol.ProtocolTransport
import org.junit.Assert.*
import org.junit.Test

class ControllerConnectionMessageTest {
    @Test fun freshInstallMakesRequiredPairingVisible() {
        assertEquals("Pair with receiver", ControllerConnectionMessage.forStatus(AndroidConnectionStatus()))
    }
    @Test fun missingUsbPermissionIsActionableBeforePairing() {
        assertEquals("Allow USB access", ControllerConnectionMessage.forStatus(
            AndroidConnectionStatus(usb = AndroidLinkState.PERMISSION_REQUIRED)))
    }
    @Test fun warmLinkDoesNotPretendReceiverAuthorityIsReady() {
        assertEquals("Connecting to receiver", ControllerConnectionMessage.forStatus(
            AndroidConnectionStatus(usb = AndroidLinkState.ACTIVE, trustedPcCount = 1)))
    }
    @Test fun lostReceiverShowsDisconnectedState() {
        assertEquals("Receiver disconnected", ControllerConnectionMessage.forStatus(
            AndroidConnectionStatus(trustedPcCount = 1)))
    }
    @Test fun authenticatedReceiverAuthorityClearsTheMessage() {
        assertNull(ControllerConnectionMessage.forStatus(AndroidConnectionStatus(
            trustedPcCount = 1, smartAutoActiveTransport = ProtocolTransport.USB_DIRECT,
            smartAutoStateSequence = 1u)))
    }
}
