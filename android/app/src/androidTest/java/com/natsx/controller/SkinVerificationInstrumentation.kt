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
                for ((w, h) in listOf(1870 to 841, 2400 to 1080, 1920 to 1080)) verify(w, h)
            } catch (failure: Throwable) {
                error = failure
            }
        }
        val result = Bundle()
        result.putString("stream", error?.stackTraceToString() ?: "PASS: native rendering, safe insets, multitouch, diagonals, release/cancel at three aspect ratios\n")
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
        canvas.drawBitmap(logo, null, android.graphics.RectF(w / 2f - h * 0.0475f, h * 0.155f, w / 2f + h * 0.0475f, h * 0.250f), paint)
        val directory = File(targetContext.getExternalFilesDir(null), "skin-verification").apply { mkdirs() }
        File(directory, "controller-${w}x$h.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        // Include non-zero cutout and bottom gesture insets in the real pointer checks.
        view.applySafeInsets(32, 8, 0, 16)
        val uw = w - 64f; val uh = h - 24f
        fun point(x: Float, y: Float) = 32f + uw * x to 8f + uh * y
        val points = arrayListOf(point(0.153f, 0.474f), point(0.897f, 0.120f), point(0.844f, 0.640f), point(0.620f, 0.710f))
        touch(view, MotionEvent.ACTION_DOWN, points.take(1))
        for (count in 2..4) touch(view, MotionEvent.ACTION_POINTER_DOWN or ((count - 1) shl 8), points.take(count))
        check(store.snapshot().rightTrigger == 255)
        check(store.snapshot().buttons and GamepadButtons.A != 0)
        points[0] = point(0.153f + uh * 0.11f / uw, 0.474f)
        points[3] = point(0.620f, 0.625f)
        touch(view, MotionEvent.ACTION_MOVE, points)
        check(store.snapshot().leftX > 0 && store.snapshot().rightY > 0)
        touch(view, MotionEvent.ACTION_POINTER_UP or (2 shl 8), points)
        check(store.snapshot().buttons and GamepadButtons.A == 0)
        check(store.snapshot().rightTrigger == 255 && store.snapshot().leftX > 0)
        touch(view, MotionEvent.ACTION_CANCEL, points)
        check(store.snapshot() == GamepadState.Neutral)
        val diagonal = listOf(point(0.380f, 0.582f), point(0.380f + uh * 0.140f / uw, 0.722f))
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
