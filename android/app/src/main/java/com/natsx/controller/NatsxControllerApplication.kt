package com.natsx.controller

import android.app.Application
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.pairing.PairingConfirmationCoordinator
import com.natsx.controller.core.session.ControllerRealtimePublisher
import com.natsx.controller.core.session.RealtimeStateBroadcaster
import com.natsx.controller.core.session.SessionSequence
import com.natsx.controller.core.trust.AndroidKeystoreTrustSecretProtector
import com.natsx.controller.core.trust.LocalPeerIdentityStore
import com.natsx.controller.core.trust.SharedPreferencesTrustedPeerStore
import com.natsx.controller.core.trust.TrustedPeerStore
import com.natsx.controller.core.protocol.PeerId
import com.natsx.controller.core.protocol.TrustedSessionRegistry
import com.natsx.controller.core.transport.usb.UsbRuntimeStatusCoordinator
import com.natsx.controller.core.transport.wifi.SharedPreferencesWifiEndpointCache
import com.natsx.controller.core.transport.wifi.WifiEndpointCache

class NatsxControllerApplication : Application() {
    private val trustPreferences by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        getSharedPreferences("natsx_trust_v1", MODE_PRIVATE)
    }

    private val connectionPreferences by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        getSharedPreferences("natsx_connection_v1", MODE_PRIVATE)
    }

    val localPeerId: PeerId by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        LocalPeerIdentityStore(trustPreferences).getOrCreate()
    }

    val pairingConfirmation: PairingConfirmationCoordinator by lazy(
        LazyThreadSafetyMode.SYNCHRONIZED,
    ) {
        PairingConfirmationCoordinator()
    }

    val usbRuntimeStatus: UsbRuntimeStatusCoordinator by lazy(
        LazyThreadSafetyMode.SYNCHRONIZED,
    ) {
        UsbRuntimeStatusCoordinator()
    }

    val trustedPeerStore: TrustedPeerStore by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        SharedPreferencesTrustedPeerStore(
            preferences = trustPreferences,
            protector = AndroidKeystoreTrustSecretProtector(),
        )
    }

    val wifiEndpointCache: WifiEndpointCache by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        SharedPreferencesWifiEndpointCache(connectionPreferences)
    }

    val gamepadStateStore: GamepadStateStore by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        GamepadStateStore()
    }

    val sessionSequence: SessionSequence by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        SessionSequence()
    }

    val trustedSessionRegistry: TrustedSessionRegistry by lazy(
        LazyThreadSafetyMode.SYNCHRONIZED,
    ) {
        TrustedSessionRegistry()
    }

    val realtimeBroadcaster: RealtimeStateBroadcaster by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        RealtimeStateBroadcaster(
            sequence = sessionSequence,
            monotonicMicros = ControllerRealtimePublisher::defaultMonotonicMicros,
        )
    }
}
