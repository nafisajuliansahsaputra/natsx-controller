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
                UsbInputVerification.verify(activity) { block -> runOnMainSync { block() } }
            } catch (failure: Throwable) {
                error = failure
            } finally {
                runOnMainSync { activity.finish() }
            }
        }
        val result = Bundle()
        result.putString("stream", error?.stackTraceToString() ?: "PASS: native rendering, safe insets, multitouch, diagonals, release/cancel at three aspect ratios; Activity pairing confirm/reject and input unblock; Activity touch to authenticated USB packets\n")
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
            colorFilter = android.graphics.PorterDuffColorFilter(Color.rgb(195, 177, 225), android.graphics.PorterDuff.Mode.SRC_IN)
        }
        val logoViewport = com.natsx.controller.feature.controller.ControllerDesignViewport.fit(w.toFloat(), h.toFloat())
        val logoLeft = logoViewport.x(1198.201f) - 30f * logoViewport.scale
        val logoTop = logoViewport.y(120f)
        canvas.drawBitmap(logo, null, android.graphics.RectF(logoLeft, logoTop,
            logoLeft + 60f * logoViewport.scale, logoTop + 60f * logoViewport.scale), paint)
        // Figma cap is a radial gradient and native layers retain the saturated outer ring.
        val center = bitmap.getPixel(logoViewport.x(325f).toInt(), logoViewport.y(540f).toInt())
        val rim = bitmap.getPixel(logoViewport.x(535f).toInt(), logoViewport.y(540f).toInt())
        check(Color.green(center) > Color.red(center) + 20)
        check(Color.green(rim) > Color.red(rim) + 50)
        check(Color.green(center) > Color.green(rim))
        // The third circle moves as a complete cap; the green socket stays concentric/fixed.
        val stickX = logoViewport.x(325f)
        val stickY = logoViewport.y(540f)
        val scale = logoViewport.scale
        // Every newly enlarged face-button edge acquires exactly that button;
        // the decorative glow and empty corner outside its circle do not.
        for ((cx, cy, button) in listOf(
            Triple(2074.62f, 349.62f, GamepadButtons.Y),
            Triple(1884.62f, 539.62f, GamepadButtons.X),
            Triple(2265.62f, 539.62f, GamepadButtons.B),
            Triple(2074.62f, 730.62f, GamepadButtons.A),
        )) {
            val x = logoViewport.x(2074.62f) + (cx - 2074.62f) * scale
            val y = logoViewport.y(539.62f) + (cy - 539.62f) * scale
            for ((dx, dy) in listOf(1f to 0f, -1f to 0f, 0f to 1f, 0f to -1f)) {
                val edge = listOf((x + dx * 114.62f * scale) to (y + dy * 114.62f * scale))
                touch(view, MotionEvent.ACTION_DOWN, edge)
                check(store.snapshot() == GamepadState(buttons = button)) { "Enlarged button edge must acquire $button" }
                touch(view, MotionEvent.ACTION_UP, edge)
                check(store.snapshot() == GamepadState.Neutral)
                val outside = listOf((x + dx * 116.62f * scale) to (y + dy * 116.62f * scale))
                touch(view, MotionEvent.ACTION_DOWN, outside)
                check(store.snapshot() == GamepadState.Neutral) { "Glow must not acquire $button" }
                touch(view, MotionEvent.ACTION_CANCEL, outside)
            }
            val corner = listOf((x + 100f * scale) to (y + 100f * scale))
            touch(view, MotionEvent.ACTION_DOWN, corner)
            check(store.snapshot() == GamepadState.Neutral) { "Face-button hitbox must remain circular" }
            touch(view, MotionEvent.ACTION_CANCEL, corner)
        }
        // Matching outer bevels at the same radial position on both sticks.
        for ((dx, dy) in listOf(248f to 0f, -248f to 0f, 0f to -248f, 0f to 248f)) {
            val left = bitmap.getPixel((stickX + dx * scale).toInt(), (stickY + dy * scale).toInt())
            val right = bitmap.getPixel((logoViewport.x(1500f) + dx * scale).toInt(),
                (logoViewport.y(780f) + dy * scale).toInt())
            check(kotlin.math.abs(Color.red(left) - Color.red(right)) <= 2 &&
                kotlin.math.abs(Color.green(left) - Color.green(right)) <= 2 &&
                kotlin.math.abs(Color.blue(left) - Color.blue(right)) <= 2) {
                "Right analog bevel must match left at $dx,$dy: $left vs $right"
            }
        }
        fun pixel(dx: Float, dy: Float = 0f) = bitmap.getPixel((stickX + dx * scale).toInt(), (stickY + dy * scale).toInt())
        val idleThird = pixel(-145f)
        val idleSocket = pixel(205f)
        val fixedSocket = pixel(-215f)
        store.setLeftStick(32767, 0)
        view.draw(canvas)
        check(Color.red(pixel(205f)) > Color.red(idleSocket) + 30) { "Third circle must move, not just the second" }
        check(Color.red(idleThird) > Color.red(pixel(-145f)) + 10) { "Moving cap must expose the socket" }
        check(pixel(-215f) == fixedSocket) { "Outer socket must stay fixed" }
        store.neutralize()
        view.draw(canvas)
        check(pixel(-145f) == idleThird) { "Cap must return to center on release" }
        // Original Figma PNG exports retain their effects and texture.
        check(bitmap.getPixel(5, h / 2) == Color.rgb(250, 250, 250))
        val texture = (0..8).map { pixel(-60f + it * 15f, 20f) }
        check(texture.toSet().size > 4) { "Original Figma stick texture must be preserved" }
        // Added bevels retain neutral white backplates and matching gray depth.
        for ((x, y) in listOf(625f to 44f, 1775f to 44f, 792.65f to 247f, 1607.65f to 247f,
            864.65f to 372f, 1534.65f to 372f, 1199.65f to 372f)) {
            val cy = if (y < 200f) 125f else if (y < 300f) 287.5f else 412.5f
            val at = bitmap.getPixel(logoViewport.x(x).toInt(), (logoViewport.y(cy) + (y-cy)*scale).toInt())
            check(Color.red(at) > 90 && Color.red(at) <= 255 &&
                kotlin.math.abs(Color.red(at) - Color.green(at)) <= 3) { "Matching bevel missing at $x,$y" }
        }
        // The same authoritative transport colors both the light and all three contours.
        val status = com.natsx.controller.feature.controller.ControllerStatusView(targetContext)
        status.layout(0, 0, w, h)
        val expected = listOf(
            com.natsx.controller.feature.controller.ControllerTransportIndicator.WIFI to Color.rgb(178,235,178),
            com.natsx.controller.feature.controller.ControllerTransportIndicator.USB to Color.rgb(170,139,232),
            com.natsx.controller.feature.controller.ControllerTransportIndicator.BLUETOOTH to Color.rgb(246,246,250),
            null to Color.rgb(185,187,194),
        )
        for ((transport, color) in expected) {
            view.updateActiveTransport(transport)
            view.draw(canvas)
            status.updateState(com.natsx.controller.feature.controller.ControllerHudState(activeTransport = transport))
            status.draw(canvas)
            val lamp = bitmap.getPixel(logoViewport.x(1200f).toInt(), logoViewport.y(52.5f).toInt())
            check(lamp == color) { "Light must show $transport: $lamp != $color" }
            for ((sx, sy) in listOf(1200f to 325.5f, 200f to 869.5f, 2200f to 869.5f)) {
                val x = logoViewport.x(sx).toInt(); val y = logoViewport.y(sy).toInt()
                val stroke = (-2..2).map { bitmap.getPixel(x, (y+it).coerceIn(0,h-1)) }.minBy {
                    kotlin.math.abs(Color.red(it)-Color.red(color)) +
                        kotlin.math.abs(Color.green(it)-Color.green(color)) + kotlin.math.abs(Color.blue(it)-Color.blue(color))
                }
                val distance = kotlin.math.abs(Color.red(stroke)-Color.red(color)) +
                    kotlin.math.abs(Color.green(stroke)-Color.green(color)) + kotlin.math.abs(Color.blue(stroke)-Color.blue(color))
                check(distance <= 110) { "Contour must follow $transport at $sx,$sy: $stroke vs $color" }
            }
            check(store.snapshot() == GamepadState.Neutral) { "Connection indicators cannot change input" }
        }
        view.updateActiveTransport(com.natsx.controller.feature.controller.ControllerTransportIndicator.USB)
        view.draw(canvas)
        val directory = File(targetContext.getExternalFilesDir(null), "skin-verification").apply { mkdirs() }
        // Composite the production HUD and settings logo for a complete gameplay preview.
        status.updateState(com.natsx.controller.feature.controller.ControllerHudState(
            activeTransport = com.natsx.controller.feature.controller.ControllerTransportIndicator.USB,
            batteryPercent = 68,
        ))
        status.draw(canvas)
        canvas.drawBitmap(logo, null, android.graphics.RectF(logoLeft, logoTop,
            logoLeft + 60f * scale, logoTop + 60f * scale), paint)
        File(directory, "controller-${w}x$h.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        // Full travel stays responsive past the plate, clamps, and releases immediately.
        for ((cx, cy, leftStick) in listOf(Triple(325f, 540f, true), Triple(1500f, 780f, false))) {
            val centerPoint = listOf(logoViewport.x(cx) to logoViewport.y(cy))
            touch(view, MotionEvent.ACTION_DOWN, centerPoint)
            val beyondPlate = listOf((logoViewport.x(cx) + 280f * scale) to logoViewport.y(cy))
            touch(view, MotionEvent.ACTION_MOVE, beyondPlate)
            check(if (leftStick) store.snapshot().leftX == 32767 else store.snapshot().rightX == 32767)
            touch(view, MotionEvent.ACTION_CANCEL, beyondPlate)
            check(store.snapshot() == GamepadState.Neutral)
        }
        // The outer portion of the stick capture surface also accepts analog input.
        val capEdge = listOf((stickX + 190f * scale) to stickY)
        touch(view, MotionEvent.ACTION_DOWN, capEdge)
        check(store.snapshot().leftX > 0) { "Third circle edge must accept stick input" }
        touch(view, MotionEvent.ACTION_CANCEL, capEdge)
        check(store.snapshot() == GamepadState.Neutral)
        // Include non-zero cutout and bottom gesture insets in the real pointer checks.
        view.applySafeInsets(32, 8, 0, 16)
        val viewport = com.natsx.controller.feature.controller.ControllerDesignViewport.fit(w.toFloat(), h.toFloat(), 32f, 8f, 0f, 16f)
        fun point(x: Float, y: Float) = viewport.x(x) to viewport.y(y)
        val points = arrayListOf(point(325f, 540f), point(2175f, 125f), point(2074.62f, 730.62f), point(1500f, 780f))
        touch(view, MotionEvent.ACTION_DOWN, points.take(1))
        for (count in 2..4) touch(view, MotionEvent.ACTION_POINTER_DOWN or ((count - 1) shl 8), points.take(count))
        check(store.snapshot().rightTrigger == 255)
        check(store.snapshot().buttons and GamepadButtons.A != 0)
        points[0] = point(434f, 518f)
        points[3] = point(1501f, 664f)
        touch(view, MotionEvent.ACTION_MOVE, points)
        check(store.snapshot().leftX > 0 && store.snapshot().rightY > 0)
        touch(view, MotionEvent.ACTION_POINTER_UP or (2 shl 8), points)
        check(store.snapshot().buttons and GamepadButtons.A == 0)
        check(store.snapshot().rightTrigger == 255 && store.snapshot().leftX > 0)
        touch(view, MotionEvent.ACTION_CANCEL, points)
        check(store.snapshot() == GamepadState.Neutral)
        val diagonal = listOf(point(900f, 620f), point(1060f, 780f))
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
            view.updateActiveTransport(expected[it % expected.size].first)
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
