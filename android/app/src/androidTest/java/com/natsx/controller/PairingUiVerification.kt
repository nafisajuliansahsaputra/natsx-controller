package com.natsx.controller

import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.TextView
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.pairing.PairingPrompt
import com.natsx.controller.core.protocol.PeerId
import java.util.concurrent.CountDownLatch
import java.util.concurrent.FutureTask
import java.util.concurrent.TimeUnit

/** Exercises the real Activity hierarchy, including the HUD above the input surface. */
internal object PairingUiVerification {
    fun verify(activity: MainActivity, onMain: (() -> Unit) -> Unit) {
        val app = activity.application as NatsxControllerApplication
        val root = activity.window.decorView.findViewById<View>(android.R.id.content)
        fun views(view: View): List<View> = listOf(view) +
            if (view is ViewGroup) (0 until view.childCount).flatMap { views(view.getChildAt(it)) } else emptyList()

        for (approved in listOf(true, false)) {
            val displayed = CountDownLatch(1)
            val listener: (PairingPrompt?) -> Unit = { if (it != null) displayed.countDown() }
            app.pairingConfirmation.addListener(listener)
            val prompt = PairingPrompt("123456", PeerId.fromBytes(ByteArray(16) { 1 }))
            val pending = FutureTask { app.pairingConfirmation.requestConfirmation(prompt, 10) }
            Thread(pending, "pairing-ui-verification").start()
            try {
                check(displayed.await(5, TimeUnit.SECONDS)) { "Pairing request did not reach UI" }
                onMain {
                    check(views(root).filterIsInstance<TextView>().any { it.text.toString() == prompt.comparisonCode && it.isShown }) {
                        "Comparison code must be visible in the real Activity"
                    }
                    // Pending approval must neutralize and block gameplay touches.
                    val viewport = com.natsx.controller.feature.controller.ControllerDesignViewport.fit(root.width.toFloat(), root.height.toFloat())
                    touch(root, MotionEvent.ACTION_DOWN, viewport.x(2074.62f), viewport.y(539.62f) + 191f * viewport.scale)
                    touch(root, MotionEvent.ACTION_UP, viewport.x(2074.62f), viewport.y(539.62f) + 191f * viewport.scale)
                    check(app.gamepadStateStore.snapshot() == GamepadState.Neutral)
                    val action = if (approved) "Confirm" else "Reject"
                    check(views(root).filterIsInstance<Button>().single { it.text.toString() == action }.performClick())
                }
                check(pending.get(5, TimeUnit.SECONDS) == approved) { "Pairing action did not reach coordinator" }
                onMain {
                    check(views(root).filterIsInstance<TextView>().none { it.text.toString() == prompt.comparisonCode && it.isShown }) {
                        "Pairing overlay must disappear after resolution"
                    }
                    // A must pass through the complete HUD hierarchy again after approval ends.
                    val viewport = com.natsx.controller.feature.controller.ControllerDesignViewport.fit(root.width.toFloat(), root.height.toFloat())
                    touch(root, MotionEvent.ACTION_DOWN, viewport.x(2074.62f), viewport.y(539.62f) + 191f * viewport.scale)
                    check(app.gamepadStateStore.snapshot().buttons and GamepadButtons.A != 0) { "HUD blocked A input" }
                    touch(root, MotionEvent.ACTION_UP, viewport.x(2074.62f), viewport.y(539.62f) + 191f * viewport.scale)
                    check(app.gamepadStateStore.snapshot() == GamepadState.Neutral)
                }
            } finally {
                app.pairingConfirmation.resolve(false)
                app.pairingConfirmation.removeListener(listener)
            }
        }
    }

    private fun touch(root: View, action: Int, x: Float, y: Float) {
        val now = android.os.SystemClock.uptimeMillis()
        MotionEvent.obtain(now, now, action, x, y, 0).let { event ->
            try { root.dispatchTouchEvent(event) } finally { event.recycle() }
        }
    }
}
