package com.natsx.controller

import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.protocol.GamepadStateCodec
import com.natsx.controller.core.protocol.ProtocolFrameCodec
import com.natsx.controller.core.protocol.SessionId
import com.natsx.controller.core.transport.usb.UsbRealtimeSender
import com.natsx.controller.core.transport.usb.UsbStreamFrameCodec
import com.natsx.controller.core.transport.usb.UsbTrustedSession
import com.natsx.controller.feature.controller.ControllerDesignViewport
import java.io.ByteArrayInputStream
import java.io.OutputStream
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit

/** Actual Activity touch -> application store -> publisher -> USB sender -> authenticated wire state. */
internal object UsbInputVerification {
    fun verify(activity: MainActivity, onMain: (() -> Unit) -> Unit) {
        check(activity.packageName == "com.natsx.controller") { "APK must use the canonical controller identity" }
        val app = activity.application as NatsxControllerApplication
        val root = activity.window.decorView.findViewById<View>(android.R.id.content)
        val viewport = ControllerDesignViewport.fit(root.width.toFloat(), root.height.toFloat())
        val states = ArrayBlockingQueue<GamepadState>(256)
        // Synthetic test session; never uses/logs a device trust secret.
        val key = ByteArray(32) { it.toByte() }
        val session = UsbTrustedSession(SessionId.fromBytes(ByteArray(16) { (it + 1).toByte() }), key)
        val output = object : OutputStream() {
            override fun write(value: Int) = error("Sender must write complete packets")
            override fun write(bytes: ByteArray, offset: Int, length: Int) {
                val frame = ProtocolFrameCodec.decode(
                    UsbStreamFrameCodec.readFrame(ByteArrayInputStream(bytes, offset, length)), key)
                states.offer(GamepadStateCodec.decode(frame.payload))
            }
        }
        val sender = UsbRealtimeSender(output, session)
        fun receive(predicate: (GamepadState) -> Boolean) {
            val deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(5)
            while (System.nanoTime() < deadline) {
                val state = states.poll(100, TimeUnit.MILLISECONDS) ?: continue
                if (predicate(state)) return
            }
            error("Activity input did not reach an authenticated USB packet")
        }
        fun touch(action: Int, x: Float, y: Float) = onMain {
            val now = SystemClock.uptimeMillis()
            val event = MotionEvent.obtain(now, now, action, x, y, 0)
            try { check(root.dispatchTouchEvent(event)) } finally { event.recycle() }
        }
        try {
            app.realtimeBroadcaster.addSink(sender)
            fun press(x: Float, y: Float, expected: GamepadState) {
                states.clear()
                touch(MotionEvent.ACTION_DOWN, viewport.x(x), viewport.y(y))
                receive { it == expected }
                touch(MotionEvent.ACTION_UP, viewport.x(x), viewport.y(y))
                receive { it == GamepadState.Neutral }
            }
            // Real hitboxes -> complete authenticated state; equality catches crossed controls.
            for ((x, y, button) in listOf(
                Triple(2074.91f, 700.91f, GamepadButtons.A),
                Triple(2235.91f, 539.91f, GamepadButtons.B),
                Triple(1915.91f, 539.91f, GamepadButtons.X),
                Triple(2074.91f, 378.91f, GamepadButtons.Y),
                Triple(625f, 125f, GamepadButtons.LEFT_SHOULDER),
                Triple(1775f, 125f, GamepadButtons.RIGHT_SHOULDER),
                Triple(864f, 412f, GamepadButtons.LEFT_STICK),
                Triple(1534f, 412f, GamepadButtons.RIGHT_STICK),
                Triple(792f, 287f, GamepadButtons.BACK),
                Triple(1607f, 287f, GamepadButtons.START),
                Triple(1200f, 412f, GamepadButtons.GUIDE),
            )) press(x, y, GamepadState(buttons = button))
            press(225f, 125f, GamepadState(leftTrigger = 255))
            press(2175f, 125f, GamepadState(rightTrigger = 255))
            for ((x, y, direction) in listOf(
                Triple(900f, 620f, DpadState.UP),
                Triple(900f, 940f, DpadState.DOWN),
                Triple(740f, 780f, DpadState.LEFT),
                Triple(1060f, 780f, DpadState.RIGHT),
            )) press(x, y, GamepadState(dpad = direction))
            for ((cx, cy, left) in listOf(Triple(325f, 540f, true), Triple(1500f, 780f, false))) {
                for ((dx, dy) in listOf(100f to 0f, -100f to 0f, 0f to -100f, 0f to 100f)) {
                    states.clear()
                    touch(MotionEvent.ACTION_DOWN, viewport.x(cx), viewport.y(cy))
                    touch(MotionEvent.ACTION_MOVE, viewport.x(cx + dx), viewport.y(cy + dy))
                    receive {
                        val x = if (left) it.leftX else it.rightX
                        val y = if (left) it.leftY else it.rightY
                        val otherCentered = if (left) it.rightX == 0 && it.rightY == 0
                            else it.leftX == 0 && it.leftY == 0
                        otherCentered && it.buttons == 0 && it.dpad == 0 &&
                            it.leftTrigger == 0 && it.rightTrigger == 0 &&
                            when {
                                dx > 0 -> x > 0 && y == 0
                                dx < 0 -> x < 0 && y == 0
                                dy < 0 -> x == 0 && y > 0
                                else -> x == 0 && y < 0
                            }
                    }
                    touch(MotionEvent.ACTION_CANCEL, viewport.x(cx), viewport.y(cy))
                    receive { it == GamepadState.Neutral }
                }
            }
            check(sender.sentFrames > 0 && sender.sendFailures == 0L)
        } finally {
            onMain { app.gamepadStateStore.neutralize() }
            app.realtimeBroadcaster.removeSink(sender)
            sender.close()
            session.close()
            key.fill(0)
        }
    }
}
