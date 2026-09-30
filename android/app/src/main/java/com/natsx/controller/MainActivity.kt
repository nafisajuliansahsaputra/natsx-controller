package com.natsx.controller

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.view.View
import android.view.WindowManager
import com.natsx.controller.feature.controller.ControllerSurfaceView
import com.natsx.controller.feature.pairing.PairingScreenView
import com.natsx.controller.feature.settings.SettingsScreenView
import com.natsx.controller.pairing.AndroidPairingClient
import com.natsx.controller.service.ControllerService
import com.natsx.controller.transport.wifi.DiscoveredReceiver
import com.natsx.controller.transport.wifi.WifiDiscoveryClient

class MainActivity : Activity() {
    private lateinit var controllerView: ControllerSurfaceView
    private var pairingView: PairingScreenView? = null
    private var pairingClient: AndroidPairingClient? = null
    private var currentScreen = Screen.PAIRING

    private val app: NatsxControllerApp
        get() = application as NatsxControllerApp

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        window.addFlags(
            WindowManager.LayoutParams.FLAG_FULLSCREEN or
                WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
        )

        applyImmersiveMode()

        if (app.trustedReceiverStore
                .listTrustedDeviceIds()
                .isEmpty()
        ) {
            showPairingScreen()
        } else {
            showControllerScreen()
        }

        startForegroundService(
            Intent(this, ControllerService::class.java),
        )
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)

        if (hasFocus) {
            applyImmersiveMode()
        } else if (
            currentScreen == Screen.CONTROLLER &&
            ::controllerView.isInitialized
        ) {
            controllerView.releaseAllInputs()
        }
    }

    override fun onStop() {
        if (
            currentScreen == Screen.CONTROLLER &&
            ::controllerView.isInitialized
        ) {
            controllerView.releaseAllInputs()
        }

        super.onStop()
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        when (currentScreen) {
            Screen.CONTROLLER -> showSettingsScreen()
            Screen.SETTINGS -> showControllerScreen()
            Screen.PAIRING -> {
                if (app.trustedReceiverStore
                        .listTrustedDeviceIds()
                        .isNotEmpty()
                ) {
                    showSettingsScreen()
                } else {
                    super.onBackPressed()
                }
            }
        }
    }

    override fun onDestroy() {
        pairingClient?.close()
        pairingClient = null
        super.onDestroy()
    }

    private fun showPairingScreen() {
        releaseGameplayInputs()

        if (app.trustedReceiverStore
                .listTrustedDeviceIds()
                .isNotEmpty()
        ) {
            app.connectionRuntime.stop()
        }

        pairingClient?.close()
        pairingClient = null

        currentScreen = Screen.PAIRING

        val view = PairingScreenView(this)
        pairingView = view

        view.onSearchRequested = {
            discoverReceivers()
        }

        view.onReceiverSelected = { receiver ->
            beginPairing(receiver)
        }

        view.onConfirmRequested = {
            pairingClient?.confirm()
        }

        view.onCancelRequested = {
            pairingClient?.cancel()
            view.showFailure("Pairing cancelled.")
        }

        setContentView(view)
        applyImmersiveMode()
    }

    private fun discoverReceivers() {
        val view = pairingView ?: return
        view.showSearching()

        Thread(
            {
                val receivers = runCatching {
                    WifiDiscoveryClient(
                        app.deviceIdentityStore
                            .getOrCreate(),
                    ).discover(1_200)
                }.getOrDefault(emptyList())

                runOnUiThread {
                    pairingView?.showReceivers(
                        receivers,
                    )
                }
            },
            "natsx-pairing-discovery",
        ).apply {
            isDaemon = true
            start()
        }
    }

    private fun beginPairing(
        receiver: DiscoveredReceiver,
    ) {
        val view = pairingView ?: return

        view.showPairingStarted(
            receiver.response.receiverName,
        )

        pairingClient?.close()

        val client = AndroidPairingClient(
            androidDeviceId =
                app.deviceIdentityStore.getOrCreate(),
            trustedReceivers =
                app.trustedReceiverStore,
        )

        pairingClient = client

        client.onPromptReady = { prompt ->
            runOnUiThread {
                pairingView?.showPrompt(prompt)
            }
        }

        client.onCompleted = { prompt ->
            runOnUiThread {
                pairingView?.showSuccess(
                    prompt.receiverName,
                )

                showControllerScreen()
            }
        }

        client.onFailed = { message ->
            runOnUiThread {
                pairingView?.showFailure(message)
            }
        }

        client.start(receiver)
    }

    private fun showControllerScreen() {
        pairingClient?.close()
        pairingClient = null
        pairingView = null
        currentScreen = Screen.CONTROLLER

        controllerView = ControllerSurfaceView(
            context = this,
            stateStore = app.gamepadStateStore,
            haptics = app.controllerHaptics,
        )

        setContentView(controllerView)
        app.connectionRuntime.start()
        applyImmersiveMode()
    }

    private fun showSettingsScreen() {
        releaseGameplayInputs()

        pairingClient?.close()
        pairingClient = null
        pairingView = null
        currentScreen = Screen.SETTINGS

        val view = SettingsScreenView(this)

        view.onBackRequested = {
            showControllerScreen()
        }

        view.onPairNewRequested = {
            showPairingScreen()
        }

        view.onHapticStrengthChanged = { strength ->
            app.hapticSettings.strength = strength
            app.controllerHaptics.touchTick()
            bindSettings(view)
        }

        view.onForgetRequested = { receiver ->
            app.connectionRuntime.stop()
            app.trustedReceiverStore.forget(
                receiver.windowsDeviceId,
            )
            releaseGameplayInputs()

            if (app.trustedReceiverStore
                    .listTrustedDeviceIds()
                    .isEmpty()
            ) {
                showPairingScreen()
            } else {
                app.connectionRuntime.start()
                bindSettings(view)
            }
        }

        bindSettings(view)
        setContentView(view)
        applyImmersiveMode()
    }

    private fun bindSettings(
        view: SettingsScreenView,
    ) {
        view.bind(
            hapticStrength =
                app.hapticSettings.strength,
            trustedReceivers =
                app.trustedReceiverStore.listMetadata(),
            connectionStatus =
                app.connectionRuntime.status,
        )
    }

    private fun releaseGameplayInputs() {
        if (::controllerView.isInitialized) {
            controllerView.releaseAllInputs()
        } else {
            app.gamepadStateStore.neutralize()
        }

        app.controllerHaptics.stopRumble()
    }

    private fun applyImmersiveMode() {
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility =
            View.SYSTEM_UI_FLAG_FULLSCREEN or
                View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                View.SYSTEM_UI_FLAG_LAYOUT_STABLE
    }

    private enum class Screen {
        PAIRING,
        CONTROLLER,
        SETTINGS,
    }
}
