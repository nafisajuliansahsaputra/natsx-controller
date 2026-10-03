package com.natsx.controller.feature.controller

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.BlurMaskFilter
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import com.natsx.controller.core.gamepad.DpadState
import org.json.JSONObject
import kotlin.math.ceil
import kotlin.math.min
import kotlin.math.sqrt

/** Exact exported Figma layers; raster/blur work is performed only when layout changes. */
internal class ControllerSkin(private val context: Context) {
    data class Node(val id: String, val x: Float, val y: Float, val radius: Float, val rect: RectF?, val travel: Float)
    private class Sprite(val bitmap: Bitmap, val bounds: RectF)
    private class Button(val idle: Sprite, val pressed: Sprite)
    private val bitmapPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val feedbackPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = 0x224D3A7C }
    private val editorPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 2f }
    private val strokePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1f }
    private val assets = mutableMapOf<String, Bitmap>()
    private val manifest = context.assets.open("controller/figma-dark/manifest.json").bufferedReader().use { JSONObject(it.readText()) }
    private var background: Sprite? = null
    private val buttons = mutableMapOf<String, Button>()
    private val nodes = mutableMapOf<String, Node>()
    private val dpadArms = mutableListOf<Pair<Int, Path>>()
    private val caps = mutableMapOf<String, Button>()
    private var unit = 1f
    private var rasterScale = 1f
    private val px get() = unit / 1080f
    private var insetX = 0f
    private var insetY = 0f
    private var viewportScaleX = 1f
    private var viewportScaleY = 1f
    private val connectionStrokes = ConnectionStrokePaths.create()

    private fun asset(name: String): Bitmap = assets.getOrPut(name) {
        context.assets.open("controller/figma-dark/$name.png").use { checkNotNull(BitmapFactory.decodeStream(it)) }
    }
    private fun layer(c: Canvas, name: String, dx: Float = 0f, dy: Float = 0f) {
        val b = manifest.getJSONObject("parts").getJSONArray(name)
        val w = b.getDouble(2).toFloat(); val h = b.getDouble(3).toFloat()
        c.drawBitmap(asset(name), null, RectF(dx, dy, dx + w * px, dy + h * px), bitmapPaint)
    }
    private fun positionedLayer(c: Canvas, name: String, centerX: Float, centerY: Float) {
        val b = manifest.getJSONObject("parts").getJSONArray(name)
        layer(c, name, centerX - b.getDouble(2).toFloat() / 2f * px,
            centerY - b.getDouble(3).toFloat() / 2f * px)
    }
    fun rebuild(w: Float, h: Float, insetX: Float, insetY: Float, usableW: Float, usableH: Float, geometry: List<Node>) {
        buttons.clear(); nodes.clear(); dpadArms.clear(); caps.clear()
        nodes.putAll(geometry.associateBy { it.id })
        unit = min(usableW / 2400f, usableH / 1080f) * 1080f
        this.insetX = insetX; this.insetY = insetY; viewportScaleX = usableW / 2400f; viewportScaleY = usableH / 1080f
        rasterScale = min(1f, min(1080f / h, 2400f / w))
        background = raster(RectF(0f, 0f, w, h)) { c ->
            c.drawColor(Color.rgb(26, 26, 26))
            c.drawBitmap(asset("background"), null, RectF(insetX, insetY, insetX + usableW, insetY + usableH), bitmapPaint)
            for (node in geometry) if (node.id.endsWith("_STICK")) positionedLayer(c, if (node.id == "LEFT_STICK") "stick-base-left" else "stick-base", node.x, node.y)
            drawDpadBase(c)
        }
        for (node in geometry) {
            if (node.id.endsWith("_STICK") || node.id.startsWith("DPAD_")) continue
            val r = node.rect ?: RectF(node.x - node.radius, node.y - node.radius, node.x + node.radius, node.y + node.radius)
            val bounds = RectF().also { keyPath(node.id, r).computeBounds(it, true) }.apply { inset(-48f * px, -48f * px) }
            buttons[node.id] = Button(raster(bounds) { drawKey(it, node.id, r, false) }, raster(bounds) { drawKey(it, node.id, r, true) })
        }
        val capBounds = RectF(-208f * px, -208f * px, 208f * px, 208f * px)
        for (id in listOf("LEFT_STICK", "RIGHT_STICK")) {
            val name = if (id == "LEFT_STICK") "stick-cap-left" else "stick-cap"
            caps[id] = Button(raster(capBounds) { positionedLayer(it, name, 0f, 0f) },
                raster(capBounds) { positionedLayer(it, name, 0f, 0f); it.drawCircle(0f, 0f, 190f * px, feedbackPaint) })
        }
    }
    private fun raster(bounds: RectF, draw: (Canvas) -> Unit): Sprite {
        val bitmap = Bitmap.createBitmap(ceil(bounds.width() * rasterScale).toInt().coerceAtLeast(1),
            ceil(bounds.height() * rasterScale).toInt().coerceAtLeast(1), Bitmap.Config.ARGB_8888)
        val c = Canvas(bitmap)
        c.scale(rasterScale, rasterScale); c.translate(-bounds.left, -bounds.top); draw(c)
        return Sprite(bitmap, bounds)
    }
    private fun drawSprite(c: Canvas, sprite: Sprite) { c.drawBitmap(sprite.bitmap, null, sprite.bounds, bitmapPaint) }
    fun drawBackground(c: Canvas) { background?.let { drawSprite(c, it) } }
    fun drawConnectionStroke(c: Canvas, transport: ControllerTransportIndicator?) {
        strokePaint.color = transport.indicatorColor()
        c.save(); c.translate(insetX, insetY); c.scale(viewportScaleX, viewportScaleY)
        for (path in connectionStrokes) c.drawPath(path, strokePaint)
        c.restore()
    }
    fun drawButton(c: Canvas, id: String, active: Boolean) {
        val button = buttons[id] ?: return
        drawSprite(c, if (active) button.pressed else button.idle)
    }
    fun drawStick(c: Canvas, id: String, x: Int, y: Int, active: Boolean) {
        val node = nodes[id] ?: return; val sprite = caps[id] ?: return
        val nx = (x / if (x < 0) 32768f else 32767f).coerceIn(-1f, 1f)
        val ny = (y / if (y < 0) 32768f else 32767f).coerceIn(-1f, 1f)
        val travel = 110f * px / sqrt(nx * nx + ny * ny).coerceAtLeast(1f)
        c.save(); c.translate(node.x + nx * travel, node.y - ny * travel)
        drawSprite(c, if (active) sprite.pressed else sprite.idle); c.restore()
    }
    fun drawDpad(c: Canvas, dpad: Int) {
        for ((bit, path) in dpadArms) if (dpad and bit != 0) c.drawPath(path, feedbackPaint)
    }
    fun drawEditorTarget(c: Canvas, x: Float, y: Float, selected: Boolean) {
        editorPaint.color = Color.parseColor(if (selected) "#574379" else "#B7A5D8")
        c.drawCircle(x, y, 19f * px, editorPaint)
    }
    private fun drawMissingBevel(c: Canvas, key: Path, rim: Float) {
        // Same neutral, translucent backplate and soft spread as the supplied LT/RT bevel.
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = 0x40292929; style = Paint.Style.FILL_AND_STROKE
            strokeJoin = Paint.Join.ROUND; strokeWidth = 2f * rim
            maskFilter = BlurMaskFilter(6f * px, BlurMaskFilter.Blur.NORMAL)
        }
        c.drawPath(key, p)
        p.maskFilter = null; c.drawPath(key, p)
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
        val key = keyPath(id, r)
        if (id in listOf("LB", "RB", "BACK", "START", "L3", "R3", "GUIDE")) {
            drawMissingBevel(c, key, (if (id == "LB" || id == "RB") 12f else 6f) * px)
        }
        positionedLayer(c, id, r.centerX(), r.centerY())
        if (active) c.drawPath(key, feedbackPaint)
    }
    private fun drawDpadBase(c: Canvas) {
        val up = nodes["DPAD_UP"] ?: return; val down = nodes["DPAD_DOWN"] ?: return
        val left = nodes["DPAD_LEFT"] ?: return; val right = nodes["DPAD_RIGHT"] ?: return
        val x = (left.x + right.x) / 2f; val y = (up.y + down.y) / 2f
        positionedLayer(c, "dpad", x, y)
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
