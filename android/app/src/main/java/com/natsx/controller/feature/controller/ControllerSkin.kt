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
import android.graphics.RadialGradient
import android.graphics.Shader
import android.graphics.Typeface
import com.natsx.controller.core.gamepad.DpadState
import kotlin.math.ceil
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt

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
        val source = context.assets.open("controller/$name.png").use { checkNotNull(BitmapFactory.decodeStream(it)) }
        // Retone neutral backplates and their baked depth once. Colored faces and
        // standalone white glyph/icon assets keep their original pixels.
        if (name !in setOf("panel", "imgVector", "imgVector1", "imgEllipse19", "imgEllipse5",
                "imgRectangle20", "imgRectangle21", "imgRectangle22", "imgRectangle23")) return@getOrPut source
        val pixels = IntArray(source.width * source.height)
        source.getPixels(pixels, 0, source.width, 0, 0, source.width, source.height)
        for (i in pixels.indices) {
            val color = pixels[i]
            val red = Color.red(color); val green = Color.green(color); val blue = Color.blue(color)
            if (Color.alpha(color) == 0 || max(red, max(green, blue)) - min(red, min(green, blue)) > 18) continue
            val light = (red + green + blue) / 3
            val dark = if (light <= 245) light * 41 / 245 else 41 + (light - 245) * 26 / 10
            pixels[i] = Color.argb(Color.alpha(color), dark, dark, dark)
        }
        Bitmap.createBitmap(pixels, source.width, source.height, Bitmap.Config.ARGB_8888)
    }

    private fun layer(c: Canvas, name: String, x: Float, y: Float, w: Float, h: Float) {
        c.drawBitmap(asset(name), null, RectF(x, y, x + w, y + h), bitmapPaint)
    }

    fun rebuild(w: Float, h: Float, insetX: Float, insetY: Float, usableW: Float, usableH: Float, geometry: List<Node>) {
        // Do not recycle old bitmaps while RenderThread may still hold them.
        buttons.clear(); nodes.clear(); dpadArms.clear()
        nodes.putAll(geometry.associateBy { it.id })
        unit = min(usableW / 2400f, usableH / 1080f) * 1080f
        rasterScale = min(1f, min(1080f / h, 2400f / w))
        background = raster(RectF(0f, 0f, w, h)) { c ->
            c.drawColor(Color.parseColor("#292929"))
            c.save(); c.translate(insetX, insetY); c.scale(usableW / 2400f, usableH / 1080f)
            layer(c, "panel", 749f, 0f, 902f, 325f)
            layer(c, "imgVector", 1746f, 846f, 654f, 234f)
            layer(c, "imgVector1", 0f, 846f, 667f, 234f)
            c.restore()
            for (node in geometry) if (node.id.endsWith("_STICK")) {
                drawStickBase(c, node.x, node.y)
            }
            drawDpadBase(c)
        }
        for (node in geometry) {
            if (node.id.endsWith("_STICK") || node.id.startsWith("DPAD_")) continue
            val r = node.rect ?: RectF(node.x - node.radius, node.y - node.radius, node.x + node.radius, node.y + node.radius)
            val bounds = RectF().also { keyPath(node.id, r).computeBounds(it, true) }.apply { inset(-48f * px, -48f * px) }
            buttons[node.id] = Button(raster(bounds) { drawKey(it, node.id, r, false) },
                raster(bounds) { drawKey(it, node.id, r, true) })
        }
        val capBounds = RectF(-170f * px, -170f * px, 170f * px, 170f * px)
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
        val magnitude = sqrt(nx * nx + ny * ny).coerceAtLeast(1f)
        val travel = 110f * px / magnitude
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

    private fun drawStickBase(c: Canvas, x: Float, y: Float) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.parseColor("#601A1A1A")
            style = Paint.Style.FILL_AND_STROKE
            strokeWidth = 12f * px
            maskFilter = BlurMaskFilter(6f * px, BlurMaskFilter.Blur.NORMAL)
        }
        c.drawCircle(x, y, 250f * px, p)
        p.maskFilter = null; p.style = Paint.Style.FILL; p.alpha = 255
        p.shader = LinearGradient(x - 250f * px, y - 250f * px, x + 250f * px, y + 250f * px,
            intArrayOf(Color.parseColor("#575757"), Color.parseColor("#434343"), Color.parseColor("#292929")),
            floatArrayOf(0f, 0.45f, 1f), Shader.TileMode.CLAMP)
        c.drawCircle(x, y, 250f * px, p)
        p.shader = null
        p.color = Color.parseColor("#801C4B1E")
        p.style = Paint.Style.FILL_AND_STROKE; p.strokeWidth = 12f * px
        p.maskFilter = BlurMaskFilter(12f * px, BlurMaskFilter.Blur.NORMAL)
        c.drawCircle(x, y, 222.5f * px, p)
        p.maskFilter = null; p.style = Paint.Style.FILL
        drawRecessedCircle(c, x, y, 222.5f * px, "#37B039", "#29802C", p)
    }

    private fun drawRecessedCircle(c: Canvas, x: Float, y: Float, r: Float,
        fill: String, edge: String, p: Paint) {
        p.shader = RadialGradient(x, y, r,
            intArrayOf(Color.parseColor(fill), Color.parseColor(fill), Color.parseColor(edge)),
            floatArrayOf(0f, (1f - 18f * px / r).coerceAtLeast(0f), 1f), Shader.TileMode.CLAMP)
        c.drawCircle(x, y, r, p)
        p.shader = null
    }

    private fun drawCap(c: Canvas, active: Boolean) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        // Shrink the complete concentric cap from 380px to 310px; keep its layers together.
        c.save(); c.scale(155f / 190f, 155f / 190f)
        // Analytic concentric circles avoid the seams/resampling artifacts of stacked exports.
        drawRecessedCircle(c, 0f, 0f, 190f * px, "#93E294", "#588F59", p)
        p.color = Color.parseColor("#80FFFFFF")
        p.maskFilter = BlurMaskFilter(12f * px, BlurMaskFilter.Blur.NORMAL)
        c.drawCircle(0f, 0f, 140f * px, p)
        p.maskFilter = null; p.color = Color.parseColor("#B2EBB2")
        c.drawCircle(0f, 0f, 140f * px, p)
        p.shader = RadialGradient(0f, 0f, 125f * px,
            intArrayOf(Color.parseColor("#B2EBB2"), Color.parseColor("#5DCB5F"), Color.parseColor("#55BA57")),
            floatArrayOf(0f, 0.93f, 1f), Shader.TileMode.CLAMP)
        c.drawCircle(0f, 0f, 125f * px, p)
        p.shader = null
        if (active) c.drawCircle(0f, 0f, 190f * px, feedbackPaint)
        c.restore()
    }

    private fun drawDarkBevel(c: Canvas, key: Path, rim: Float) {
        val p = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.parseColor("#601A1A1A")
            style = Paint.Style.FILL_AND_STROKE
            strokeJoin = Paint.Join.ROUND
            strokeWidth = 2f * rim
            maskFilter = BlurMaskFilter(6f * px, BlurMaskFilter.Blur.NORMAL)
        }
        // Short soft shadow under one continuous silhouette, not a thick black halo.
        c.save(); c.translate(2f * px, 3f * px); c.drawPath(key, p); c.restore()
        p.color = Color.parseColor("#20434343")
        p.strokeWidth = 2f * (rim + 4f * px)
        c.drawPath(key, p)
        p.maskFilter = null; p.strokeWidth = 2f * rim; p.alpha = 255
        val bounds = RectF().also { key.computeBounds(it, true) }
        p.shader = LinearGradient(bounds.left, bounds.top, bounds.right, bounds.bottom,
            intArrayOf(Color.parseColor("#575757"), Color.parseColor("#434343"), Color.parseColor("#292929")),
            floatArrayOf(0f, 0.45f, 1f), Shader.TileMode.CLAMP)
        c.drawPath(key, p)
        p.shader = null
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
        val key = keyPath(id, r)
        drawDarkBevel(c, key, (if (circle || mint) 6f else 12f) * px)
        val depth = (if (mint) 6f else 12f) * px
        // The backplate already owns the soft drop shadow. Keep the face inset
        // independent so a second dark stroke cannot muddy the bevel.
        p.style = Paint.Style.FILL
        p.color = Color.parseColor(if (mint) "#B2EBB2" else "#C3B1E1")
        c.drawPath(key, p)
        c.save(); c.clipPath(key)
        val outside = Path(key).apply { fillType = Path.FillType.INVERSE_WINDING }
        // Software bitmap canvas reproduces inset shadows once; no blur in onDraw/onTouch.
        p.color = Color.WHITE
        p.setShadowLayer(depth, depth, depth, Color.parseColor("#40FFFFFF")); c.drawPath(outside, p)
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
