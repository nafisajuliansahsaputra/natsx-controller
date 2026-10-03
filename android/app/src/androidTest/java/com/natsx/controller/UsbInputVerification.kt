package com.natsx.controller

import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
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
            val ax = viewport.x(2074.91f)
            val ay = viewport.y(539.91f) + 161f * viewport.scale
            touch(MotionEvent.ACTION_DOWN, ax, ay)
            receive { it.buttons and GamepadButtons.A != 0 }
            touch(MotionEvent.ACTION_UP, ax, ay)
            receive { it == GamepadState.Neutral }
            states.clear()
            touch(MotionEvent.ACTION_DOWN, viewport.x(2175f), viewport.y(125f))
            receive { it.rightTrigger == 255 }
            touch(MotionEvent.ACTION_UP, viewport.x(2175f), viewport.y(125f))
            receive { it == GamepadState.Neutral }
            states.clear()
            touch(MotionEvent.ACTION_DOWN, viewport.x(325f), viewport.y(540f))
            touch(MotionEvent.ACTION_MOVE, viewport.x(325f) + 110f * viewport.scale, viewport.y(540f))
            receive { it.leftX > 0 }
            touch(MotionEvent.ACTION_CANCEL, viewport.x(325f), viewport.y(540f))
            receive { it == GamepadState.Neutral }
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
