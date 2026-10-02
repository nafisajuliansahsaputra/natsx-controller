package com.natsx.controller

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.SeekBar
import android.widget.TextView
import com.natsx.controller.core.connection.AndroidConnectionStatus
import com.natsx.controller.core.connection.AndroidLinkState
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.haptics.HapticLevel
import com.natsx.controller.core.input.ControllerInputTuning
import com.natsx.controller.core.input.ControllerProfile
import com.natsx.controller.core.pairing.PairingConfirmationCoordinator
import com.natsx.controller.core.pairing.PairingPrompt
import com.natsx.controller.core.protocol.ProtocolTransport
import com.natsx.controller.core.transport.bluetooth.BluetoothPermissionGate
import com.natsx.controller.core.transport.usb.UsbRuntimeStatus
import com.natsx.controller.core.transport.usb.UsbRuntimeStatusCoordinator
import com.natsx.controller.core.trust.TrustedPeerRecord
import com.natsx.controller.feature.controller.ControllerSurfaceView
import com.natsx.controller.service.ControllerService
import kotlin.math.roundToInt

class MainActivity : Activity() {
    private lateinit var app: NatsxControllerApplication
    private lateinit var stateStore: GamepadStateStore
    private lateinit var controllerView: ControllerSurfaceView
    private lateinit var pairingConfirmation: PairingConfirmationCoordinator
    private lateinit var pairingOverlay: LinearLayout
    private lateinit var pairingCodeText: TextView
    private lateinit var pairingPeerText: TextView
    private lateinit var usbRuntimeStatus: UsbRuntimeStatusCoordinator
    private lateinit var usbStatusText: TextView
    private var currentUsbStatus =
        UsbRuntimeStatus(
            "USB idle",
        )
    private var currentConnectionStatus =
        AndroidConnectionStatus()

    private val pairingListener: (PairingPrompt?) -> Unit = { prompt ->
        runOnUiThread {
            showPairingPrompt(prompt)
        }
    }

    private val usbStatusListener: (UsbRuntimeStatus) -> Unit = { status ->
        runOnUiThread {
            currentUsbStatus = status
            renderStatusOverlay()
        }
    }

    private val connectionStatusListener:
        (AndroidConnectionStatus) -> Unit = { status ->
            runOnUiThread {
                currentConnectionStatus = status
                renderStatusOverlay()
            }
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        window.addFlags(
            WindowManager.LayoutParams.FLAG_FULLSCREEN or
                WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
        )

        applyImmersiveMode()

        app = application as NatsxControllerApplication

        stateStore = app.gamepadStateStore
        pairingConfirmation = app.pairingConfirmation
        usbRuntimeStatus = app.usbRuntimeStatus
        controllerView =
            ControllerSurfaceView(
                context = this,
                stateStore = stateStore,
                hapticLevel = {
                    app.hapticSettings.level
                },
            ).apply {
                applyInputTuning(
                    app.inputSettings.current(),
                )
            }

        setContentView(
            buildRootView(),
        )

        pairingConfirmation.addListener(
            pairingListener,
        )

        usbRuntimeStatus.addListener(
            usbStatusListener,
        )

        app.connectionStatus.addListener(
            connectionStatusListener,
        )

        requestBluetoothPermissionsIfNeeded()

        startForegroundService(
            Intent(
                this,
                ControllerService::class.java,
            ),
        )
    }

    private fun buildRootView(): View {
        val root = FrameLayout(this)

        root.addView(
            controllerView,
            FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT,
            ),
        )

        usbStatusText =
            TextView(this).apply {
                textSize = 13f
                setTextColor(Color.WHITE)
                setBackgroundColor(
                    Color.argb(
                        190,
                        16,
                        16,
                        20,
                    ),
                )
                setPadding(
                    dp(12),
                    dp(8),
                    dp(12),
                    dp(8),
                )
                maxLines = 10
                isClickable = true
                isFocusable = true
                setOnClickListener {
                    cycleHapticLevel()
                }
                setOnLongClickListener {
                    showTrustedPcList()
                    true
                }
            }

