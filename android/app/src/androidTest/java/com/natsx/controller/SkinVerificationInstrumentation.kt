package com.natsx.controller

import android.app.Instrumentation
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.os.Bundle
import android.os.SystemClock
import android.view.MotionEvent
import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.haptics.HapticLevel
import com.natsx.controller.feature.controller.ControllerSurfaceView
import java.io.File

/** Runs the actual View/Canvas and MotionEvent routing without a paired PC or test libraries. */
class SkinVerificationInstrumentation : Instrumentation() {
    override fun onCreate(arguments: Bundle?) {
        super.onCreate(arguments)
        start()
    }

    override fun onStart() {
        var error: Throwable? = null
        runOnMainSync {
            try {
                for ((w, h) in listOf(2400 to 1080, 1870 to 841, 1920 to 1080)) verify(w, h)
            } catch (failure: Throwable) {
                error = failure
            }
        }
        if (error == null) {
            val activity = startActivitySync(
                android.content.Intent(targetContext, MainActivity::class.java)
                    .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK),
            ) as MainActivity
            try {
                waitForIdleSync()
                PairingUiVerification.verify(activity) { block -> runOnMainSync { block() } }
            } catch (failure: Throwable) {
                error = failure
            } finally {
                runOnMainSync { activity.finish() }
            }
        }
        val result = Bundle()
        result.putString("stream", error?.stackTraceToString() ?: "PASS: native rendering, safe insets, multitouch, diagonals, release/cancel at three aspect ratios; Activity pairing confirm/reject and input unblock\n")
        finish(if (error == null) -1 else 0, result)
    }

    private fun verify(w: Int, h: Int) {
        val store = GamepadStateStore()
        val view = ControllerSurfaceView(targetContext, store) { HapticLevel.OFF }
        view.layout(0, 0, w, h)
        val bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        view.draw(canvas)
        // Composite the logo from the production resource at its proportional settings position.
        val logo = android.graphics.BitmapFactory.decodeResource(targetContext.resources, R.drawable.natsx_logo)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG).apply {
            colorFilter = android.graphics.PorterDuffColorFilter(Color.rgb(168, 139, 223), android.graphics.PorterDuff.Mode.SRC_IN)
        }
        val logoViewport = com.natsx.controller.feature.controller.ControllerDesignViewport.fit(w.toFloat(), h.toFloat())
        canvas.drawBitmap(logo, null, android.graphics.RectF(logoViewport.x(1148.7f), logoViewport.y(167.4f), logoViewport.x(1251.3f), logoViewport.y(270f)), paint)
        val capX = logoViewport.x(367f).toInt()
        val topGreen = Color.green(bitmap.getPixel(capX, logoViewport.y(455f).toInt()))
        val bottomGreen = Color.green(bitmap.getPixel(capX, logoViewport.y(585f).toInt()))
        check(bottomGreen - topGreen > 25) { "Recessed cap shading must not inherit the stipple alpha" }
        val directory = File(targetContext.getExternalFilesDir(null), "skin-verification").apply { mkdirs() }
        File(directory, "controller-${w}x$h.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        // Include non-zero cutout and bottom gesture insets in the real pointer checks.
        view.applySafeInsets(32, 8, 0, 16)
        val viewport = com.natsx.controller.feature.controller.ControllerDesignViewport.fit(w.toFloat(), h.toFloat(), 32f, 8f, 0f, 16f)
        fun point(x: Float, y: Float) = viewport.x(x) to viewport.y(y)
        val points = arrayListOf(point(367f, 520f), point(2150f, 134f), point(2025f, 692f), point(1485f, 774f))
        touch(view, MotionEvent.ACTION_DOWN, points.take(1))
        for (count in 2..4) touch(view, MotionEvent.ACTION_POINTER_DOWN or ((count - 1) shl 8), points.take(count))
        check(store.snapshot().rightTrigger == 255)
        check(store.snapshot().buttons and GamepadButtons.A != 0)
        points[0] = point(486f, 520f)
        points[3] = point(1485f, 682f)
        touch(view, MotionEvent.ACTION_MOVE, points)
        check(store.snapshot().leftX > 0 && store.snapshot().rightY > 0)
        touch(view, MotionEvent.ACTION_POINTER_UP or (2 shl 8), points)
        check(store.snapshot().buttons and GamepadButtons.A == 0)
        check(store.snapshot().rightTrigger == 255 && store.snapshot().leftX > 0)
        touch(view, MotionEvent.ACTION_CANCEL, points)
        check(store.snapshot() == GamepadState.Neutral)
        val diagonal = listOf(point(910f, 625f), point(1075f, 790f))
        touch(view, MotionEvent.ACTION_DOWN, diagonal.take(1))
        touch(view, MotionEvent.ACTION_POINTER_DOWN or (1 shl 8), diagonal)
        check(store.snapshot().dpad == (DpadState.UP or DpadState.RIGHT))
        touch(view, MotionEvent.ACTION_CANCEL, diagonal)
        check(store.snapshot() == GamepadState.Neutral)
        // Pressed screenshot is produced by the same View, using its authoritative state.
        store.setButton(GamepadButtons.A or GamepadButtons.RIGHT_SHOULDER, true)
        store.setLeftStick(24000, 12000)
        store.setDpad(DpadState.UP or DpadState.RIGHT, true)
        view.draw(canvas)
        File(directory, "pressed-${w}x$h.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        val skinField = ControllerSurfaceView::class.java.getDeclaredField("skin").apply { isAccessible = true }
        val skin = skinField.get(view)
        val backgroundField = skin.javaClass.getDeclaredField("background").apply { isAccessible = true }
        val cachedBackground = backgroundField.get(skin)
        repeat(60) {
            store.setLeftStick(it * 500, -it * 400)
            view.draw(canvas)
        }
        check(backgroundField.get(skin) === cachedBackground) { "Gameplay must reuse its raster cache" }
        bitmap.recycle()
        logo.recycle()
    }

    private fun touch(view: ControllerSurfaceView, action: Int, points: List<Pair<Float, Float>>) {
        val props = Array(points.size) { i -> MotionEvent.PointerProperties().apply { id = i; toolType = MotionEvent.TOOL_TYPE_FINGER } }
        val coords = Array(points.size) { i -> MotionEvent.PointerCoords().apply {
            x = points[i].first; y = points[i].second; pressure = 1f; size = 1f
        } }
        val time = SystemClock.uptimeMillis()
        val event = MotionEvent.obtain(time, time, action, points.size, props, coords, 0, 0, 1f, 1f, 0, 0, android.view.InputDevice.SOURCE_TOUCHSCREEN, 0)
        view.onTouchEvent(event)
        event.recycle()
    }
}
