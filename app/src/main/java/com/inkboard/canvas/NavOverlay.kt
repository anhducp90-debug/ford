package com.inkboard.canvas

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import com.inkboard.model.Affine
import com.inkboard.model.Box
import com.inkboard.model.Document
import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.log10
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/**
 * Navigation aids drawn in screen space over the canvas:
 * - CAD-style rulers (top = X, left = Y, Y positive upwards) with a pointer marker
 * - origin marker
 * - minimap of the whole drawing with the current viewport; tap/drag to jump
 */
class NavOverlay(private val density: Float) {
    val rulerSize = 22f * density
    private val mapW = 168f * density
    private val mapH = 112f * density

    private val bg = Paint()
    private val tick = Paint(Paint.ANTI_ALIAS_FLAG).apply { strokeWidth = 1f }
    private val label = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = 9.5f * density }
    private val marker = Paint(Paint.ANTI_ALIAS_FLAG)
    private val frame = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1.5f * density }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)

    /** Minimap rect on screen (valid after draw). */
    val mapRect = RectF()

    // Minimap thumbnail cache
    private var mapBmp: Bitmap? = null
    private var mapVersion = -1L
    private var mapWorld = Box.EMPTY
    private var mapRenderedAt = 0L

    // ----------------------------------------------------------------- rulers

    /** "Nice" step (1, 2, 5 × 10^k) in world units so ticks are >= [minPx] apart. */
    fun niceStep(scale: Float, minPx: Float): Float {
        val raw = minPx / scale
        val exp = floor(log10(raw.toDouble()))
        val base = 10.0.pow(exp)
        val f = raw / base
        val m = when {
            f <= 1.0 -> 1.0
            f <= 2.0 -> 2.0
            f <= 5.0 -> 5.0
            else -> 10.0
        }
        return (m * base).toFloat()
    }

    fun fmt(v: Float, step: Float): String {
        if (abs(v) < step * 1e-3f) return "0"
        return if (step >= 1f) Math.round(v).toString()
        else {
            val decimals = (-floor(log10(step.toDouble()))).toInt().coerceIn(1, 4)
            "%.${decimals}f".format(v)
        }
    }

    fun drawRulers(
        c: Canvas, w: Int, h: Int, insetL: Float, insetT: Float,
        scale: Float, tx: Float, ty: Float, pointerX: Float, pointerY: Float, dark: Boolean,
    ) {
        val r = rulerSize
        val top = insetT
        val left = insetL
        bg.color = if (dark) 0xE62A2B2F.toInt() else 0xEEF3F3F1.toInt()
        tick.color = if (dark) 0x88FFFFFF.toInt() else 0x88000000.toInt()
        label.color = if (dark) 0xCCFFFFFF.toInt() else 0xCC333333.toInt()
        c.drawRect(left, top, w.toFloat(), top + r, bg)
        c.drawRect(left, top, left + r, h.toFloat(), bg)

        val step = niceStep(scale, 80f * density)
        val minor = step / 5f
        // X ruler
        var wx = floor(((left + r - tx) / scale) / minor) * minor
        val wxEnd = (w - tx) / scale
        while (wx <= wxEnd) {
            val sx = wx * scale + tx
            if (sx >= left + r) {
                val major = abs(wx / step - Math.round(wx / step)) < 1e-3f
                c.drawLine(sx, top + r - if (major) r * 0.5f else r * 0.22f, sx, top + r, tick)
                if (major) c.drawText(fmt(wx, step), sx + 3 * density, top + r * 0.45f, label)
            }
            wx += minor
        }
        // Y ruler (displayed with Y up = positive, like CAD)
        var wy = floor(((top + r - ty) / scale) / minor) * minor
        val wyEnd = (h - ty) / scale
        while (wy <= wyEnd) {
            val sy = wy * scale + ty
            if (sy >= top + r) {
                val major = abs(wy / step - Math.round(wy / step)) < 1e-3f
                c.drawLine(left + r - if (major) r * 0.5f else r * 0.22f, sy, left + r, sy, tick)
                if (major) {
                    c.save()
                    c.rotate(-90f, left + r * 0.45f, sy - 3 * density)
                    c.drawText(fmt(-wy, step), left + r * 0.45f, sy - 3 * density, label)
                    c.restore()
                }
            }
            wy += minor
        }
        // Pointer markers
        marker.color = 0xFF2F80ED.toInt()
        if (pointerX >= left + r) c.drawRect(pointerX - density, top, pointerX + density, top + r, marker)
        if (pointerY >= top + r) c.drawRect(left, pointerY - density, left + r, pointerY + density, marker)
        // Corner
        c.drawRect(left, top, left + r, top + r, bg)
    }

    /** Small cross at world (0,0). */
    fun drawOrigin(c: Canvas, scale: Float, tx: Float, ty: Float, dark: Boolean) {
        val x = tx; val y = ty
        marker.color = if (dark) 0x99FF8A80.toInt() else 0x99D93025.toInt()
        marker.strokeWidth = 1.5f * density
        val s = 8f * density
        c.drawLine(x - s, y, x + s, y, marker)
        c.drawLine(x, y - s, x, y + s, marker)
    }

    // ---------------------------------------------------------------- minimap

    fun layoutMap(w: Int, h: Int, insetR: Float, insetB: Float) {
        val right = w - insetR - 12f * density
        val bottom = h - insetB - 72f * density
        mapRect.set(right - mapW, bottom - mapH, right, bottom)
    }

    /** World box the minimap shows: content plus the current view, padded. */
    private fun mapWorldFor(doc: Document, view: Box): Box {
        val content = doc.contentBounds()
        val u = if (content.isEmpty) view else content.union(view)
        val pad = max(u.w, u.h) * 0.08f + 1f
        return Box(u.l - pad, u.t - pad, u.r + pad, u.b + pad)
    }

    private fun fitAffine(world: Box): Affine {
        val s = min(mapRect.width() / max(world.w, 1e-3f), mapRect.height() / max(world.h, 1e-3f))
        val ox = mapRect.left + (mapRect.width() - world.w * s) / 2 - world.l * s
        val oy = mapRect.top + (mapRect.height() - world.h * s) / 2 - world.t * s
        return Affine(s, ox, oy)
    }

    /** Background renderer for the minimap thumbnail (never blocks drawing). */
    private val worker = java.util.concurrent.Executors.newSingleThreadExecutor { r -> Thread(r, "minimap").apply { isDaemon = true; priority = Thread.MIN_PRIORITY } }
    private val workerRenderer by lazy { Renderer() }
    @Volatile private var pending = false
    private var requested = Box.EMPTY
    private val main = android.os.Handler(android.os.Looper.getMainLooper())
    /** Called on the main thread when a new thumbnail is ready. */
    var onThumbReady: (() -> Unit)? = null

    fun drawMinimap(c: Canvas, doc: Document, view: Box, renderer: Renderer, dark: Boolean, now: Long) {
        val world = mapWorldFor(doc, view)
        // Re-render the thumbnail only when content changed (throttled) or the framing changed a lot.
        val framingChanged = mapWorld.isEmpty || abs(world.w - mapWorld.w) > mapWorld.w * 0.15f ||
            abs(world.h - mapWorld.h) > mapWorld.h * 0.15f || !mapWorld.inset(-mapWorld.w * 0.05f).let { it.l <= world.l && it.r >= world.r && it.t <= world.t && it.b >= world.b }
        val contentChanged = doc.version != mapVersion
        if (!pending && (framingChanged || (contentChanged && now - mapRenderedAt > 600))) {
            renderThumbAsync(doc.snapshot(), world)
            mapVersion = doc.version; mapRenderedAt = now
        }
        fill.color = if (dark) 0xE62A2B2F.toInt() else 0xF2FFFFFF.toInt()
        c.drawRoundRect(mapRect, 10f * density, 10f * density, fill)
        mapBmp?.let { c.drawBitmap(it, mapRect.left, mapRect.top, null) }
        frame.color = if (dark) 0x55FFFFFF else 0x33000000
        c.drawRoundRect(mapRect, 10f * density, 10f * density, frame)
        // Viewport rectangle in the thumbnail's framing.
        val a = fitAffine(mapWorld)
        val vr = RectF(a.x(view.l), a.y(view.t), a.x(view.r), a.y(view.b))
        vr.intersect(mapRect)
        fill.color = 0x222F80ED
        c.drawRect(vr, fill)
        frame.color = 0xFF2F80ED.toInt()
        c.drawRect(vr, frame)
    }

    private fun renderThumbAsync(snap: Document, world: Box) {
        val bw = max(1, mapRect.width().toInt()); val bh = max(1, mapRect.height().toInt())
        val rect = android.graphics.RectF(mapRect)
        val s = min(rect.width() / max(world.w, 1e-3f), rect.height() / max(world.h, 1e-3f))
        val ox = (rect.width() - world.w * s) / 2 - world.l * s
        val oy = (rect.height() - world.h * s) / 2 - world.t * s
        pending = true
        requested = world
        worker.execute {
            val bmp = runCatching {
                Bitmap.createBitmap(bw, bh, Bitmap.Config.ARGB_8888).also { workerRenderer.drawAll(Canvas(it), snap, Affine(s, ox, oy)) }
            }.getOrNull()
            main.post {
                pending = false
                if (bmp != null) { mapBmp = bmp; mapWorld = world }
                onThumbReady?.invoke()
            }
        }
    }

    /** World point under a screen point inside the minimap. */
    fun mapToWorld(sx: Float, sy: Float): Pair<Float, Float> {
        val a = fitAffine(if (mapWorld.isEmpty) requested else mapWorld)
        return (sx - a.tx) / a.s to (sy - a.ty) / a.s
    }
}
