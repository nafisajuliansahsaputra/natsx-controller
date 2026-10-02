package com.natsx.controller.feature.controller

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.CornerPathEffect
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
            p.shader = gradient(0f, h, "#FCFCFD", "#EBEBEE")
            c.drawRect(0f, 0f, w, h, p)
            val panel = Path().apply {
                moveTo(insetX + usableW * 0.340f, insetY)
                lineTo(insetX + usableW * 0.660f, insetY)
                lineTo(insetX + usableW * 0.553f, insetY + usableH * 0.381f)
                quadTo(insetX + usableW * 0.542f, insetY + usableH * 0.415f,
                    insetX + usableW * 0.518f, insetY + usableH * 0.415f)
                lineTo(insetX + usableW * 0.482f, insetY + usableH * 0.415f)
                quadTo(insetX + usableW * 0.458f, insetY + usableH * 0.415f,
                    insetX + usableW * 0.447f, insetY + usableH * 0.381f)
                close()
            }
            p.shader = gradient(insetY, insetY + usableH * 0.415f, "#F9F9FA", "#EEEAF6")
            p.setShadowLayer(unit * 0.014f, 0f, unit * 0.003f, Color.parseColor("#DCD2EE"))
            c.drawPath(panel, p)
            p.clearShadowLayer()
            p.shader = null
            p.color = Color.WHITE
            p.style = Paint.Style.STROKE
            p.strokeWidth = unit * 0.004f
            c.drawPath(panel, p)
            p.color = Color.parseColor("#E6DBF7")
            p.strokeWidth = unit * 0.0015f
            c.drawPath(panel, p)
            for (node in geometry) {
                if (node.id.endsWith("_STICK")) drawPlate(c, node.x, node.y, unit * 0.208f)
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
        val capRadius = unit * 0.128f
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
        p.color = Color.parseColor("#F7F7FA")
        p.setShadowLayer(r * 0.10f, 0f, r * 0.09f, Color.parseColor("#42000000"))
        c.drawCircle(x, y, r, p)
        p.clearShadowLayer()
        p.shader = gradient(y - r, y + r, "#FFFFFF", "#D9D9DF")
        c.drawCircle(x, y, r, p)
        p.shader = null
        p.style = Paint.Style.STROKE
        p.strokeWidth = unit * 0.0025f
        p.color = Color.parseColor("#CEBEDF")
        c.drawCircle(x, y, r * 0.985f, p)
        p.color = Color.WHITE
        c.drawCircle(x, y - unit * 0.002f, r * 0.97f, p)
        p.style = Paint.Style.FILL
        p.shader = RadialGradient(x, y, r * 0.82f,
            intArrayOf(Color.parseColor("#B6BDB7"), Color.parseColor("#D2D5D2"), Color.parseColor("#F0F0F2")),
            floatArrayOf(0f, 0.78f, 1f), Shader.TileMode.CLAMP)
        c.drawCircle(x, y, r * 0.82f, p)
        p.shader = null
        p.color = Color.parseColor("#DEDDE4")
        p.strokeWidth = unit * 0.003f
        p.strokeCap = Paint.Cap.ROUND
        val markR = r * 0.88f
        for (i in 0..5) {
            val angle = Math.PI * 2 * i / 6 + Math.PI / 2
            val dx = cos(angle).toFloat()
            val dy = sin(angle).toFloat()
            c.drawLine(x + dx * markR, y + dy * markR,
                x + dx * (markR + r * 0.035f), y + dy * (markR + r * 0.035f), p)
        }
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
        val p = Path()
        val h = r.height()
        val w = r.width()
        when (id) {
            "A", "B", "X", "Y" -> p.addOval(r, Path.Direction.CW)
            "LT", "RT" -> p.addRoundRect(r, h * 0.28f, h * 0.28f, Path.Direction.CW)
            "LB", "RB" -> {
                val taper = w * 0.21f
                if (id == "LB") {
                    p.moveTo(r.left, r.top); p.lineTo(r.right - taper, r.top)
                    p.lineTo(r.right, r.bottom); p.lineTo(r.left, r.bottom)
                } else {
                    p.moveTo(r.left + taper, r.top); p.lineTo(r.right, r.top)
                    p.lineTo(r.right, r.bottom); p.lineTo(r.left, r.bottom)
                }
                p.close()
            }
            "GUIDE" -> {
                p.moveTo(r.left, r.top); p.lineTo(r.right, r.top)
                p.lineTo(r.right - w * 0.21f, r.bottom); p.lineTo(r.left + w * 0.21f, r.bottom); p.close()
            }
            else -> {
                val slant = w * 0.18f
                if (id == "BACK" || id == "L3") {
                    p.moveTo(r.left, r.top); p.lineTo(r.right - slant, r.top)
                    p.lineTo(r.right, r.bottom); p.lineTo(r.left + slant * 0.25f, r.bottom)
                } else {
                    p.moveTo(r.left + slant, r.top); p.lineTo(r.right, r.top)
                    p.lineTo(r.right - slant * 0.25f, r.bottom); p.lineTo(r.left, r.bottom)
                }
                p.close()
            }
        }
        return p
    }

    private fun drawKey(c: Canvas, id: String, r: RectF, active: Boolean) {
        val mint = id in listOf("BACK", "START", "L3", "R3", "GUIDE")
        val circle = id in listOf("A", "B", "X", "Y")
        val depth = unit * if (active) 0.003f else 0.008f
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.pathEffect = CornerPathEffect(r.height() * 0.23f)
        val rim = keyPath(id, RectF(r).apply { inset(-unit * 0.009f, -unit * 0.009f) })
        p.color = Color.parseColor("#ECEAF0")
        p.setShadowLayer(unit * 0.012f, 0f, unit * 0.012f, Color.parseColor("#38000000"))
        c.drawPath(rim, p)
        p.clearShadowLayer()
        p.style = Paint.Style.STROKE
        p.strokeWidth = unit * 0.002f
        p.color = Color.parseColor("#CDC7D4")
        c.drawPath(rim, p)
        p.style = Paint.Style.FILL
        p.color = Color.parseColor(if (mint) "#315941" else "#51466A")
        c.save(); c.translate(0f, depth); c.drawPath(keyPath(id, r), p); c.restore()
        val face = keyPath(id, r)
        p.shader = gradient(r.top, r.bottom,
            if (mint) "#C1F2C9" else "#E0CDFB",
            if (mint) (if (active) "#6FAE7B" else "#84C58E") else (if (active) "#A28BBC" else "#B7A3D6"),
            if (mint) "#589566" else "#9D8CBE")
        c.save(); if (active) c.translate(0f, unit * 0.004f)
        c.drawPath(face, p)
        p.shader = null
        p.style = Paint.Style.STROKE
        p.strokeWidth = unit * 0.0018f
        p.color = Color.parseColor(if (mint) "#609969" else "#9B8AAE")
        c.drawPath(face, p)
        val highlight = RectF(r).apply { inset(unit * 0.007f, unit * 0.007f) }
        p.color = Color.parseColor("#66FFFFFF")
        p.strokeWidth = unit * 0.002f
        c.drawPath(keyPath(id, highlight), p)
        p.pathEffect = null
        p.color = Color.parseColor("#FAF8FF")
        p.style = Paint.Style.FILL
        p.textAlign = Paint.Align.CENTER
        p.typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
        p.textSize = r.height() * if (circle) 0.42f else 0.31f
        val label = when (id) { "L3" -> "LS"; "R3" -> "RS"; "BACK", "START", "GUIDE" -> ""; else -> id }
        if (label.isNotEmpty()) {
            c.drawText(label, r.centerX(), r.centerY() - (p.ascent() + p.descent()) / 2f, p)
        } else drawUtilityIcon(c, id, r, p)
        c.restore()
    }

    private fun drawUtilityIcon(c: Canvas, id: String, r: RectF, p: Paint) {
        val x = r.centerX(); val y = r.centerY(); val s = r.height() * 0.24f
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
        val radius = unit * 0.215f
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.color = Color.parseColor("#F3F0F7")
        p.setShadowLayer(unit * 0.018f, 0f, unit * 0.014f, Color.parseColor("#38000000"))
        c.drawCircle(x, y, radius * 1.055f, p)
        p.clearShadowLayer()
        p.shader = gradient(y - radius, y + radius, "#E1CDF7", "#A38DBF")
        c.drawCircle(x, y, radius, p)
        p.shader = null; p.style = Paint.Style.STROKE
        p.color = Color.parseColor("#544561"); p.strokeWidth = unit * 0.0025f
        c.drawCircle(x, y, radius, p)
        p.color = Color.WHITE; p.strokeWidth = unit * 0.002f
        c.drawCircle(x, y - unit * 0.003f, radius * 1.025f, p)
        val t = unit * 0.066f
        val l = left.x - left.radius * 0.94f; val r = right.x + right.radius * 0.94f
        val top = up.y - up.radius * 0.94f; val bottom = down.y + down.radius * 0.94f
        val cross = Path().apply {
            moveTo(x - t, top); lineTo(x + t, top); lineTo(x + t, y - t)
            lineTo(r, y - t); lineTo(r, y + t); lineTo(x + t, y + t)
            lineTo(x + t, bottom); lineTo(x - t, bottom); lineTo(x - t, y + t)
            lineTo(l, y + t); lineTo(l, y - t); lineTo(x - t, y - t); close()
        }
        p.style = Paint.Style.FILL; p.pathEffect = CornerPathEffect(unit * 0.018f)
        p.color = Color.parseColor("#69557E")
        p.setShadowLayer(unit * 0.008f, 0f, unit * 0.01f, Color.parseColor("#55000000"))
        c.save(); c.translate(0f, unit * 0.008f); c.drawPath(cross, p); c.restore()
        p.clearShadowLayer()
        p.shader = gradient(top, bottom, "#D5BEF0", "#B4A0D1", "#AA95C8")
        c.drawPath(cross, p)
        p.shader = null; p.style = Paint.Style.STROKE
        p.color = Color.parseColor("#AAFFFFFF"); p.strokeWidth = unit * 0.003f
        c.drawPath(cross, p)
        p.pathEffect = null; p.strokeCap = Paint.Cap.ROUND
        p.color = Color.parseColor("#F4EDFF"); p.strokeWidth = unit * 0.007f
        val mark = unit * 0.014f
        for (n in listOf(up, down)) c.drawLine(n.x, n.y - mark, n.x, n.y + mark, p)
        for (n in listOf(left, right)) c.drawLine(n.x - mark, n.y, n.x + mark, n.y, p)
        // Overlay paths are built once. They preserve independent/diagonal state feedback.
        for ((bit, rect) in listOf(
            DpadState.UP to RectF(x - t, top, x + t, y - t),
            DpadState.DOWN to RectF(x - t, y + t, x + t, bottom),
            DpadState.LEFT to RectF(l, y - t, x - t, y + t),
            DpadState.RIGHT to RectF(x + t, y - t, r, y + t),
        )) {
            dpadArms += bit to Path().apply { addRoundRect(rect, unit * 0.012f, unit * 0.012f, Path.Direction.CW) }
        }
    }
}
