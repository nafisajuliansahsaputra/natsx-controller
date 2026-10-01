package com.natsx.controller

import android.app.Activity
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
import android.widget.TextView
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.pairing.PairingConfirmationCoordinator
import com.natsx.controller.core.pairing.PairingPrompt
import com.natsx.controller.core.transport.bluetooth.BluetoothPermissionGate
import com.natsx.controller.core.transport.usb.UsbRuntimeStatus
import com.natsx.controller.core.transport.usb.UsbRuntimeStatusCoordinator
import com.natsx.controller.feature.controller.ControllerSurfaceView
import com.natsx.controller.service.ControllerService

class MainActivity : Activity() {
    private lateinit var stateStore: GamepadStateStore
    private lateinit var controllerView: ControllerSurfaceView
    private lateinit var pairingConfirmation: PairingConfirmationCoordinator
    private lateinit var pairingOverlay: LinearLayout
    private lateinit var pairingCodeText: TextView
    private lateinit var pairingPeerText: TextView
    private lateinit var usbRuntimeStatus: UsbRuntimeStatusCoordinator
    private lateinit var usbStatusText: TextView

    private val pairingListener: (PairingPrompt?) -> Unit = { prompt ->
        runOnUiThread {
            showPairingPrompt(prompt)
        }
    }

    private val usbStatusListener: (UsbRuntimeStatus) -> Unit = { status ->
        runOnUiThread {
            showUsbStatus(status)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        window.addFlags(
            WindowManager.LayoutParams.FLAG_FULLSCREEN or
                WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON,
        )

        applyImmersiveMode()

        val app = application as NatsxControllerApplication

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
            )

        setContentView(
            buildRootView(),
        )

        pairingConfirmation.addListener(
            pairingListener,
        )

        usbRuntimeStatus.addListener(
            usbStatusListener,
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
                maxLines = 9
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

    private fun showUsbStatus(
        status: UsbRuntimeStatus,
    ) {
        if (!::usbStatusText.isInitialized) {
            return
        }

        val history =
            usbRuntimeStatus.historyText()

        usbStatusText.text =
            if (history.isBlank()) {
                if (status.isError) {
                    "USB ERROR — ${status.message}"
                } else {
                    "USB — ${status.message}"
                }
            } else {
                "USB diagnostics\n$history"
            }

        usbStatusText.setTextColor(
            if (status.isError) {
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
