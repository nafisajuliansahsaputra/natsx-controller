package com.natsx.controller.feature.controller

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.input.AnalogStickProcessor
import kotlin.math.min

class ControllerSurfaceView(
    context: Context,
    private val stateStore: GamepadStateStore,
) : View(context) {
    private val outlinePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        style = Paint.Style.STROKE
        strokeWidth = 3f
    }

    private val fillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(52, 52, 56)
        style = Paint.Style.FILL
    }

    private val activeFillPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.rgb(92, 92, 98)
        style = Paint.Style.FILL
    }

    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textAlign = Paint.Align.CENTER
        typeface = android.graphics.Typeface.DEFAULT_BOLD
    }

    private val leftStickProcessor = AnalogStickProcessor(deadzone = 0.05f)
    private val rightStickProcessor = AnalogStickProcessor(deadzone = 0.07f)

    private val controls = mutableListOf<ControlGeometry>()
    private val activePointers = mutableMapOf<Int, ControlId>()

    init {
        isClickable = true
        isFocusable = true
        setBackgroundColor(Color.rgb(9, 9, 11))
    }

    override fun onSizeChanged(
        width: Int,
        height: Int,
        oldWidth: Int,
        oldHeight: Int,
    ) {
        super.onSizeChanged(width, height, oldWidth, oldHeight)
        rebuildLayout(width.toFloat(), height.toFloat())
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        val state = stateStore.snapshot()

        controls.forEach { control ->
            when (control.shape) {
                Shape.CIRCLE -> drawCircleControl(canvas, control, state)
                Shape.RECT -> drawRectControl(canvas, control, state)
            }
        }

        drawStickKnob(canvas, ControlId.LEFT_STICK, state.leftX, state.leftY)
        drawStickKnob(canvas, ControlId.RIGHT_STICK, state.rightX, state.rightY)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN,
            MotionEvent.ACTION_POINTER_DOWN,
            -> handlePointerDown(event, event.actionIndex)

            MotionEvent.ACTION_MOVE -> handleMove(event)

            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_POINTER_UP,
            -> {
                handlePointerUp(event.getPointerId(event.actionIndex))
                performClick()
            }

            MotionEvent.ACTION_CANCEL -> releaseAllInputs()
        }

        invalidate()
        return true
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    fun releaseAllInputs() {
        activePointers.clear()
        leftStickProcessor.reset()
        rightStickProcessor.reset()
        stateStore.neutralize()
        invalidate()
    }

    private fun handlePointerDown(event: MotionEvent, pointerIndex: Int) {
        val pointerId = event.getPointerId(pointerIndex)
        val x = event.getX(pointerIndex)
        val y = event.getY(pointerIndex)

        val target = controls.firstOrNull { control ->
            control.contains(x, y) &&
                activePointers.values.none { it == control.id }
        } ?: return

        activePointers[pointerId] = target.id
        activate(target.id, x, y)

        if (target.id != ControlId.LEFT_STICK && target.id != ControlId.RIGHT_STICK) {
            performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
        }
    }

    private fun handleMove(event: MotionEvent) {
        for (index in 0 until event.pointerCount) {
            val pointerId = event.getPointerId(index)
            val controlId = activePointers[pointerId] ?: continue

            when (controlId) {
                ControlId.LEFT_STICK,
                ControlId.RIGHT_STICK,
                -> updateStick(controlId, event.getX(index), event.getY(index))

                else -> Unit
            }
        }
    }

    private fun handlePointerUp(pointerId: Int) {
        val controlId = activePointers.remove(pointerId) ?: return
        release(controlId)
    }

    private fun activate(
        controlId: ControlId,
        x: Float,
        y: Float,
    ) {
        when (controlId) {
            ControlId.LEFT_STICK,
            ControlId.RIGHT_STICK,
            -> updateStick(controlId, x, y)

            ControlId.A -> stateStore.setButton(GamepadButtons.A, true)
            ControlId.B -> stateStore.setButton(GamepadButtons.B, true)
            ControlId.X -> stateStore.setButton(GamepadButtons.X, true)
            ControlId.Y -> stateStore.setButton(GamepadButtons.Y, true)
            ControlId.LB -> stateStore.setButton(GamepadButtons.LEFT_SHOULDER, true)
            ControlId.RB -> stateStore.setButton(GamepadButtons.RIGHT_SHOULDER, true)
            ControlId.L3 -> stateStore.setButton(GamepadButtons.LEFT_STICK, true)
            ControlId.R3 -> stateStore.setButton(GamepadButtons.RIGHT_STICK, true)
            ControlId.BACK -> stateStore.setButton(GamepadButtons.BACK, true)
            ControlId.START -> stateStore.setButton(GamepadButtons.START, true)
            ControlId.GUIDE -> stateStore.setButton(GamepadButtons.GUIDE, true)
            ControlId.LT -> stateStore.setLeftTrigger(255)
            ControlId.RT -> stateStore.setRightTrigger(255)
            ControlId.DPAD_UP -> stateStore.setDpad(DpadState.UP, true)
            ControlId.DPAD_DOWN -> stateStore.setDpad(DpadState.DOWN, true)
            ControlId.DPAD_LEFT -> stateStore.setDpad(DpadState.LEFT, true)
            ControlId.DPAD_RIGHT -> stateStore.setDpad(DpadState.RIGHT, true)
        }
    }

    private fun release(controlId: ControlId) {
        when (controlId) {
            ControlId.LEFT_STICK -> {
                leftStickProcessor.reset()
                stateStore.setLeftStick(0, 0)
            }

            ControlId.RIGHT_STICK -> {
                rightStickProcessor.reset()
                stateStore.setRightStick(0, 0)
            }
            ControlId.A -> stateStore.setButton(GamepadButtons.A, false)
            ControlId.B -> stateStore.setButton(GamepadButtons.B, false)
            ControlId.X -> stateStore.setButton(GamepadButtons.X, false)
            ControlId.Y -> stateStore.setButton(GamepadButtons.Y, false)
            ControlId.LB -> stateStore.setButton(GamepadButtons.LEFT_SHOULDER, false)
            ControlId.RB -> stateStore.setButton(GamepadButtons.RIGHT_SHOULDER, false)
            ControlId.L3 -> stateStore.setButton(GamepadButtons.LEFT_STICK, false)
            ControlId.R3 -> stateStore.setButton(GamepadButtons.RIGHT_STICK, false)
            ControlId.BACK -> stateStore.setButton(GamepadButtons.BACK, false)
            ControlId.START -> stateStore.setButton(GamepadButtons.START, false)
            ControlId.GUIDE -> stateStore.setButton(GamepadButtons.GUIDE, false)
            ControlId.LT -> stateStore.setLeftTrigger(0)
            ControlId.RT -> stateStore.setRightTrigger(0)
            ControlId.DPAD_UP -> stateStore.setDpad(DpadState.UP, false)
            ControlId.DPAD_DOWN -> stateStore.setDpad(DpadState.DOWN, false)
            ControlId.DPAD_LEFT -> stateStore.setDpad(DpadState.LEFT, false)
            ControlId.DPAD_RIGHT -> stateStore.setDpad(DpadState.RIGHT, false)
        }
    }

    private fun updateStick(
        controlId: ControlId,
        x: Float,
        y: Float,
    ) {
        val geometry = controls.firstOrNull { it.id == controlId } ?: return
        val processor =
            if (controlId == ControlId.LEFT_STICK) {
                leftStickProcessor
            } else {
                rightStickProcessor
            }

        val output = processor.process(
            pointerX = x,
            pointerY = y,
            centerX = geometry.centerX,
            centerY = geometry.centerY,
            radius = geometry.radius,
        )

        if (controlId == ControlId.LEFT_STICK) {
            stateStore.setLeftStick(output.x, output.y)
        } else {
            stateStore.setRightStick(output.x, output.y)
        }
    }

    private fun rebuildLayout(width: Float, height: Float) {
        controls.clear()

        if (width <= 0f || height <= 0f) {
            return
        }

        val unit = min(width, height)

        fun circle(
            id: ControlId,
            x: Float,
            y: Float,
            radius: Float,
            hitScale: Float = 1.16f,
        ) {
            controls += ControlGeometry.circle(
                id = id,
                centerX = width * x,
                centerY = height * y,
                radius = unit * radius,
                hitRadius = unit * radius * hitScale,
            )
        }

        fun rect(
            id: ControlId,
            left: Float,
            top: Float,
            right: Float,
            bottom: Float,
            hitPadding: Float = 0.018f,
        ) {
            controls += ControlGeometry.rect(
                id = id,
                rect = RectF(
                    width * left,
                    height * top,
                    width * right,
                    height * bottom,
                ),
                hitPadding = unit * hitPadding,
            )
        }

        rect(ControlId.LT, 0.025f, 0.035f, 0.125f, 0.165f)
        rect(ControlId.LB, 0.145f, 0.035f, 0.245f, 0.165f)
        rect(ControlId.RB, 0.755f, 0.035f, 0.855f, 0.165f)
        rect(ControlId.RT, 0.875f, 0.035f, 0.975f, 0.165f)

        circle(ControlId.BACK, 0.435f, 0.30f, 0.033f, 1.20f)
        circle(ControlId.L3, 0.485f, 0.30f, 0.033f, 1.20f)
        circle(ControlId.R3, 0.535f, 0.30f, 0.033f, 1.20f)
        circle(ControlId.START, 0.585f, 0.30f, 0.033f, 1.20f)
        circle(ControlId.GUIDE, 0.510f, 0.145f, 0.028f, 1.25f)

        circle(ControlId.LEFT_STICK, 0.165f, 0.625f, 0.125f, 1.10f)

        circle(ControlId.DPAD_UP, 0.475f, 0.535f, 0.040f, 1.14f)
        circle(ControlId.DPAD_LEFT, 0.435f, 0.625f, 0.040f, 1.14f)
        circle(ControlId.DPAD_RIGHT, 0.515f, 0.625f, 0.040f, 1.14f)
        circle(ControlId.DPAD_DOWN, 0.475f, 0.715f, 0.040f, 1.14f)

        circle(ControlId.RIGHT_STICK, 0.640f, 0.655f, 0.070f, 1.20f)

        circle(ControlId.Y, 0.815f, 0.455f, 0.054f, 1.18f)
        circle(ControlId.X, 0.745f, 0.610f, 0.054f, 1.18f)
        circle(ControlId.B, 0.885f, 0.610f, 0.054f, 1.18f)
        circle(ControlId.A, 0.815f, 0.765f, 0.054f, 1.18f)
    }

    private fun drawCircleControl(
        canvas: Canvas,
        control: ControlGeometry,
        state: GamepadState,
    ) {
        val active = isControlActive(control.id, state)
        canvas.drawCircle(
            control.centerX,
            control.centerY,
            control.radius,
            if (active) activeFillPaint else fillPaint,
        )

        canvas.drawCircle(
            control.centerX,
            control.centerY,
            control.radius,
            outlinePaint,
        )

        val label = controlLabel(control.id)
        if (label.isNotEmpty()) {
            textPaint.textSize = control.radius * if (label.length > 2) 0.50f else 0.70f
            drawCenteredText(canvas, label, control.centerX, control.centerY, textPaint)
        }
    }

    private fun drawRectControl(
        canvas: Canvas,
        control: ControlGeometry,
        state: GamepadState,
    ) {
        val active = isControlActive(control.id, state)
        val rect = control.rect ?: return
        val radius = min(rect.width(), rect.height()) * 0.32f

        canvas.drawRoundRect(
            rect,
            radius,
            radius,
            if (active) activeFillPaint else fillPaint,
        )
        canvas.drawRoundRect(rect, radius, radius, outlinePaint)

        textPaint.textSize = rect.height() * 0.42f
        drawCenteredText(canvas, controlLabel(control.id), rect.centerX(), rect.centerY(), textPaint)
    }

    private fun drawStickKnob(
        canvas: Canvas,
        id: ControlId,
        logicalX: Int,
        logicalY: Int,
    ) {
        val control = controls.firstOrNull { it.id == id } ?: return
        val x = logicalX / 32767f
        val y =
            if (logicalY >= 0) {
                logicalY / 32767f
            } else {
                logicalY / 32768f
            }

        val knobRadius = control.radius * 0.42f
        val travel = control.radius * 0.48f
        val knobX = control.centerX + x.coerceIn(-1f, 1f) * travel
        val knobY = control.centerY - y.coerceIn(-1f, 1f) * travel

        canvas.drawCircle(knobX, knobY, knobRadius, activeFillPaint)
        canvas.drawCircle(knobX, knobY, knobRadius, outlinePaint)
    }

    private fun isControlActive(
        id: ControlId,
        state: GamepadState,
    ): Boolean {
        return when (id) {
            ControlId.LEFT_STICK -> state.leftX != 0 || state.leftY != 0
            ControlId.RIGHT_STICK -> state.rightX != 0 || state.rightY != 0
            ControlId.A -> state.buttons and GamepadButtons.A != 0
            ControlId.B -> state.buttons and GamepadButtons.B != 0
            ControlId.X -> state.buttons and GamepadButtons.X != 0
            ControlId.Y -> state.buttons and GamepadButtons.Y != 0
            ControlId.LB -> state.buttons and GamepadButtons.LEFT_SHOULDER != 0
            ControlId.RB -> state.buttons and GamepadButtons.RIGHT_SHOULDER != 0
            ControlId.L3 -> state.buttons and GamepadButtons.LEFT_STICK != 0
            ControlId.R3 -> state.buttons and GamepadButtons.RIGHT_STICK != 0
            ControlId.BACK -> state.buttons and GamepadButtons.BACK != 0
            ControlId.START -> state.buttons and GamepadButtons.START != 0
            ControlId.GUIDE -> state.buttons and GamepadButtons.GUIDE != 0
            ControlId.LT -> state.leftTrigger > 0
            ControlId.RT -> state.rightTrigger > 0
            ControlId.DPAD_UP -> state.dpad and DpadState.UP != 0
            ControlId.DPAD_DOWN -> state.dpad and DpadState.DOWN != 0
            ControlId.DPAD_LEFT -> state.dpad and DpadState.LEFT != 0
            ControlId.DPAD_RIGHT -> state.dpad and DpadState.RIGHT != 0
        }
    }

    private fun controlLabel(id: ControlId): String {
        return when (id) {
            ControlId.LEFT_STICK,
            ControlId.RIGHT_STICK,
            -> ""

            ControlId.A -> "A"
            ControlId.B -> "B"
            ControlId.X -> "X"
            ControlId.Y -> "Y"
            ControlId.LB -> "LB"
            ControlId.RB -> "RB"
            ControlId.LT -> "LT"
            ControlId.RT -> "RT"
            ControlId.L3 -> "L3"
            ControlId.R3 -> "R3"
            ControlId.BACK -> "‹"
            ControlId.START -> "›"
            ControlId.GUIDE -> "●"
            ControlId.DPAD_UP -> "▲"
            ControlId.DPAD_DOWN -> "▼"
            ControlId.DPAD_LEFT -> "◀"
            ControlId.DPAD_RIGHT -> "▶"
        }
    }

    private fun drawCenteredText(
        canvas: Canvas,
        text: String,
        x: Float,
        y: Float,
        paint: Paint,
    ) {
        val metrics = paint.fontMetrics
        val baseline = y - (metrics.ascent + metrics.descent) / 2f
        canvas.drawText(text, x, baseline, paint)
    }

    private enum class ControlId {
        LEFT_STICK,
        RIGHT_STICK,
        A,
        B,
        X,
        Y,
        LB,
        RB,
        LT,
        RT,
        L3,
        R3,
        BACK,
        START,
        GUIDE,
        DPAD_UP,
        DPAD_DOWN,
        DPAD_LEFT,
        DPAD_RIGHT,
    }

    private enum class Shape {
        CIRCLE,
        RECT,
    }

    private data class ControlGeometry(
        val id: ControlId,
        val shape: Shape,
        val centerX: Float,
        val centerY: Float,
        val radius: Float,
        val hitRadius: Float,
        val rect: RectF?,
        val hitPadding: Float,
    ) {
        fun contains(x: Float, y: Float): Boolean {
            return when (shape) {
                Shape.CIRCLE -> {
                    val dx = x - centerX
                    val dy = y - centerY
                    dx * dx + dy * dy <= hitRadius * hitRadius
                }

                Shape.RECT -> {
                    val visual = rect ?: return false
                    x >= visual.left - hitPadding &&
                        x <= visual.right + hitPadding &&
                        y >= visual.top - hitPadding &&
                        y <= visual.bottom + hitPadding
                }
            }
        }

        companion object {
            fun circle(
                id: ControlId,
                centerX: Float,
                centerY: Float,
                radius: Float,
                hitRadius: Float,
            ) = ControlGeometry(
                id = id,
                shape = Shape.CIRCLE,
                centerX = centerX,
                centerY = centerY,
                radius = radius,
                hitRadius = hitRadius,
                rect = null,
                hitPadding = 0f,
            )

            fun rect(
                id: ControlId,
                rect: RectF,
                hitPadding: Float,
            ) = ControlGeometry(
                id = id,
                shape = Shape.RECT,
                centerX = rect.centerX(),
                centerY = rect.centerY(),
                radius = 0f,
                hitRadius = 0f,
                rect = rect,
                hitPadding = hitPadding,
            )
        }
    }
}
