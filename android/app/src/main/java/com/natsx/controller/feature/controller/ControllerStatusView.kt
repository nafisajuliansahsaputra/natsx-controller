package com.natsx.controller.feature.controller

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.view.View
import kotlin.math.max
import kotlin.math.min

class ControllerStatusView(
    context: Context,
) : View(context) {
    private val density =
        resources.displayMetrics.density

    private val inactivePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = COLOR_INACTIVE
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
        }

    private val activePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = COLOR_ACTIVE
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
        }

    private val batteryFillPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.FILL
        }

    private val messagePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = COLOR_TEXT
            textAlign = Paint.Align.CENTER
        }

    private val lampPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.FILL
        }

    private val lampOutlinePaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = COLOR_SURFACE_OUTLINE
            style = Paint.Style.STROKE
            strokeWidth =
                (1f * density)
                    .coerceAtLeast(
                        1f,
                    )
        }

    private val tempRect = RectF()
    private val iconPath = Path()

    private var state =
        ControllerHudState()

    private var safeInsetLeft = 0
    private var safeInsetTop = 0
    private var safeInsetRight = 0

    init {
        isClickable = false
        isFocusable = false
        importantForAccessibility =
            IMPORTANT_FOR_ACCESSIBILITY_NO
        setBackgroundColor(Color.TRANSPARENT)
    }

    fun updateState(
        next: ControllerHudState,
    ) {
        if (state == next) {
            return
        }

        state = next
        invalidate()
    }

    fun applySafeInsets(
        left: Int,
        top: Int,
        right: Int,
    ) {
        val horizontalInset =
            max(
                left.coerceAtLeast(0),
                right.coerceAtLeast(0),
            )
        val nextLeft =
            horizontalInset
        val nextTop =
            top.coerceAtLeast(0)
        val nextRight =
            horizontalInset

        if (
            safeInsetLeft == nextLeft &&
            safeInsetTop == nextTop &&
            safeInsetRight == nextRight
        ) {
            return
        }

        safeInsetLeft = nextLeft
        safeInsetTop = nextTop
        safeInsetRight = nextRight
        invalidate()
    }

    override fun onDraw(
        canvas: Canvas,
    ) {
        super.onDraw(canvas)

        if (width <= 0 || height <= 0) {
            return
        }

        val contentWidth = (width - safeInsetLeft - safeInsetRight).coerceAtLeast(1).toFloat()
        val contentHeight = (height - safeInsetTop).coerceAtLeast(1).toFloat()
        val contentCenterX = width / 2f
        val iconSize = contentHeight * 0.028f
        val gap = contentWidth * 0.018f
        val groupWidth = iconSize * 4f + gap * 3f
        val firstCenterX = contentCenterX - groupWidth / 2f + iconSize / 2f
        val iconCenterY = safeInsetTop + contentHeight * 0.063f
        val stroke = (contentHeight * 0.0028f).coerceAtLeast(1.5f)

        inactivePaint.strokeWidth = stroke
        activePaint.strokeWidth = stroke

        drawWifi(
            canvas = canvas,
            centerX = firstCenterX,
            centerY = iconCenterY,
            size = iconSize,
            paint =
                transportPaint(
                    ControllerTransportIndicator.WIFI,
                ),
        )

        drawBattery(
            canvas = canvas,
            centerX =
                firstCenterX +
                    iconSize +
                    gap,
            centerY = iconCenterY,
            size = iconSize,
        )

        drawBluetooth(
            canvas = canvas,
            centerX =
                firstCenterX +
                    (iconSize + gap) * 2f,
            centerY = iconCenterY,
            size = iconSize,
            paint =
                transportPaint(
                    ControllerTransportIndicator.BLUETOOTH,
                ),
        )

        drawUsb(
            canvas = canvas,
            centerX =
                firstCenterX +
                    (iconSize + gap) * 3f,
            centerY = iconCenterY,
            size = iconSize,
            paint =
                transportPaint(
                    ControllerTransportIndicator.USB,
                ),
        )

        val lampWidth = contentWidth * 0.064f
        val lampHeight = contentHeight * 0.014f
        val lampCenterY = safeInsetTop + contentHeight * 0.334f

        lampPaint.color =
            when (state.activeTransport) {
                ControllerTransportIndicator.USB ->
                    COLOR_USB_LAMP

                ControllerTransportIndicator.WIFI ->
                    COLOR_WIFI_LAMP

                ControllerTransportIndicator.BLUETOOTH ->
                    COLOR_BLUETOOTH_LAMP

                null ->
                    COLOR_OFFLINE_LAMP
            }

        tempRect.set(
            contentCenterX - lampWidth / 2f,
            lampCenterY - lampHeight / 2f,
            contentCenterX + lampWidth / 2f,
            lampCenterY + lampHeight / 2f,
        )

        canvas.drawRoundRect(
            tempRect,
            lampHeight / 2f,
            lampHeight / 2f,
            lampPaint,
        )
        canvas.drawRoundRect(
            tempRect,
            lampHeight / 2f,
            lampHeight / 2f,
            lampOutlinePaint,
        )

    }

    private fun transportPaint(
        transport:
            ControllerTransportIndicator,
    ): Paint =
        if (state.activeTransport == transport) {
            activePaint
        } else {
            inactivePaint
        }

    private fun drawWifi(
        canvas: Canvas,
        centerX: Float,
        centerY: Float,
        size: Float,
        paint: Paint,
    ) {
        val half =
            size * 0.48f

        for (index in 0..2) {
            val radius =
                half *
                    (
                        1f -
                            index *
                            0.27f
                    )

            tempRect.set(
                centerX - radius,
                centerY - radius * 0.70f,
                centerX + radius,
                centerY + radius * 1.30f,
            )

            canvas.drawArc(
                tempRect,
                220f,
                100f,
                false,
                paint,
            )
        }

        val previousStyle =
            paint.style
        paint.style = Paint.Style.FILL
        canvas.drawCircle(
            centerX,
            centerY + size * 0.28f,
            size * 0.07f,
            paint,
        )
        paint.style = previousStyle
    }

    private fun drawBattery(
        canvas: Canvas,
        centerX: Float,
        centerY: Float,
        size: Float,
    ) {
        val width =
            size * 1.08f
        val height =
            size * 0.58f

        inactivePaint.strokeWidth =
            (2f * density)
                .coerceAtLeast(
                    1.5f,
                )

        tempRect.set(
            centerX - width / 2f,
            centerY - height / 2f,
            centerX + width / 2f,
            centerY + height / 2f,
        )

        canvas.drawRoundRect(
            tempRect,
            height * 0.15f,
            height * 0.15f,
            inactivePaint,
        )

        val terminalWidth =
            size * 0.10f

        tempRect.set(
            centerX + width / 2f +
                inactivePaint.strokeWidth,
            centerY - height * 0.17f,
            centerX + width / 2f +
                terminalWidth,
            centerY + height * 0.17f,
        )

        val previousStyle =
            inactivePaint.style
        inactivePaint.style = Paint.Style.FILL
        canvas.drawRoundRect(
            tempRect,
            terminalWidth * 0.25f,
            terminalWidth * 0.25f,
            inactivePaint,
        )
        inactivePaint.style = previousStyle

        val percent =
            state.batteryPercent
                ?: 0

        batteryFillPaint.color =
            when {
                state.batteryPercent == null ->
                    COLOR_INACTIVE

                percent < 15 ->
                    COLOR_CRITICAL

                percent < 30 ->
                    COLOR_WARNING

                else ->
                    COLOR_ACTIVE
            }

        val innerPadding =
            size * 0.10f
        val availableWidth =
            width -
                innerPadding * 2f
        val fillFraction =
            if (state.batteryPercent == null) {
                0.28f
            } else {
                percent / 100f
            }

        tempRect.set(
            centerX - width / 2f +
                innerPadding,
            centerY - height / 2f +
                innerPadding,
            centerX - width / 2f +
                innerPadding +
                availableWidth *
                    fillFraction.coerceIn(
                        0.08f,
                        1f,
                    ),
            centerY + height / 2f -
                innerPadding,
        )

        canvas.drawRoundRect(
            tempRect,
            height * 0.08f,
            height * 0.08f,
            batteryFillPaint,
        )

        if (state.charging) {
            val boltPaint =
                if (percent >= 30) {
                    activePaint
                } else {
                    inactivePaint
                }

            iconPath.reset()
            iconPath.moveTo(
                centerX + size * 0.02f,
                centerY - size * 0.24f,
            )
            iconPath.lineTo(
                centerX - size * 0.10f,
                centerY + size * 0.02f,
            )
            iconPath.lineTo(
                centerX + size * 0.01f,
                centerY + size * 0.02f,
            )
            iconPath.lineTo(
                centerX - size * 0.04f,
                centerY + size * 0.25f,
            )
            iconPath.lineTo(
                centerX + size * 0.13f,
                centerY - size * 0.04f,
            )
            iconPath.lineTo(
                centerX + size * 0.02f,
                centerY - size * 0.04f,
            )

            canvas.drawPath(
                iconPath,
                boltPaint,
            )
        }
    }

    private fun drawBluetooth(
        canvas: Canvas,
        centerX: Float,
        centerY: Float,
        size: Float,
        paint: Paint,
    ) {
        val halfHeight =
            size * 0.48f
        val halfWidth =
            size * 0.26f

        iconPath.reset()
        iconPath.moveTo(
            centerX,
            centerY - halfHeight,
        )
        iconPath.lineTo(
            centerX + halfWidth,
            centerY - size * 0.22f,
        )
        iconPath.lineTo(
            centerX - halfWidth,
            centerY + size * 0.22f,
        )
        iconPath.lineTo(
            centerX,
            centerY + halfHeight,
        )
        iconPath.lineTo(
            centerX,
            centerY - halfHeight,
        )
        iconPath.lineTo(
            centerX - halfWidth,
            centerY - size * 0.22f,
        )
        iconPath.lineTo(
            centerX + halfWidth,
            centerY + size * 0.22f,
        )

        canvas.drawPath(
            iconPath,
            paint,
        )
    }

    private fun drawUsb(
        canvas: Canvas,
        centerX: Float,
        centerY: Float,
        size: Float,
        paint: Paint,
    ) {
        val top =
            centerY - size * 0.48f
        val bottom =
            centerY + size * 0.46f

        canvas.drawLine(
            centerX,
            bottom,
            centerX,
            top,
            paint,
        )

        canvas.drawLine(
            centerX,
            centerY - size * 0.02f,
            centerX - size * 0.30f,
            centerY - size * 0.24f,
            paint,
        )

        canvas.drawLine(
            centerX,
            centerY + size * 0.12f,
            centerX + size * 0.30f,
            centerY - size * 0.12f,
            paint,
        )

        val previousStyle =
            paint.style
        paint.style = Paint.Style.FILL

        iconPath.reset()
        iconPath.moveTo(
            centerX,
            top - size * 0.10f,
        )
        iconPath.lineTo(
            centerX - size * 0.10f,
            top + size * 0.08f,
        )
        iconPath.lineTo(
            centerX + size * 0.10f,
            top + size * 0.08f,
        )
        iconPath.close()
        canvas.drawPath(
            iconPath,
            paint,
        )

        canvas.drawCircle(
            centerX - size * 0.30f,
            centerY - size * 0.24f,
            size * 0.07f,
            paint,
        )

        tempRect.set(
            centerX + size * 0.23f,
            centerY - size * 0.19f,
            centerX + size * 0.37f,
            centerY - size * 0.05f,
        )
        canvas.drawRect(
            tempRect,
            paint,
        )

        paint.style = previousStyle
    }

    private fun drawCenteredText(
        canvas: Canvas,
        text: String,
        x: Float,
        y: Float,
        paint: Paint,
    ) {
        val metrics =
            paint.fontMetrics
        val baseline =
            y -
                (
                    metrics.ascent +
                        metrics.descent
                ) /
                2f

        canvas.drawText(
            text,
            x,
            baseline,
            paint,
        )
    }

    private companion object {
        const val COLOR_ACTIVE =
            0xFF69D67A.toInt()
        const val COLOR_INACTIVE =
            0xFFB8BBC2.toInt()
        const val COLOR_WARNING =
            0xFFE9B35E.toInt()
        const val COLOR_CRITICAL =
            0xFFD96C6C.toInt()
        const val COLOR_ERROR =
            0xFFD96C6C.toInt()
        const val COLOR_TEXT =
            0xFF55525B.toInt()
        const val COLOR_LAVENDER =
            0xFFA88BDF.toInt()
        const val COLOR_USB_LAMP =
            0xFFAA8BE8.toInt()
        const val COLOR_WIFI_LAMP =
            0xFF69D67A.toInt()
        const val COLOR_BLUETOOTH_LAMP =
            0xFFF6F6FA.toInt()
        const val COLOR_OFFLINE_LAMP =
            0xFFB9BBC2.toInt()
        const val COLOR_SURFACE_OUTLINE =
            0xFFDEDBE5.toInt()
    }
}
