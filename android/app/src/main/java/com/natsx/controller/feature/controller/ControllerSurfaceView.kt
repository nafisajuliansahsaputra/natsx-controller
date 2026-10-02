package com.natsx.controller.feature.controller

import android.content.Context
import android.graphics.Canvas
import android.graphics.RectF
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import com.natsx.controller.core.gamepad.DpadState
import com.natsx.controller.core.gamepad.GamepadButtons
import com.natsx.controller.core.gamepad.GamepadState
import com.natsx.controller.core.gamepad.GamepadStateStore
import com.natsx.controller.core.haptics.HapticLevel
import com.natsx.controller.core.input.AnalogStickProcessor
import com.natsx.controller.core.input.ControllerInputTuning
import com.natsx.controller.core.input.ControllerStickCalibration
import kotlin.math.max
import kotlin.math.min

class ControllerSurfaceView(
    context: Context,
    private val stateStore: GamepadStateStore,
    private val hapticLevel: () -> HapticLevel = {
        HapticLevel.MEDIUM
    },
) : View(context) {
    private val skin = ControllerSkin()

    private var inputTuning =
        ControllerInputTuning.Default
    private var stickCalibration =
        ControllerStickCalibration.Default

    private var leftStickProcessor =
        createLeftStickProcessor()
    private var rightStickProcessor =
        createRightStickProcessor()

    private val controls = mutableListOf<ControlGeometry>()
    private val pointerOwnership =
        PointerOwnershipTracker<ControlId>()

    private var controllerLayout =
        ControllerLayout.Default
    private var layoutEditing =
        false
    private var layoutEditPointerId:
        Int? = null
    private var selectedLayoutControl:
        ControlId? = null
    private var onLayoutChanged:
        ((ControllerLayout) -> Unit)? = null

    private var safeInsetLeft = 0f
    private var safeInsetTop = 0f
    private var safeInsetRight = 0f
    private var safeInsetBottom = 0f

    init {
        isClickable = true
        isFocusable = true
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

        skin.drawBackground(canvas)
        skin.drawDpad(canvas, state.dpad)
        for (control in controls) {
            if (isDpadControl(control.id)) continue
            val active = isControlActive(control.id, state) ||
                pointerOwnership.isControlClaimed(control.id) ||
                (layoutEditing && selectedLayoutControl == control.id)
            when (control.id) {
                ControlId.LEFT_STICK -> skin.drawStick(canvas, control.id.name, state.leftX, state.leftY, active)
                ControlId.RIGHT_STICK -> skin.drawStick(canvas, control.id.name, state.rightX, state.rightY, active)
                else -> skin.drawButton(canvas, control.id.name, active)
            }
        }
        if (layoutEditing) {
            for (control in controls) {
                skin.drawEditorTarget(canvas, control.centerX, control.centerY,
                    control.id == selectedLayoutControl)
            }
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (layoutEditing) {
            return handleLayoutEditorTouch(
                event,
            )
        }

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
        pointerOwnership.clear()
        leftStickProcessor.reset()
        rightStickProcessor.reset()
        stateStore.neutralize()
        invalidate()
    }

    fun applyInputTuning(
        tuning: ControllerInputTuning,
    ) {
        inputTuning = tuning
        rebuildStickProcessors()
    }

    fun applyStickCalibration(
        calibration: ControllerStickCalibration,
    ) {
        stickCalibration = calibration
        rebuildStickProcessors()
    }

    fun applyControllerLayout(
        layout: ControllerLayout,
    ) {
        controllerLayout = layout
        releaseAllInputs()
        rebuildLayout(
            width.toFloat(),
            height.toFloat(),
        )
        invalidate()
    }

    fun currentControllerLayout():
        ControllerLayout =
        controllerLayout

    fun applySafeInsets(
        left: Int,
        top: Int,
        right: Int,
        bottom: Int,
    ) {
        val horizontalInset =
            max(
                left.coerceAtLeast(0),
                right.coerceAtLeast(0),
            )
                .toFloat()
        val nextLeft =
            horizontalInset
        val nextTop =
            top.coerceAtLeast(0)
                .toFloat()
        val nextRight =
            horizontalInset
        val nextBottom =
            bottom.coerceAtLeast(0)
                .toFloat()

        if (
            safeInsetLeft == nextLeft &&
            safeInsetTop == nextTop &&
            safeInsetRight == nextRight &&
            safeInsetBottom == nextBottom
        ) {
            return
        }

        releaseAllInputs()

        safeInsetLeft = nextLeft
        safeInsetTop = nextTop
        safeInsetRight = nextRight
        safeInsetBottom = nextBottom

        rebuildLayout(
            width.toFloat(),
            height.toFloat(),
        )
        invalidate()
    }

    fun setLayoutEditing(
        enabled: Boolean,
        onChanged:
            ((ControllerLayout) -> Unit)? =
            null,
    ) {
        releaseAllInputs()
        layoutEditing = enabled
        onLayoutChanged =
            if (enabled) {
                onChanged
            } else {
                null
            }

        layoutEditPointerId = null
        selectedLayoutControl = null
        invalidate()
    }

    private fun rebuildStickProcessors() {
        releaseAllInputs()
        leftStickProcessor =
            createLeftStickProcessor()
        rightStickProcessor =
            createRightStickProcessor()
        invalidate()
    }

    private fun createLeftStickProcessor() =
        AnalogStickProcessor(
            deadzone =
                inputTuning.leftDeadzone,
            sensitivity =
                inputTuning.leftSensitivity,
            calibration =
                stickCalibration.left,
        )

    private fun createRightStickProcessor() =
        AnalogStickProcessor(
            deadzone =
                inputTuning.rightDeadzone,
            sensitivity =
                inputTuning.rightSensitivity,
            calibration =
                stickCalibration.right,
        )

    private fun handleLayoutEditorTouch(
        event: MotionEvent,
    ): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                val x = event.x
                val y = event.y

                val target =
                    controls
                        .asReversed()
                        .firstOrNull {
                            it.contains(
                                x,
                                y,
                            )
                        }

                if (target != null) {
                    layoutEditPointerId =
                        event.getPointerId(
                            0,
                        )
                    selectedLayoutControl =
                        target.id

                    moveLayoutControl(
                        target.id,
                        x,
                        y,
                    )
                }
            }

            MotionEvent.ACTION_MOVE -> {
                val pointerId =
                    layoutEditPointerId

                val controlId =
                    selectedLayoutControl

                if (
                    pointerId != null &&
                    controlId != null
                ) {
                    val index =
                        event.findPointerIndex(
                            pointerId,
                        )

                    if (index >= 0) {
                        moveLayoutControl(
                            controlId,
                            event.getX(index),
                            event.getY(index),
                        )
                    }
                }
            }

            MotionEvent.ACTION_UP -> {
                val controlId =
                    selectedLayoutControl

                if (controlId != null) {
                    moveLayoutControl(
                        controlId,
                        event.x,
                        event.y,
                    )

                    onLayoutChanged
                        ?.invoke(
                            controllerLayout,
                        )
                }

                layoutEditPointerId = null
                performClick()
            }

            MotionEvent.ACTION_CANCEL -> {
                layoutEditPointerId = null
            }
        }

        invalidate()
        return true
    }

    private fun moveLayoutControl(
        controlId: ControlId,
        x: Float,
        y: Float,
    ) {
        if (
            width <= 0 ||
            height <= 0
        ) {
            return
        }

        val geometry =
            controls.firstOrNull {
                it.id == controlId
            } ?: return

        val contentWidth = (width - safeInsetLeft - safeInsetRight).coerceAtLeast(1f)
        val contentHeight = (height - safeInsetTop - safeInsetBottom).coerceAtLeast(1f)

        val halfWidth =
            when (
                geometry.shape
            ) {
                Shape.CIRCLE ->
                    geometry.radius /
                        contentWidth
                Shape.RECT ->
                    (
                        geometry.rect
                            ?.width()
                            ?: 0f
                    ) /
                        2f /
                        contentWidth
            }

        val halfHeight =
            when (
                geometry.shape
            ) {
                Shape.CIRCLE ->
                    geometry.radius /
                        contentHeight
                Shape.RECT ->
                    (
                        geometry.rect
                            ?.height()
                            ?: 0f
                    ) /
                        2f /
                        contentHeight
            }

        val normalizedX =
            (
                (x - safeInsetLeft) /
                    contentWidth
            )
                .coerceIn(
                    (halfWidth + 0.01f)
                        .coerceAtMost(0.49f),
                    (1f - halfWidth - 0.01f)
                        .coerceAtLeast(0.51f),
                )

        val normalizedY =
            (
                (y - safeInsetTop) /
                    contentHeight
            )
                .coerceIn(
                    (halfHeight + 0.01f)
                        .coerceAtMost(0.49f),
                    (1f - halfHeight - 0.01f)
                        .coerceAtLeast(0.51f),
                )

        controllerLayout =
            controllerLayout
                .withPosition(
                    controlId.name,
                    normalizedX,
                    normalizedY,
                )

        rebuildLayout(
            width.toFloat(),
            height.toFloat(),
        )
    }

    private fun handlePointerDown(event: MotionEvent, pointerIndex: Int) {
        val pointerId = event.getPointerId(pointerIndex)
        val x = event.getX(pointerIndex)
        val y = event.getY(pointerIndex)

        val target =
            controls.firstOrNull { control ->
                control.contains(
                    x,
                    y,
                ) &&
                    !pointerOwnership
                        .isControlClaimed(
                            control.id,
                        )
            } ?: return

        if (
            !pointerOwnership.tryClaim(
                pointerId,
                target.id,
            )
        ) {
            return
        }

        activate(
            target.id,
            x,
            y,
        )

        if (
            target.id != ControlId.LEFT_STICK &&
            target.id != ControlId.RIGHT_STICK
        ) {
            performTouchHaptic()
        }
    }

    private fun performTouchHaptic() {
        val feedback =
            when (hapticLevel()) {
                HapticLevel.OFF ->
                    null

                HapticLevel.LOW ->
                    HapticFeedbackConstants.CLOCK_TICK

                HapticLevel.MEDIUM ->
                    HapticFeedbackConstants.KEYBOARD_TAP

                HapticLevel.HIGH ->
                    HapticFeedbackConstants.LONG_PRESS
            }

        if (feedback != null) {
            performHapticFeedback(
                feedback,
            )
        }
    }

    private fun handleMove(event: MotionEvent) {
        for (index in 0 until event.pointerCount) {
            val pointerId =
                event.getPointerId(
                    index,
                )
            val controlId =
                pointerOwnership.controlFor(
                    pointerId,
                )
                    ?: continue
            val x =
                event.getX(
                    index,
                )
            val y =
                event.getY(
                    index,
                )

            when (controlId) {
                ControlId.LEFT_STICK,
                ControlId.RIGHT_STICK,
                -> updateStick(
                    controlId,
                    x,
                    y,
                )

                else -> {
                    val geometry =
                        controls.firstOrNull {
                            it.id ==
                                controlId
                        }

                    if (
                        geometry != null &&
                        !geometry.containsForActivePointer(
                            x,
                            y,
                        )
                    ) {
                        pointerOwnership.release(
                            pointerId,
                        )
                        release(
                            controlId,
                        )
                    }
                }
            }
        }
    }

    private fun handlePointerUp(pointerId: Int) {
        val controlId =
            pointerOwnership.release(
                pointerId,
            ) ?: return

        release(
            controlId,
        )
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

        val contentWidth =
            (
                width -
                    safeInsetLeft -
                    safeInsetRight
            ).coerceAtLeast(
                1f,
            )
        val contentHeight =
            (
                height -
                    safeInsetTop -
                    safeInsetBottom
            ).coerceAtLeast(
                1f,
            )
        val unit =
            min(
                contentWidth,
                contentHeight,
            )

        fun circle(
            id: ControlId,
            x: Float,
            y: Float,
            radius: Float,
            hitScale: Float = 1.16f,
        ) {
            val position =
                controllerLayout
                    .positionFor(
                        id.name,
                        x,
                        y,
                    )

            controls += ControlGeometry.circle(
                id = id,
                centerX =
                    safeInsetLeft + contentWidth *
                            position.x,
                centerY =
                    safeInsetTop + contentHeight *
                            position.y,
                radius = unit * radius,
                hitRadius =
                    unit *
                        radius *
                        hitScale,
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
            val defaultX =
                (left + right) /
                    2f
            val defaultY =
                (top + bottom) /
                    2f

            val position =
                controllerLayout
                    .positionFor(
                        id.name,
                        defaultX,
                        defaultY,
                    )

            val halfWidth =
                (right - left) /
                    2f
            val halfHeight =
                (bottom - top) /
                    2f

            controls += ControlGeometry.rect(
                id = id,
                rect =
                    RectF(
                        safeInsetLeft +
                            contentWidth *
                                (
                                    position.x -
                                    halfWidth
                            ),
                        safeInsetTop +
                            contentHeight *
                                (
                                    position.y -
                                    halfHeight
                            ),
                        safeInsetLeft +
                            contentWidth *
                                (
                                    position.x +
                                    halfWidth
                            ),
                        safeInsetTop +
                            contentHeight *
                                (
                                    position.y +
                                    halfHeight
                            ),
                    ),
                hitPadding =
                    unit *
                        hitPadding,
            )
        }

        rect(ControlId.LT, 0.025f, 0.045f, 0.182f, 0.196f)
        rect(ControlId.LB, 0.205f, 0.051f, 0.366f, 0.197f)
        rect(ControlId.RB, 0.635f, 0.051f, 0.796f, 0.197f)
        rect(ControlId.RT, 0.819f, 0.045f, 0.975f, 0.196f)

        rect(ControlId.BACK, 0.326f, 0.243f, 0.397f, 0.321f)
        rect(ControlId.L3, 0.358f, 0.364f, 0.429f, 0.442f)
        rect(ControlId.R3, 0.571f, 0.364f, 0.642f, 0.442f)
        rect(ControlId.START, 0.603f, 0.243f, 0.674f, 0.321f)
        rect(ControlId.GUIDE, 0.467f, 0.472f, 0.534f, 0.554f)

        circle(ControlId.LEFT_STICK, 0.153f, 0.474f, 0.132f, 1.28f)

        circle(ControlId.DPAD_UP, 0.380f, 0.582f, 0.070f, 1.10f)
        circle(ControlId.DPAD_LEFT, 0.380f - unit * 0.140f / contentWidth, 0.722f, 0.070f, 1.10f)
        circle(ControlId.DPAD_RIGHT, 0.380f + unit * 0.140f / contentWidth, 0.722f, 0.070f, 1.10f)
        circle(ControlId.DPAD_DOWN, 0.380f, 0.862f, 0.070f, 1.10f)

        circle(ControlId.RIGHT_STICK, 0.620f, 0.710f, 0.105f, 1.60f)

        circle(ControlId.Y, 0.843f, 0.358f, 0.077f, 1.14f)
        circle(ControlId.X, 0.781f, 0.498f, 0.077f, 1.14f)
        circle(ControlId.B, 0.907f, 0.498f, 0.077f, 1.14f)
        circle(ControlId.A, 0.844f, 0.640f, 0.077f, 1.14f)

        skin.rebuild(width, height, safeInsetLeft, safeInsetTop, contentWidth, contentHeight,
            controls.map { control ->
                ControllerSkin.Node(control.id.name, control.centerX, control.centerY,
                    control.radius, control.rect, control.radius * 0.50f)
            })
    }

    private fun isDpadControl(
        id: ControlId,
    ): Boolean =
        id == ControlId.DPAD_UP ||
            id == ControlId.DPAD_DOWN ||
            id == ControlId.DPAD_LEFT ||
            id == ControlId.DPAD_RIGHT

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
        fun contains(x: Float, y: Float): Boolean =
            contains(
                x,
                y,
                releaseScale = 1f,
            )

        fun containsForActivePointer(
            x: Float,
            y: Float,
        ): Boolean =
            contains(
                x,
                y,
                releaseScale =
                    ACTIVE_POINTER_RELEASE_SCALE,
            )

        private fun contains(
            x: Float,
            y: Float,
            releaseScale: Float,
        ): Boolean {
            return when (shape) {
                Shape.CIRCLE -> {
                    val dx =
                        x -
                            centerX
                    val dy =
                        y -
                            centerY
                    val releaseRadius =
                        hitRadius *
                            releaseScale

                    dx * dx +
                        dy * dy <=
                        releaseRadius *
                        releaseRadius
                }

                Shape.RECT -> {
                    val visual =
                        rect
                            ?: return false
                    val releasePadding =
                        hitPadding *
                            releaseScale

                    x >=
                        visual.left -
                            releasePadding &&
                        x <=
                        visual.right +
                            releasePadding &&
                        y >=
                        visual.top -
                            releasePadding &&
                        y <=
                        visual.bottom +
                            releasePadding
                }
            }
        }

        companion object {
            private const val ACTIVE_POINTER_RELEASE_SCALE =
                1.45f

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