        root.addView(
            usbStatusText,
            FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.TOP or Gravity.CENTER_HORIZONTAL,
            ).apply {
                topMargin = dp(12)
            },
        )

        val settingsButton =
            TextView(this).apply {
                text = "⚙"
                textSize = 22f
                gravity = Gravity.CENTER
                contentDescription = "Controller settings"
                setTextColor(Color.WHITE)
                setBackgroundColor(
                    Color.argb(
                        190,
                        16,
                        16,
                        20,
                    ),
                )
                isClickable = true
                isFocusable = true
                setOnClickListener {
                    showControllerSettings()
                }
            }

        root.addView(
            settingsButton,
            FrameLayout.LayoutParams(
                dp(44),
                dp(44),
                Gravity.TOP or Gravity.END,
            ).apply {
                topMargin = dp(12)
                marginEnd = dp(12)
            },
        )

        pairingOverlay =
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER
                setPadding(
                    dp(32),
                    dp(24),
                    dp(32),
                    dp(24),
                )
                setBackgroundColor(
                    Color.argb(
                        238,
                        14,
                        14,
                        18,
                    ),
                )
                visibility = View.GONE
                isClickable = true
                isFocusable = true
            }

        val title =
            TextView(this).apply {
                text = "Pair this Windows receiver"
                textSize = 24f
                setTextColor(Color.WHITE)
                gravity = Gravity.CENTER
            }

        val description =
            TextView(this).apply {
                text =
                    "Make sure this code matches the code shown on your PC."
                textSize = 15f
                setTextColor(
                    Color.rgb(
                        205,
                        205,
                        215,
                    ),
                )
                gravity = Gravity.CENTER
                setPadding(
                    0,
                    dp(8),
                    0,
                    dp(18),
                )
            }

        pairingCodeText =
            TextView(this).apply {
                textSize = 44f
                setTextColor(Color.WHITE)
                gravity = Gravity.CENTER
                letterSpacing = 0.18f
            }

        pairingPeerText =
            TextView(this).apply {
                textSize = 12f
                setTextColor(
                    Color.rgb(
                        170,
                        170,
                        180,
                    ),
                )
                gravity = Gravity.CENTER
                setPadding(
                    0,
                    dp(8),
                    0,
                    dp(20),
                )
            }

        val actions =
            LinearLayout(this).apply {
                orientation =
                    LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER
            }

        val reject =
            Button(this).apply {
                text = "Reject"
                setOnClickListener {
                    pairingConfirmation.resolve(
                        false,
                    )
                }
            }

        val confirm =
            Button(this).apply {
                text = "Confirm"
                setOnClickListener {
                    pairingConfirmation.resolve(
                        true,
                    )
                }
            }

        actions.addView(
            reject,
            LinearLayout.LayoutParams(
                dp(140),
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ).apply {
                marginEnd = dp(12)
            },
        )

        actions.addView(
            confirm,
            LinearLayout.LayoutParams(
                dp(140),
                ViewGroup.LayoutParams.WRAP_CONTENT,
            ),
        )

        pairingOverlay.addView(title)
        pairingOverlay.addView(description)
        pairingOverlay.addView(pairingCodeText)
        pairingOverlay.addView(pairingPeerText)
        pairingOverlay.addView(actions)

        root.addView(
            pairingOverlay,
            FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT,
            ),
        )

        return root
    }

    private fun showPairingPrompt(
        prompt: PairingPrompt?,
    ) {
        if (!::pairingOverlay.isInitialized) {
            return
        }

        if (prompt == null) {
            pairingOverlay.visibility =
                View.GONE
            return
        }

        controllerView.releaseAllInputs()

        pairingCodeText.text =
            prompt.comparisonCode

        pairingPeerText.text =
            "PC: " +
                prompt.remotePeerId
                    .toString()

        pairingOverlay.visibility =
            View.VISIBLE
        pairingOverlay.bringToFront()
    }

    private fun renderStatusOverlay() {
        if (!::usbStatusText.isInitialized) {
            return
        }

        val smartAuto =
            currentConnectionStatus.smartAutoActiveTransport
                ?.let {
                    "Smart Auto • Active: " +
                        smartAutoTransportLabel(it)
                }
                ?: "Smart Auto • Waiting for Windows"

        val links =
            "Links • Wi-Fi ${linkStateLabel(currentConnectionStatus.wifi)}" +
                " • Bluetooth ${linkStateLabel(currentConnectionStatus.bluetooth)}" +
                " • USB ${linkStateLabel(currentConnectionStatus.usb)}"

        val controls =
            "Profile — ${app.inputSettings.profile.displayName}" +
                " • Haptics — ${hapticLevelLabel(app.hapticSettings.level)} (tap)" +
                " • PCs ${currentConnectionStatus.trustedPcCount} (hold)"

        val usbDetail =
            currentUsbStatus
                .takeIf {
                    it.isError
                }
                ?.let {
                    "\nUSB — ${it.message}"
                }
                .orEmpty()

        usbStatusText.text =
            smartAuto + "\n" +
                links + "\n" +
                controls + usbDetail

        usbStatusText.setTextColor(
            if (currentUsbStatus.isError) {
                Color.rgb(
                    255,
                    150,
                    150,
                )
            } else {
                Color.WHITE
            },
        )

        usbStatusText.bringToFront()

        if (
            ::pairingOverlay.isInitialized &&
            pairingOverlay.visibility ==
                View.VISIBLE
        ) {
            pairingOverlay.bringToFront()
        }
    }

    private fun linkStateLabel(
        state: AndroidLinkState,
    ): String =
        when (state) {
            AndroidLinkState.UNAVAILABLE -> "Unavailable"
            AndroidLinkState.PERMISSION_REQUIRED -> "Permission"
            AndroidLinkState.OFF -> "Off"
            AndroidLinkState.IDLE -> "Idle"
            AndroidLinkState.CONNECTING -> "Connecting"
            AndroidLinkState.ACTIVE -> "Active"
            AndroidLinkState.RECONNECTING -> "Reconnecting"
            AndroidLinkState.STOPPED -> "Stopped"
        }

    private fun smartAutoTransportLabel(
        transport: ProtocolTransport,
    ): String =
        when (transport) {
            ProtocolTransport.WIFI -> "Wi-Fi"
            ProtocolTransport.BLUETOOTH -> "Bluetooth"
            ProtocolTransport.USB_DIRECT -> "USB"
        }

    private fun showControllerSettings() {
        controllerView.releaseAllInputs()

        val tuning =
            app.inputSettings.current()

        val container =
            LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(
                    dp(20),
                    dp(8),
                    dp(20),
                    dp(8),
                )
            }

        val profileButton =
            Button(this)

        val leftDeadzoneLabel =
            TextView(this)
        val leftSensitivityLabel =
            TextView(this)
        val rightDeadzoneLabel =
            TextView(this)
        val rightSensitivityLabel =
            TextView(this)

        val leftDeadzone =
            SeekBar(this).apply {
                max = 25
            }
        val leftSensitivity =
            SeekBar(this).apply {
                max = 100
            }
        val rightDeadzone =
            SeekBar(this).apply {
                max = 25
            }
        val rightSensitivity =
            SeekBar(this).apply {
                max = 100
            }

        fun sensitivityFromProgress(
            progress: Int,
        ): Float =
            (50 + progress) / 100f

        fun sensitivityProgress(
            value: Float,
        ): Int =
            ((value - 0.5f) * 100f)
                .roundToInt()
                .coerceIn(
                    0,
                    100,
                )

        fun updateLabels() {
            leftDeadzoneLabel.text =
                "Left deadzone — ${leftDeadzone.progress}%"
            leftSensitivityLabel.text =
                "Left sensitivity — " +
                    "${(sensitivityFromProgress(leftSensitivity.progress) * 100f).roundToInt()}%"
            rightDeadzoneLabel.text =
                "Right deadzone — ${rightDeadzone.progress}%"
            rightSensitivityLabel.text =
                "Right sensitivity — " +
                    "${(sensitivityFromProgress(rightSensitivity.progress) * 100f).roundToInt()}%"

            profileButton.text =
                "Profile — " +
                    app.inputSettings
                        .profile
                        .displayName
        }

        fun setControls(
            next: ControllerInputTuning,
        ) {
            leftDeadzone.progress =
                (next.leftDeadzone * 100f)
                    .roundToInt()
            rightDeadzone.progress =
                (next.rightDeadzone * 100f)
                    .roundToInt()
            leftSensitivity.progress =
                sensitivityProgress(
                    next.leftSensitivity,
                )
            rightSensitivity.progress =
                sensitivityProgress(
                    next.rightSensitivity,
                )

            updateLabels()
        }

        fun applyCustomTuning() {
            val next =
                ControllerInputTuning(
                    leftDeadzone =
                        leftDeadzone.progress /
                            100f,
                    rightDeadzone =
                        rightDeadzone.progress /
                            100f,
                    leftSensitivity =
                        sensitivityFromProgress(
                            leftSensitivity.progress,
                        ),
                    rightSensitivity =
                        sensitivityFromProgress(
                            rightSensitivity.progress,
                        ),
                )

            app.inputSettings
                .updateCustom(next)
            controllerView
                .applyInputTuning(next)
            updateLabels()
            renderStatusOverlay()
        }

        val seekListener =
            object :
                SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(
                    seekBar: SeekBar?,
                    progress: Int,
                    fromUser: Boolean,
                ) {
                    if (fromUser) {
                        applyCustomTuning()
                    } else {
                        updateLabels()
                    }
                }

                override fun onStartTrackingTouch(
                    seekBar: SeekBar?,
                ) = Unit

                override fun onStopTrackingTouch(
                    seekBar: SeekBar?,
                ) = Unit
            }

        leftDeadzone.setOnSeekBarChangeListener(
            seekListener,
        )
        leftSensitivity.setOnSeekBarChangeListener(
            seekListener,
        )
        rightDeadzone.setOnSeekBarChangeListener(
            seekListener,
        )
        rightSensitivity.setOnSeekBarChangeListener(
            seekListener,
        )

        profileButton.setOnClickListener {
            showControllerProfileChooser { profile ->
                val next =
                    app.inputSettings
                        .applyProfile(profile)

                controllerView
                    .applyInputTuning(next)
                setControls(next)
                renderStatusOverlay()
            }
        }

        container.addView(profileButton)
        container.addView(leftDeadzoneLabel)
        container.addView(leftDeadzone)
        container.addView(leftSensitivityLabel)
        container.addView(leftSensitivity)
        container.addView(rightDeadzoneLabel)
        container.addView(rightDeadzone)
        container.addView(rightSensitivityLabel)
        container.addView(rightSensitivity)

        setControls(tuning)

        AlertDialog.Builder(this)
            .setTitle("Controller settings")
            .setView(container)
            .setPositiveButton(
                "Done",
                null,
            )
            .create()
            .apply {
                setOnDismissListener {
                    applyImmersiveMode()
                }
                show()
            }
    }

    private fun showControllerProfileChooser(
        onSelected: (ControllerProfile) -> Unit,
    ) {
        val profiles =
            ControllerProfile.entries
        val selected =
            profiles.indexOf(
                app.inputSettings.profile,
            )

        AlertDialog.Builder(this)
            .setTitle("Controller profile")
            .setSingleChoiceItems(
                profiles
                    .map {
                        it.displayName
                    }
                    .toTypedArray(),
                selected,
            ) { dialog, which ->
                onSelected(
                    profiles[which],
                )
                dialog.dismiss()
            }
            .setNegativeButton(
                "Cancel",
                null,
            )
            .show()
    }

    private fun cycleHapticLevel() {
        val next =
            when (app.hapticSettings.level) {
                HapticLevel.OFF ->
                    HapticLevel.LOW

                HapticLevel.LOW ->
                    HapticLevel.MEDIUM

                HapticLevel.MEDIUM ->
                    HapticLevel.HIGH

                HapticLevel.HIGH ->
                    HapticLevel.OFF
            }

        app.hapticSettings.level = next
        app.hapticEngine.stopGameRumble()

        currentUsbStatus =
            usbRuntimeStatus.current()
        renderStatusOverlay()
    }

    private fun hapticLevelLabel(
        level: HapticLevel,
    ): String =
        when (level) {
            HapticLevel.OFF -> "Off"
            HapticLevel.LOW -> "Low"
            HapticLevel.MEDIUM -> "Medium"
            HapticLevel.HIGH -> "High"
        }

    private fun showTrustedPcList() {
        val peers =
            app.trustedPeerStore.list()

        if (peers.isEmpty()) {
            AlertDialog.Builder(this)
                .setTitle("Trusted PCs")
                .setMessage(
                    "No trusted Windows receiver is saved yet.",
                )
                .setPositiveButton(
                    "Close",
                    null,
                )
                .show()
            return
        }

        val labels =
            peers.map { peer ->
                val name =
                    peer.displayName
                        ?.takeIf {
                            it.isNotBlank()
                        }
                        ?: "NATSX Windows Receiver"

                "$name\n${peer.peerId}"
            }.toTypedArray()

        AlertDialog.Builder(this)
            .setTitle("Trusted PCs")
            .setItems(
                labels,
            ) { _, index ->
                showForgetTrustedPcConfirmation(
                    peers[index],
                )
            }
            .setNegativeButton(
                "Close",
                null,
            )
            .show()
    }

    private fun showForgetTrustedPcConfirmation(
        peer: TrustedPeerRecord,
    ) {
        val name =
            peer.displayName
                ?.takeIf {
                    it.isNotBlank()
                }
                ?: "NATSX Windows Receiver"

        AlertDialog.Builder(this)
            .setTitle("Forget trusted PC?")
            .setMessage(
                "$name will need secure pairing again before it can control this phone session.",
            )
            .setNegativeButton(
                "Cancel",
                null,
            )
            .setPositiveButton(
                "Forget",
            ) { _, _ ->
                forgetTrustedPc(peer)
            }
            .show()
    }

    private fun forgetTrustedPc(
        peer: TrustedPeerRecord,
    ) {
        controllerView.releaseAllInputs()
        app.hapticEngine.stopGameRumble()
        app.trustedSessionRegistry.remove(
            peer.peerId,
        )
        app.trustedPeerStore.remove(
            peer.peerId,
        )

        val serviceIntent =
            Intent(
                this,
                ControllerService::class.java,
            )

        stopService(
            serviceIntent,
        )
        startForegroundService(
            serviceIntent,
        )

        currentUsbStatus =
            usbRuntimeStatus.current()
        currentConnectionStatus =
            app.connectionStatus.current()
        renderStatusOverlay()
    }

    private fun requestBluetoothPermissionsIfNeeded() {
        val permissionGate = BluetoothPermissionGate(this)

        if (!permissionGate.isBluetoothSupported()) {
            return
        }

        val missingPermissions =
            permissionGate.missingRuntimePermissions()

        if (missingPermissions.isNotEmpty()) {
            requestPermissions(
                missingPermissions.toTypedArray(),
                REQUEST_BLUETOOTH_PERMISSIONS,
            )
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)

        if (hasFocus) {
            applyImmersiveMode()
        } else if (::controllerView.isInitialized) {
            controllerView.releaseAllInputs()
        }
    }

    override fun onStop() {
        if (::controllerView.isInitialized) {
            controllerView.releaseAllInputs()
        }

        super.onStop()
    }

    override fun onDestroy() {
        if (::pairingConfirmation.isInitialized) {
            pairingConfirmation.removeListener(
                pairingListener,
            )
        }

        if (::usbRuntimeStatus.isInitialized) {
            usbRuntimeStatus.removeListener(
                usbStatusListener,
            )
        }

        if (::app.isInitialized) {
            app.connectionStatus.removeListener(
                connectionStatusListener,
            )
        }

        super.onDestroy()
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

    private fun dp(value: Int): Int =
        (value * resources.displayMetrics.density)
            .toInt()

    private companion object {
        const val REQUEST_BLUETOOTH_PERMISSIONS = 2001
    }
}
