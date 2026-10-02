package com.natsx.controller.feature.controller

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.BlurMaskFilter
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import com.natsx.controller.core.gamepad.DpadState
import kotlin.math.ceil
import kotlin.math.min

/** Original Figma layers and native key silhouettes. Raster/shadow work happens on layout only. */
internal class ControllerSkin(private val context: Context) {
    data class Node(val id: String, val x: Float, val y: Float, val radius: Float, val rect: RectF?, val travel: Float)
    private class Sprite(val bitmap: Bitmap, val bounds: RectF)
    private class Button(val idle: Sprite, val pressed: Sprite)
    private val bitmapPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val feedbackPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0x334D3A7C }
    private val editorPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 2f }
    private val assets = mutableMapOf<String, Bitmap>()
    private val font = Typeface.createFromAsset(context.assets, "controller/jakarta-bold.ttf")
    private var background: Sprite? = null
    private val buttons = mutableMapOf<String, Button>()
    private val nodes = mutableMapOf<String, Node>()
    private val dpadArms = mutableListOf<Pair<Int, Path>>()
    private var cap: Button? = null
    private var unit = 1f
    private var rasterScale = 1f
    private val px get() = unit / 1080f

    private fun asset(name: String): Bitmap = assets.getOrPut(name) {
        context.assets.open("controller/$name.png").use { checkNotNull(BitmapFactory.decodeStream(it)) }
    }

    private fun layer(c: Canvas, name: String, x: Float, y: Float, w: Float, h: Float) {
        c.drawBitmap(asset(name), null, RectF(x, y, x + w, y + h), bitmapPaint)
    }

    fun rebuild(w: Float, h: Float, insetX: Float, insetY: Float, usableW: Float, usableH: Float, geometry: List<Node>) {
        // Do not recycle old bitmaps while RenderThread may still hold them.
        buttons.clear(); nodes.clear(); dpadArms.clear()
        nodes.putAll(geometry.associateBy { it.id })
        unit = min(usableW, usableH)
        rasterScale = min(1f, min(1080f / h, 2400f / w))
        background = raster(RectF(0f, 0f, w, h)) { c ->
            c.drawColor(Color.parseColor("#F5F5F5"))
            c.save(); c.translate(insetX, insetY); c.scale(px, px)
            layer(c, "panel", 749f, 0f, 902f, 325f)
            layer(c, "imgVector", 1746f, 846f, 654f, 234f)
            layer(c, "imgVector1", 0f, 846f, 667f, 234f)
            c.restore()
            for (node in geometry) if (node.id.endsWith("_STICK")) {
                layer(c, "analog_plate", node.x - 274f * px, node.y - 274f * px, 548f * px, 548f * px)
                // Socket center is 249.5px in the original 500px analog frame.
                layer(c, "analog_socket", node.x - 241f * px, node.y - 241f * px, 481f * px, 481f * px)
                layer(c, "analog_ring", node.x - 190f * px, node.y - 190f * px, 380f * px, 380f * px)
            }
            drawDpadBase(c)
        }
        for (node in geometry) {
            if (node.id.endsWith("_STICK") || node.id.startsWith("DPAD_")) continue
            val r = node.rect ?: RectF(node.x - node.radius, node.y - node.radius, node.x + node.radius, node.y + node.radius)
            val bounds = RectF(r).apply { inset(-40f * px, -40f * px) }
            buttons[node.id] = Button(raster(bounds) { drawKey(it, node.id, r, false) },
                raster(bounds) { drawKey(it, node.id, r, true) })
        }
        val capBounds = RectF(-152f * px, -152f * px, 152f * px, 152f * px)
        cap = Button(raster(capBounds) { drawCap(it, false) }, raster(capBounds) { drawCap(it, true) })
    }

    private fun raster(bounds: RectF, draw: (Canvas) -> Unit): Sprite {
        val bitmap = Bitmap.createBitmap(ceil(bounds.width() * rasterScale).toInt().coerceAtLeast(1),
            ceil(bounds.height() * rasterScale).toInt().coerceAtLeast(1), Bitmap.Config.ARGB_8888)
        val c = Canvas(bitmap)
        c.scale(rasterScale, rasterScale); c.translate(-bounds.left, -bounds.top)
        draw(c)
        return Sprite(bitmap, bounds)
    }

    private fun drawSprite(c: Canvas, sprite: Sprite) { c.drawBitmap(sprite.bitmap, null, sprite.bounds, bitmapPaint) }
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
        c.save(); c.translate(node.x + nx * node.travel, node.y - ny * node.travel)
        drawSprite(c, if (active) sprite.pressed else sprite.idle); c.restore()
    }
    fun drawDpad(c: Canvas, dpad: Int) {
        for ((bit, path) in dpadArms) if (dpad and bit != 0) c.drawPath(path, feedbackPaint)
    }
    fun drawEditorTarget(c: Canvas, x: Float, y: Float, selected: Boolean) {
        editorPaint.color = Color.parseColor(if (selected) "#574379" else "#B7A5D8")
        c.drawCircle(x, y, 19f * px, editorPaint)
    }

    private fun drawCap(c: Canvas, active: Boolean) {
        layer(c, "analog_cap_ring", -152f * px, -152f * px, 304f * px, 304f * px)
        layer(c, "analog_cap", -125f * px, -125f * px, 250f * px, 250f * px)
        if (active) c.drawCircle(0f, 0f, 125f * px, feedbackPaint)
    }

    /** Figma rounded rectangles under their original affine transforms, unioned without seams. */
    private fun keyPath(id: String, r: RectF): Path {
        val w = r.width(); val h = r.height()
        val path = Path()
        fun rounded(width: Float, height: Float, radius: Float, skew: Float, sy: Float, tx: Float): Path {
            val part = Path().apply { addRoundRect(RectF(0f, 0f, width, height), radius, radius, Path.Direction.CW) }
            part.transform(Matrix().apply { setValues(floatArrayOf(1f, skew, tx, 0f, sy, 0f, 0f, 0f, 1f)) })
            return part
        }
        when (id) {
            "A", "B", "X", "Y" -> path.addOval(RectF(0f, 0f, w, h), Path.Direction.CW)
            "LT", "RT" -> path.addRoundRect(RectF(0f, 0f, w, h), 36f * px, 36f * px, Path.Direction.CW)
            "LB", "RB" -> {
                path.set(rounded(247f * px, h, 36f * px, 0f, 1f, 0f))
                // Original rounded rectangle under Figma's 30-degree shear and 0.866 Y scale.
                path.op(rounded(252.4f * px, 173.205f * px, 36f * px, 0.5f, 0.8660254f, 25f * px), Path.Op.UNION)
            }
            "GUIDE" -> {
                path.set(rounded(271.6f * px, 86.603f * px, 16f * px, 0.5f, 0.8660254f, 0f))
                path.op(rounded(271.6f * px, 86.603f * px, 16f * px, -0.5f, 0.8660254f, 197.701f * px), Path.Op.UNION)
                // Both sides lean inward at the bottom.
            }
            else -> path.set(rounded(144f * px, 86.603f * px, 16f * px, 0.5f, 0.8660254f, 0f))
        }
        if (id == "RB" || id == "START" || id == "R3") {
            path.transform(Matrix().apply { setScale(-1f, 1f, w / 2f, h / 2f) })
        }
        path.offset(r.left, r.top)
        return path
    }

    private fun drawKey(c: Canvas, id: String, r: RectF, active: Boolean) {
        val mint = id in listOf("BACK", "START", "L3", "R3", "GUIDE")
        val circle = id in listOf("A", "B", "X", "Y")
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        if (circle) layer(c, "imgEllipse15", r.centerX() - 119f * px, r.centerY() - 119f * px, 238f * px, 238f * px)
        if (id == "LT" || id == "RT") {
            val plate = RectF(r).apply { inset(-12f * px, -12f * px) }
            p.color = Color.WHITE; p.style = Paint.Style.FILL_AND_STROKE; p.strokeWidth = 24f * px
            p.setShadowLayer(12f * px, 0f, 0f, Color.WHITE)
            c.drawRoundRect(plate, 42f * px, 42f * px, p)
            p.clearShadowLayer(); p.style = Paint.Style.FILL
        }
        val key = keyPath(id, r)
        val depth = (if (mint) 6f else 12f) * px
        // Add the same drop depth to the originally flat LB/RB and utility keys.
        p.color = Color.parseColor("#803A264F")
        p.style = Paint.Style.FILL_AND_STROKE; p.strokeWidth = depth
        p.maskFilter = BlurMaskFilter(depth, BlurMaskFilter.Blur.NORMAL)
        c.drawPath(key, p)
        p.maskFilter = null; p.style = Paint.Style.FILL
        p.color = Color.parseColor(if (mint) "#B2EBB2" else "#C3B1E1")
        c.drawPath(key, p)
        c.save(); c.clipPath(key)
        val outside = Path(key).apply { fillType = Path.FillType.INVERSE_WINDING }
        // Software bitmap canvas reproduces inset shadows once; no blur in onDraw/onTouch.
        p.color = Color.WHITE
        p.setShadowLayer(depth, depth, depth, Color.parseColor("#80FFFFFF")); c.drawPath(outside, p)
        p.setShadowLayer(depth, -depth, -depth, Color.parseColor("#40000000")); c.drawPath(outside, p)
        p.clearShadowLayer()
        if (active) { p.color = 0x224D3A7C; c.drawPath(key, p) }
        c.restore()
        p.color = Color.WHITE; p.textAlign = Paint.Align.CENTER; p.typeface = font
        p.textSize = (if (mint) 24f else 48f) * px
        val label = when (id) { "L3" -> "LS"; "R3" -> "RS"; "BACK", "START", "GUIDE" -> ""; else -> id }
        if (label.isNotEmpty()) c.drawText(label, r.centerX(), r.centerY() - (p.ascent() + p.descent()) / 2f, p)
        else {
            val icon = when (id) { "BACK" -> "imgFluentTabDesktopMultiple24Regular"; "START" -> "imgCharmMenuHamburger"; else -> "imgFluentShareIos20Filled" }
            layer(c, icon, r.centerX() - 25f * px, r.centerY() - 25f * px, 50f * px, 50f * px)
        }
    }

    private fun drawDpadBase(c: Canvas) {
        val up = nodes["DPAD_UP"] ?: return; val down = nodes["DPAD_DOWN"] ?: return
        val left = nodes["DPAD_LEFT"] ?: return; val right = nodes["DPAD_RIGHT"] ?: return
        val x = (left.x + right.x) / 2f; val y = (up.y + down.y) / 2f
        layer(c, "imgEllipse19", x - 274f * px, y - 274f * px, 548f * px, 548f * px)
        layer(c, "imgEllipse5", x - 258f * px, y - 258f * px, 516f * px, 516f * px)
        layer(c, "imgRectangle20", down.x - 98f * px, down.y - 92f * px, 196f * px, 184f * px)
        layer(c, "imgRectangle21", right.x - 92f * px, right.y - 95f * px, 184f * px, 199f * px)
        layer(c, "imgRectangle22", left.x - 92f * px, left.y - 95f * px, 184f * px, 199f * px)
        layer(c, "imgRectangle23", up.x - 95f * px, up.y - 92f * px, 190f * px, 184f * px)
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            shader = LinearGradient(0f, y - 80f * px, 0f, y + 80f * px,
                Color.parseColor("#A484CE"), Color.parseColor("#C3B1E1"), Shader.TileMode.CLAMP)
        }
        c.drawRect(x - 80f * px, y - 80f * px, x + 80f * px, y + 80f * px, p)
        val disk = Path().apply { addCircle(x, y, 240f * px, Path.Direction.CW) }
        for ((bit, node) in listOf(DpadState.UP to up, DpadState.DOWN to down, DpadState.LEFT to left, DpadState.RIGHT to right)) {
            val path = Path().apply {
                addRect(node.x - 80f * px, node.y - 80f * px, node.x + 80f * px, node.y + 80f * px, Path.Direction.CW)
                op(disk, Path.Op.INTERSECT)
            }
            dpadArms += bit to path
        }
    }
}
