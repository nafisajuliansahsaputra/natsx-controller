package com.natsx.controller.feature.controller

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import com.natsx.controller.core.gamepad.DpadState
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

/** Decorative raster work happens only on layout changes, never on touch/packet delivery.
 * Sprites are capped at 1080px viewport height; no runtime blur, shader or animation loop.
 */
internal class ControllerSkin {
    data class Node(
        val id: String,
        val x: Float,
        val y: Float,
        val radius: Float,
        val rect: RectF?,
        val travel: Float,
    )

    private class Sprite(val bitmap: Bitmap, val bounds: RectF)
    private class Button(val idle: Sprite, val pressed: Sprite)
    private val bitmapPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val feedbackPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0x554D3A7C }
    private val editorPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 2f
    }
    private var background: Sprite? = null
    private val buttons = mutableMapOf<String, Button>()
    private val nodes = mutableMapOf<String, Node>()
    private val dpadArms = mutableListOf<Pair<Int, Path>>()
    private var cap: Button? = null
    private var unit = 1f
    private var rasterScale = 1f

    fun rebuild(w: Float, h: Float, insetX: Float, insetY: Float, usableW: Float, usableH: Float, geometry: List<Node>) {
        // Drop references rather than recycle: RenderThread may still be using an old bitmap.
        buttons.clear()
        nodes.clear()
        dpadArms.clear()
        nodes.putAll(geometry.associateBy { it.id })
        unit = min(usableW, usableH)
        rasterScale = min(1f, min(1080f / h, 2400f / w))
        background = raster(RectF(0f, 0f, w, h)) { c ->
            val p = Paint(Paint.ANTI_ALIAS_FLAG)
            p.shader = gradient(0f, h, "#F0F0F0", "#F0F0F0")
            c.drawRect(0f, 0f, w, h, p)
            val panel = Path().apply {
                fun dx(value: Float) = insetX + value * usableW / 2400f
                fun dy(value: Float) = insetY + value * usableH / 1080f
                moveTo(dx(797f), dy(0f))
                lineTo(dx(1603f), dy(0f))
                lineTo(dx(1345f), dy(431f))
                cubicTo(dx(1330f), dy(455f), dx(1310f), dy(474f), dx(1280f), dy(474f))
                lineTo(dx(1120f), dy(474f))
                cubicTo(dx(1090f), dy(474f), dx(1070f), dy(455f), dx(1055f), dy(431f))
                lineTo(dx(797f), dy(0f))
                close()
            }
            p.shader = gradient(insetY, insetY + usableH * (474f / 1080f), "#F4F4F5", "#F0F0F2")
            p.setShadowLayer(unit * 0.014f, 0f, 0f, Color.parseColor("#55FFFFFF"))
            c.drawPath(panel, p)
            p.clearShadowLayer()
            p.shader = null
            for (node in geometry) {
                if (node.id.endsWith("_STICK")) drawPlate(c, node.x, node.y, unit * (232f / 1080f))
            }
            drawDpadBase(c)
        }
        for (node in geometry) {
            if (node.id.endsWith("_STICK") || node.id.startsWith("DPAD_")) continue
            val r = node.rect ?: RectF(node.x - node.radius, node.y - node.radius, node.x + node.radius, node.y + node.radius)
            val bounds = RectF(r).apply { inset(-unit * 0.028f, -unit * 0.028f) }
            buttons[node.id] = Button(
                raster(bounds) { c -> drawKey(c, node.id, r, false) },
                raster(bounds) { c -> drawKey(c, node.id, r, true) },
            )
        }
        val capRadius = unit * (117f / 1080f)
        val capBounds = RectF(-capRadius - unit * 0.025f, -capRadius - unit * 0.025f,
            capRadius + unit * 0.025f, capRadius + unit * 0.025f)
        cap = Button(
            raster(capBounds) { c -> drawCap(c, capRadius, false) },
            raster(capBounds) { c -> drawCap(c, capRadius, true) },
        )
    }

    private fun raster(bounds: RectF, draw: (Canvas) -> Unit): Sprite {
        val bitmap = Bitmap.createBitmap(ceil(bounds.width() * rasterScale).toInt().coerceAtLeast(1),
            ceil(bounds.height() * rasterScale).toInt().coerceAtLeast(1), Bitmap.Config.ARGB_8888)
        val c = Canvas(bitmap)
        c.scale(rasterScale, rasterScale)
        c.translate(-bounds.left, -bounds.top)
        draw(c)
        return Sprite(bitmap, bounds)
    }

    private fun drawSprite(c: Canvas, sprite: Sprite) {
        c.drawBitmap(sprite.bitmap, null, sprite.bounds, bitmapPaint)
    }

    fun drawBackground(c: Canvas) { background?.let { drawSprite(c, it) } }

    fun drawButton(c: Canvas, id: String, active: Boolean) {
        val button = buttons[id] ?: return
        drawSprite(c, if (active) button.pressed else button.idle)
    }

    fun drawStick(c: Canvas, id: String, x: Int, y: Int, active: Boolean) {
        val node = nodes[id] ?: return
        val sprite = cap ?: return
        val nx = (x / if (x < 0) 32768f else 32767f).coerceIn(-1f, 1f)
        val ny = (y / if (y < 0) 32768f else 32767f).coerceIn(-1f, 1f)
        c.save()
        c.translate(node.x + nx * node.travel, node.y - ny * node.travel)
        drawSprite(c, if (active) sprite.pressed else sprite.idle)
        c.restore()
    }

    fun drawDpad(c: Canvas, dpad: Int) {
        for ((bit, path) in dpadArms) {
            if (dpad and bit != 0) c.drawPath(path, feedbackPaint)
        }
    }

    fun drawEditorTarget(c: Canvas, x: Float, y: Float, selected: Boolean) {
        editorPaint.color = if (selected) Color.parseColor("#574379") else Color.parseColor("#B7A5D8")
        c.drawCircle(x, y, unit * 0.018f, editorPaint)
    }

    private fun gradient(top: Float, bottom: Float, vararg colors: String): Shader =
        LinearGradient(0f, top, 0f, bottom, colors.map { Color.parseColor(it) }.toIntArray(), null, Shader.TileMode.CLAMP)

    private fun drawPlate(c: Canvas, x: Float, y: Float, r: Float) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.color = Color.parseColor("#F0F0F1")
        p.setShadowLayer(r * 0.05f, 0f, r * 0.055f, Color.parseColor("#45000000"))
        c.drawCircle(x, y, r, p)
        p.clearShadowLayer()
        p.shader = gradient(y - r, y + r, "#F7F7F7", "#F1F1F1", "#F0F0F0")
        c.drawCircle(x, y, r, p)
        p.shader = null
    }

    private fun drawCap(c: Canvas, r: Float, active: Boolean) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.color = Color.parseColor("#304E39")
        p.setShadowLayer(r * 0.075f, 0f, r * 0.06f, Color.parseColor("#55000000"))
        c.drawCircle(0f, r * 0.045f, r, p)
        p.clearShadowLayer()
        p.shader = gradient(-r, r, "#C1F1C6", "#6DAB79", "#3D6648")
        c.drawCircle(0f, 0f, r * 0.97f, p)
        // Restrained stippling is baked into the shared cap sprite, never repeated on move.
        p.shader = null
        p.color = Color.parseColor("#88DDF2DC")
        for (ring in 0..3) {
            val rr = r * (0.84f + ring * 0.028f)
            for (i in 0 until 112) {
                val a = Math.PI * 2 * (i + (ring % 2) * 0.5) / 112
                c.drawCircle(cos(a).toFloat() * rr, sin(a).toFloat() * rr, r * 0.0045f, p)
            }
        }
        p.alpha = 255 // Stippling has translucent paint; the recessed face must be opaque.
        p.shader = gradient(-r * 0.75f, r * 0.75f,
            if (active) "#3F714C" else "#4F8159", if (active) "#8CCD98" else "#A8E9B4")
        c.drawCircle(0f, 0f, r * 0.78f, p)
        p.shader = null
        p.style = Paint.Style.STROKE
        p.strokeWidth = r * 0.026f
        p.color = Color.parseColor("#9BDCA6")
        c.drawCircle(0f, -r * 0.007f, r * 0.78f, p)
    }

    private fun keyPath(id: String, r: RectF): Path {
        val path = Path()
        val w = r.width()
        val h = r.height()
        val x = r.left
        val y = r.top
        when (id) {
            "A", "B", "X", "Y" -> path.addOval(r, Path.Direction.CW)
            "LT", "RT" -> path.addRoundRect(r, h * 0.25f, h * 0.25f, Path.Direction.CW)
            "LB", "RB" -> {
                // Rounded trapezoid with one straight wall and one long inward slope.
                path.moveTo(x + h * 0.24f, y)
                path.lineTo(x + w * 0.69f, y)
                path.cubicTo(x + w * 0.77f, y, x + w * 0.81f, y + h * 0.13f,
                    x + w * 0.84f, y + h * 0.28f)
                path.lineTo(r.right - h * 0.04f, r.bottom - h * 0.19f)
                path.quadTo(r.right + h * 0.05f, r.bottom, r.right - h * 0.11f, r.bottom)
                path.lineTo(x + h * 0.24f, r.bottom)
                path.quadTo(x, r.bottom, x, r.bottom - h * 0.24f)
                path.lineTo(x, y + h * 0.24f)
                path.quadTo(x, y, x + h * 0.24f, y)
                path.close()
                if (id == "RB") path.transform(Matrix().apply { setScale(-1f, 1f, r.centerX(), r.centerY()) })
            }
            "GUIDE" -> {
                path.moveTo(x + h * 0.13f, y)
                path.lineTo(r.right - h * 0.13f, y)
                path.quadTo(r.right + h * 0.05f, y, r.right - h * 0.03f, y + h * 0.22f)
                path.lineTo(r.right - w * 0.19f, r.bottom - h * 0.20f)
                path.quadTo(r.right - w * 0.24f, r.bottom, r.right - w * 0.33f, r.bottom)
                path.lineTo(x + w * 0.33f, r.bottom)
                path.quadTo(x + w * 0.24f, r.bottom, x + w * 0.19f, r.bottom - h * 0.20f)
                path.lineTo(x + h * 0.03f, y + h * 0.22f)
                path.quadTo(x - h * 0.05f, y, x + h * 0.13f, y)
                path.close()
            }

            else -> {
                // Vertical outer side; only the inner side leans toward the panel.
                path.moveTo(x + h * 0.24f, y)
                path.lineTo(x + w * 0.63f, y)
                path.cubicTo(x + w * 0.75f, y, x + w * 0.80f, y + h * 0.16f,
                    x + w * 0.84f, y + h * 0.30f)
                path.lineTo(r.right - h * 0.03f, r.bottom - h * 0.19f)
                path.quadTo(r.right + h * 0.04f, r.bottom, r.right - h * 0.10f, r.bottom)
                path.lineTo(x + h * 0.24f, r.bottom)
                path.quadTo(x, r.bottom, x, r.bottom - h * 0.24f)
                path.lineTo(x, y + h * 0.24f)
                path.quadTo(x, y, x + h * 0.24f, y)
                path.close()
                if (id == "START" || id == "R3") path.transform(Matrix().apply { setScale(-1f, 1f, r.centerX(), r.centerY()) })
            }
        }
        return path
    }

    private fun drawKey(c: Canvas, id: String, bounds: RectF, active: Boolean) {
        val mint = id in listOf("BACK", "START", "L3", "R3", "GUIDE")
        val circle = id in listOf("A", "B", "X", "Y")
        val px = unit / 1080f
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        val r = RectF(bounds)
        val key = keyPath(id, r)
        p.color = Color.parseColor(if (mint) "#55725A" else "#726481")
        p.setShadowLayer(11f * px, 0f, 4f * px, Color.parseColor("#65000000"))
        c.drawPath(key, p)
        p.clearShadowLayer()
        c.save(); if (active) c.translate(0f, 4f * px)
        val body = if (mint) (if (active) "#92CB98" else "#B0E8B3")
            else (if (active) "#B7A3D2" else "#C5B3E2")
        p.shader = LinearGradient(0f, r.top, 0f, r.bottom,
            arrayOf(if (mint) "#DBFBDD" else "#E1D3F2", body, body,
                if (mint) "#55755C" else "#706080").map { Color.parseColor(it) }.toIntArray(),
            floatArrayOf(0f, 0.18f, 0.80f, 1f), Shader.TileMode.CLAMP)
        c.drawPath(key, p)
        p.shader = null
        // A single continuous silhouette: soft top reflection and bottom bevel,
        // avoiding the nested outlines that changed the apparent button shape.
        c.save(); c.clipPath(key)
        p.shader = gradient(r.top, r.top + r.height() * 0.18f, "#90FFFFFF", "#00FFFFFF")
        c.drawRect(r, p)
        p.shader = gradient(r.bottom - r.height() * 0.18f, r.bottom,
            "#00000000", if (mint) "#40334D37" else "#40544768")
        c.drawRect(r, p)
        p.shader = null
        c.restore()
        p.color = Color.parseColor("#FAF8FF")
        p.textAlign = Paint.Align.CENTER
        p.typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
        p.textSize = bounds.height() * if (circle) 0.40f else if (mint) 0.40f else 0.32f
        val label = when (id) { "L3" -> "LS"; "R3" -> "RS"; "BACK", "START", "GUIDE" -> ""; else -> id }
        if (label.isNotEmpty()) {
            c.drawText(label, bounds.centerX(), bounds.centerY() - (p.ascent() + p.descent()) / 2f, p)
        } else drawUtilityIcon(c, id, bounds, p)
        c.restore()
    }

    private fun drawUtilityIcon(c: Canvas, id: String, r: RectF, p: Paint) {
        val x = r.centerX(); val y = r.centerY(); val s = r.height() * if (id == "GUIDE") 0.24f else 0.21f
        p.style = Paint.Style.STROKE; p.strokeWidth = r.height() * 0.043f
        p.strokeCap = Paint.Cap.SQUARE
        when (id) {
            "BACK" -> {
                c.drawRect(x - s, y - s * 0.8f, x + s * 0.15f, y + s * 0.35f, p)
                c.drawRect(x - s * 0.15f, y - s * 0.35f, x + s, y + s * 0.8f, p)
            }
            "START" -> for (i in -1..1) c.drawLine(x - s, y + i * s * 0.6f, x + s, y + i * s * 0.6f, p)
            "GUIDE" -> {
                val path = Path().apply {
                    moveTo(x - s * 0.75f, y - s * 0.1f); lineTo(x - s * 0.75f, y + s * 0.7f)
                    lineTo(x + s * 0.75f, y + s * 0.7f); lineTo(x + s * 0.75f, y - s * 0.1f)
                    moveTo(x, y + s * 0.3f); lineTo(x, y - s)
                    moveTo(x - s * 0.42f, y - s * 0.52f); lineTo(x, y - s); lineTo(x + s * 0.42f, y - s * 0.52f)
                }
                c.drawPath(path, p)
            }
        }
    }

    private fun drawDpadBase(c: Canvas) {
        val up = nodes["DPAD_UP"] ?: return
        val down = nodes["DPAD_DOWN"] ?: return
        val left = nodes["DPAD_LEFT"] ?: return
        val right = nodes["DPAD_RIGHT"] ?: return
        val x = (left.x + right.x) * 0.5f
        val y = (up.y + down.y) * 0.5f
        val radius = unit * (232f / 1080f)
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        val disk = Path().apply { addCircle(x, y, radius, Path.Direction.CW) }
        p.color = Color.parseColor("#C5B3E2")
        p.setShadowLayer(unit * (12f / 1080f), 0f, unit * (4f / 1080f), Color.parseColor("#65000000"))
        c.drawPath(disk, p)
        p.clearShadowLayer()
        p.shader = gradient(y - radius, y + radius, "#C5B3E2", "#C5B3E2", "#B5A2D2")
        c.drawPath(disk, p)
        val t = unit * (82f / 1080f)
        val l = x - radius; val r = x + radius
        val top = y - radius; val bottom = y + radius
        c.save(); c.clipPath(disk)
        // The cross reaches the circular rim. Its square inner corners follow the guide.
        p.shader = gradient(y - t - unit * 0.025f, y - t, "#00FFFFFF", "#66FFFFFF")
        c.drawRect(l, y - t - unit * 0.025f, r, y - t, p)
        p.shader = gradient(y + t, y + t + unit * 0.028f, "#405A4874", "#005A4874")
        c.drawRect(l, y + t, r, y + t + unit * 0.028f, p)
        p.shader = gradient(top, bottom, "#C5B3E2", "#C5B3E2", "#BFACDC")
        c.drawRect(x - t, top, x + t, bottom, p)
        c.drawRect(l, y - t, r, y + t, p)
        p.shader = null
        c.restore()
        // Overlay paths are built once. They preserve independent/diagonal state feedback.
        for ((bit, rect) in listOf(
            DpadState.UP to RectF(x - t, top, x + t, y - t),
            DpadState.DOWN to RectF(x - t, y + t, x + t, bottom),
            DpadState.LEFT to RectF(l, y - t, x - t, y + t),
            DpadState.RIGHT to RectF(x + t, y - t, r, y + t),
        )) {
            dpadArms += bit to Path().apply { addRect(rect, Path.Direction.CW); op(disk, Path.Op.INTERSECT) }
        }
    }
}
