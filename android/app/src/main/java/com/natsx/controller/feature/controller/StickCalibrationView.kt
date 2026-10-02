package com.natsx.controller.feature.controller

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.view.MotionEvent
import android.view.View
import com.natsx.controller.core.input.CalibrationPoint
import com.natsx.controller.core.input.ControllerStickCalibration
import com.natsx.controller.core.input.StickCalibration
import com.natsx.controller.core.input.StickCalibrationEstimator
import kotlin.math.min

class StickCalibrationView(
    context: Context,
    private val onCompleted:
        (ControllerStickCalibration) -> Unit,
) : View(context) {
    private val leftEstimator =
        StickCalibrationEstimator()
    private val rightEstimator =
        StickCalibrationEstimator()

    private var phase =
        Phase.LEFT_CENTER

    private var leftCalibration:
        StickCalibration? = null

    private var lastTouch:
        CalibrationPoint? = null

    private var completionDispatched =
        false

    private val circlePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            style = Paint.Style.STROKE
            strokeWidth = 4f
        }

    private val guidePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color =
                Color.rgb(
                    92,
                    92,
                    98,
                )
            style = Paint.Style.STROKE
            strokeWidth = 2f
        }

    private val touchPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            style = Paint.Style.FILL
        }

    private val titlePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            textAlign = Paint.Align.CENTER
            typeface =
                android.graphics.Typeface
                    .DEFAULT_BOLD
        }

    private val bodyPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color =
                Color.rgb(
                    205,
                    205,
                    210,
                )
            textAlign = Paint.Align.CENTER
        }

    init {
        isClickable = true
        isFocusable = true
        setBackgroundColor(
            Color.rgb(
                9,
                9,
                11,
            ),
        )
    }

    override fun onDraw(
        canvas: Canvas,
    ) {
        super.onDraw(canvas)

        if (width <= 0 ||
            height <= 0
        ) {
            return
        }

        val radius =
            targetRadius()
        val center =
            targetCenter()

        titlePaint.textSize =
            min(
                width,
                height,
            ) * 0.060f

        bodyPaint.textSize =
            min(
                width,
                height,
            ) * 0.036f

        canvas.drawText(
            phase.title,
            width / 2f,
            height * 0.14f,
            titlePaint,
        )

        canvas.drawText(
            phase.instruction,
            width / 2f,
            height * 0.22f,
            bodyPaint,
        )

        canvas.drawCircle(
            center.x,
            center.y,
            radius,
            circlePaint,
        )

        canvas.drawCircle(
            center.x,
            center.y,
            radius * 0.72f,
            guidePaint,
        )

        canvas.drawLine(
            center.x - radius * 0.18f,
            center.y,
            center.x + radius * 0.18f,
            center.y,
            guidePaint,
        )
        canvas.drawLine(
            center.x,
            center.y - radius * 0.18f,
            center.x,
            center.y + radius * 0.18f,
            guidePaint,
        )

        lastTouch?.let { point ->
            canvas.drawCircle(
                center.x +
                    point.x * radius,
                center.y -
                    point.y * radius,
                radius * 0.055f,
                touchPaint,
            )
        }

        canvas.drawText(
            progressLabel(),
            width / 2f,
            height * 0.91f,
            bodyPaint,
        )
    }

    override fun onTouchEvent(
        event: MotionEvent,
    ): Boolean {
        if (phase ==
            Phase.COMPLETE
        ) {
            return true
        }

        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                val point =
                    normalizedPoint(
                        event.x,
                        event.y,
                    )

                lastTouch = point

                if (phase.isCenterPhase) {
                    activeEstimator()
                        .addCenterTap(
                            point.x,
                            point.y,
                        )

                    if (
                        activeEstimator()
                            .isCenterReady
                    ) {
                        phase =
                            phase.next
                        lastTouch = null
                    }
                } else {
                    addRangePoint(point)
                }
            }

            MotionEvent.ACTION_MOVE -> {
                if (!phase.isCenterPhase) {
                    for (
                        index in
                        0 until event.historySize
                    ) {
                        addRangePoint(
                            normalizedPoint(
                                event.getHistoricalX(
                                    index,
                                ),
                                event.getHistoricalY(
                                    index,
                                ),
                            ),
                        )
                    }

                    val point =
                        normalizedPoint(
                            event.x,
                            event.y,
                        )

                    lastTouch = point
                    addRangePoint(point)
                }
            }

            MotionEvent.ACTION_UP -> {
                if (!phase.isCenterPhase) {
                    val point =
                        normalizedPoint(
                            event.x,
                            event.y,
                        )

                    lastTouch = point
                    addRangePoint(point)
                    tryFinishRange()
                }

                performClick()
            }

            MotionEvent.ACTION_CANCEL -> {
                lastTouch = null
            }
        }

        invalidate()
        return true
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    private fun addRangePoint(
        point: CalibrationPoint,
    ) {
        activeEstimator()
            .addRangeSample(
                point.x,
                point.y,
            )
    }

    private fun tryFinishRange() {
        val estimator =
            activeEstimator()

        if (!estimator.isRangeReady) {
            return
        }

        val result =
            estimator.estimate()

        when (phase) {
            Phase.LEFT_RANGE -> {
                leftCalibration = result
                phase =
                    Phase.RIGHT_CENTER
                lastTouch = null
            }

            Phase.RIGHT_RANGE -> {
                val left =
                    leftCalibration
                        ?: return

                phase =
                    Phase.COMPLETE
                lastTouch = null

                if (!completionDispatched) {
                    completionDispatched =
                        true

                    post {
                        onCompleted(
                            ControllerStickCalibration(
                                left = left,
                                right = result,
                            ),
                        )
                    }
                }
            }

            else -> Unit
        }
    }

    private fun activeEstimator():
        StickCalibrationEstimator =
        when (phase) {
            Phase.LEFT_CENTER,
            Phase.LEFT_RANGE,
            ->
                leftEstimator

            Phase.RIGHT_CENTER,
            Phase.RIGHT_RANGE,
            ->
                rightEstimator

            Phase.COMPLETE ->
                rightEstimator
        }

    private fun targetCenter():
        CalibrationPoint =
        CalibrationPoint(
            x =
                when (phase) {
                    Phase.LEFT_CENTER,
                    Phase.LEFT_RANGE,
                    ->
                        width * 0.28f

                    Phase.RIGHT_CENTER,
                    Phase.RIGHT_RANGE,
                    Phase.COMPLETE,
                    ->
                        width * 0.72f
                },
            y =
                height * 0.58f,
        )

    private fun targetRadius(): Float =
        min(
            width,
            height,
        ) * 0.24f

    private fun normalizedPoint(
        x: Float,
        y: Float,
    ): CalibrationPoint {
        val center =
            targetCenter()
        val radius =
            targetRadius()
                .coerceAtLeast(1f)

        return CalibrationPoint(
            x =
                (x - center.x) /
                    radius,
            y =
                -(y - center.y) /
                    radius,
        )
    }

    private fun progressLabel(): String {
        val estimator =
            activeEstimator()

        return when (phase) {
            Phase.LEFT_CENTER,
            Phase.RIGHT_CENTER,
            ->
                "Center taps: " +
                    "${estimator.centerTapCount}/" +
                    StickCalibrationEstimator
                        .DEFAULT_CENTER_TAPS

            Phase.LEFT_RANGE,
            Phase.RIGHT_RANGE,
            ->
                "Range samples: " +
                    "${estimator.rangeSampleCount} • " +
                    "directions: " +
                    "${estimator.coveredDirectionSectors}/" +
                    StickCalibrationEstimator
                        .DEFAULT_REQUIRED_DIRECTION_SECTORS

            Phase.COMPLETE ->
                "Calibration complete"
        }
    }

    private enum class Phase(
        val title: String,
        val instruction: String,
        val isCenterPhase: Boolean,
    ) {
        LEFT_CENTER(
            title = "Left stick • center",
            instruction =
                "Tap your natural thumb center 5 times",
            isCenterPhase = true,
        ),
        LEFT_RANGE(
            title = "Left stick • range",
            instruction =
                "Drag one full circle around your comfortable range",
            isCenterPhase = false,
        ),
        RIGHT_CENTER(
            title = "Right stick • center",
            instruction =
                "Tap your natural thumb center 5 times",
            isCenterPhase = true,
        ),
        RIGHT_RANGE(
            title = "Right stick • range",
            instruction =
                "Drag one full circle around your comfortable range",
            isCenterPhase = false,
        ),
        COMPLETE(
            title = "Calibration complete",
            instruction =
                "Your thumb alignment and travel range are saved",
            isCenterPhase = false,
        );

        val next: Phase
            get() =
                when (this) {
                    LEFT_CENTER ->
                        LEFT_RANGE
                    LEFT_RANGE ->
                        RIGHT_CENTER
                    RIGHT_CENTER ->
                        RIGHT_RANGE
                    RIGHT_RANGE,
                    COMPLETE,
                    ->
                        COMPLETE
                }
    }
}
