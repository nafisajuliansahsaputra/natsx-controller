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
    private val iconBitmaps = listOf("imgGroup", "imgBiUsbSymbol", "imgGroup1", "imgAkarIconsBluetooth").associateWith { name ->
        context.assets.open("controller/$name.png").use { android.graphics.BitmapFactory.decodeStream(it)!! }
    }
    private val iconPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val activeFilter = android.graphics.PorterDuffColorFilter(COLOR_ACTIVE, android.graphics.PorterDuff.Mode.SRC_IN)
    private val inactiveFilter = android.graphics.PorterDuffColorFilter(COLOR_INACTIVE, android.graphics.PorterDuff.Mode.SRC_IN)

    private fun drawTransportAsset(canvas: Canvas, name: String, x: Float, y: Float, w: Float, h: Float,
        transport: ControllerTransportIndicator) {
        iconPaint.colorFilter = if (state.activeTransport == transport) activeFilter else inactiveFilter
        tempRect.set(x, y, x + w, y + h)
        canvas.drawBitmap(iconBitmaps.getValue(name), null, tempRect, iconPaint)
    }

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
    private var safeInsetBottom = 0

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
        bottom: Int = 0,
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
            safeInsetRight == nextRight && safeInsetBottom == bottom.coerceAtLeast(0)
        ) {
            return
        }

        safeInsetLeft = nextLeft
        safeInsetTop = nextTop
        safeInsetRight = nextRight
        safeInsetBottom = bottom.coerceAtLeast(0)
        invalidate()
    }

    override fun onDraw(
        canvas: Canvas,
    ) {
        super.onDraw(canvas)

        if (width <= 0 || height <= 0) {
            return
        }

        val viewport = ControllerDesignViewport.fit(width.toFloat(), height.toFloat(),
            safeInsetLeft.toFloat(), safeInsetTop.toFloat(), safeInsetRight.toFloat(), safeInsetBottom.toFloat())
        val contentCenterX = viewport.x(1198.201f)
        val iconSize = 30f * viewport.scale
        val iconCenterY = viewport.y(260f)
        val stroke = (2f * viewport.scale).coerceAtLeast(1f)
        inactivePaint.strokeWidth = stroke
        activePaint.strokeWidth = stroke
        drawBattery(canvas, viewport.x(1078.201f), iconCenterY, iconSize)
        drawTransportAsset(canvas, "imgBiUsbSymbol", viewport.x(1143.201f), viewport.y(245f),
            30f * viewport.scale, 30f * viewport.scale, ControllerTransportIndicator.USB)
        drawTransportAsset(canvas, "imgGroup1", viewport.x(1224.701f), viewport.y(250.25f),
            27f * viewport.scale, 20f * viewport.scale, ControllerTransportIndicator.WIFI)
        drawTransportAsset(canvas, "imgAkarIconsBluetooth", viewport.x(1303.201f), viewport.y(245f),
            30f * viewport.scale, 30f * viewport.scale, ControllerTransportIndicator.BLUETOOTH)

        val lampWidth = 500.402f * viewport.scale
        val lampHeight = 5f * viewport.scale
        val lampCenterY = viewport.y(52.5f)

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

    private fun drawBattery(canvas: Canvas, centerX: Float, centerY: Float, size: Float) {
        val scale = size / 30f
        val left = centerX - size / 2f
        val top = centerY - 7.5f * scale
        val percent = state.batteryPercent
        batteryFillPaint.color = when {
            percent == null -> COLOR_INACTIVE
            percent < 15 -> COLOR_CRITICAL
            percent < 30 -> COLOR_WARNING
            else -> COLOR_ACTIVE
        }
        iconPaint.colorFilter = if (percent == null) inactiveFilter else activeFilter
        tempRect.set(left, top, left + size, top + 15f * scale)
        canvas.drawBitmap(iconBitmaps.getValue("imgGroup"), null, tempRect, iconPaint)
        // Keep the original outline/terminal and update only the live charge bar.
        tempRect.set(left + 3.75f * scale, top + 3.75f * scale, left + 22.5f * scale, top + 11.25f * scale)
        lampOutlinePaint.style = Paint.Style.FILL
        lampOutlinePaint.color = Color.rgb(245, 245, 245)
        canvas.drawRect(tempRect, lampOutlinePaint)
        lampOutlinePaint.style = Paint.Style.STROKE
        lampOutlinePaint.color = COLOR_SURFACE_OUTLINE
        val fraction = if (percent == null) 0.28f else percent / 100f
        tempRect.right = tempRect.left + 18.75f * scale * fraction.coerceIn(0f, 1f)
        canvas.drawRect(tempRect, batteryFillPaint)
        if (state.charging) {
            iconPath.reset()
            iconPath.moveTo(centerX + 1f * scale, top + 2f * scale)
            iconPath.lineTo(centerX - 3f * scale, centerY + 1f * scale)
            iconPath.lineTo(centerX, centerY + 1f * scale)
            iconPath.lineTo(centerX - 1f * scale, top + 13f * scale)
            iconPath.lineTo(centerX + 3f * scale, centerY - 1f * scale)
            iconPath.lineTo(centerX, centerY - 1f * scale)
            iconPath.close()
            canvas.drawPath(iconPath, batteryFillPaint)
        }
    }

    private companion object {
        const val COLOR_ACTIVE =
            0xFFB2EBB2.toInt()
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
            0xFFB2EBB2.toInt()
        const val COLOR_WIFI_LAMP =
            0xFFB2EBB2.toInt()
        const val COLOR_BLUETOOTH_LAMP =
            0xFFB2EBB2.toInt()
        const val COLOR_OFFLINE_LAMP =
            0xFFB9BBC2.toInt()
        const val COLOR_SURFACE_OUTLINE =
            0xFFDEDBE5.toInt()
    }
}
