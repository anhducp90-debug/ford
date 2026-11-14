package com.inkboard.canvas

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.DashPathEffect
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.text.Layout
import android.text.StaticLayout
import android.text.TextPaint
import android.util.LruCache
import com.inkboard.model.Affine
import com.inkboard.model.Background
import com.inkboard.model.Document
import com.inkboard.model.Edge
import com.inkboard.model.Element
import com.inkboard.model.ImageEl
import com.inkboard.model.Node
import com.inkboard.model.NodeKind
import com.inkboard.model.PenType
import com.inkboard.model.ShapeEl
import com.inkboard.model.ShapeKind
import com.inkboard.model.StickyEl
import com.inkboard.model.Stroke
import com.inkboard.model.TableEl
import com.inkboard.model.TextEl
import com.inkboard.model.TextMetrics
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

/** Draws document elements onto an Android (Skia) canvas in world coordinates. */
class Renderer {
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val line = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND
    }
    private val text = TextPaint(Paint.ANTI_ALIAS_FLAG or Paint.SUBPIXEL_TEXT_FLAG)
    private val bmpPaint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private val bgPaint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val tmpRect = RectF()
    private val tmpPath = Path()
    private val images = object : LruCache<String, Bitmap>(64 * 1024 * 1024) {
        override fun sizeOf(key: String, value: Bitmap) = value.allocationByteCount
    }

    init {
        // Real text measurement for the model layer.
        val measurer = TextPaint(Paint.ANTI_ALIAS_FLAG)
        TextMetrics.measure = { s, size -> synchronized(measurer) { measurer.textSize = size; measurer.measureText(s) } }
    }

    fun draw(c: Canvas, doc: Document, e: Element) {
        when (e) {
            is Stroke -> drawStroke(c, e)
            is ShapeEl -> drawShape(c, e)
            is TextEl -> drawText(c, e)
            is StickyEl -> drawSticky(c, e)
            is Node -> drawNode(c, e)
            is ImageEl -> drawImage(c, doc, e)
            is TableEl -> drawTable(c, e)
            is Edge -> Unit // drawn by drawConnections
        }
    }

    /** Edges and mind-map branches. [lookup] lets callers substitute moving nodes. */
    fun drawConnections(c: Canvas, doc: Document, lookup: (Long) -> Node?) {
        for (e in doc.elements) {
            if (e is Node && e.parentId != 0L) {
                val p = lookup(e.parentId) ?: continue
                val n = lookup(e.id) ?: continue
                drawBranch(c, p, n)
            } else if (e is Edge) {
                val a = lookup(e.from) ?: continue
                val b = lookup(e.to) ?: continue
                val (x0, y0) = a.borderToward(b.x + b.w / 2, b.y + b.h / 2)
                val (x1, y1) = b.borderToward(a.x + a.w / 2, a.y + a.h / 2)
                line.color = e.color; line.strokeWidth = e.width; line.alpha = 255
                c.drawLine(x0, y0, x1, y1, line)
                arrowHead(c, x0, y0, x1, y1, e.width, e.color)
            }
        }
    }

    private fun drawBranch(c: Canvas, p: Node, n: Node) {
        val childRight = n.x + n.w / 2 >= p.x + p.w / 2
        val x0 = if (childRight) p.x + p.w else p.x
        val y0 = p.y + p.h / 2
        val x1 = if (childRight) n.x else n.x + n.w
        val y1 = n.y + n.h / 2
        val mx = (x0 + x1) / 2
        tmpPath.reset()
        tmpPath.moveTo(x0, y0)
        tmpPath.cubicTo(mx, y0, mx, y1, x1, y1)
        line.color = darken(n.fill, 0.55f); line.strokeWidth = max(1.5f, n.size * 0.14f); line.alpha = 255
        c.drawPath(tmpPath, line)
    }

    // ----------------------------------------------------------------- strokes

    fun drawStroke(c: Canvas, s: Stroke) {
        val path = (s.cache as? Path) ?: buildStrokePath(s.pts, s.width, s.pen).also { s.cache = it }
        when (s.pen) {
            PenType.PEN -> { fill.color = s.color; c.drawPath(path, fill) }
            PenType.HIGHLIGHTER -> {
                line.color = s.color; line.alpha = 90; line.strokeWidth = s.width
                line.strokeCap = Paint.Cap.SQUARE
                c.drawPath(path, line)
                line.strokeCap = Paint.Cap.ROUND; line.alpha = 255
            }
            PenType.PENCIL -> {
                line.color = s.color; line.alpha = 200; line.strokeWidth = s.width
                c.drawPath(path, line)
                line.alpha = 255
            }
        }
    }

    /** Live (in-progress) stroke; [n] = number of valid triples in [pts]. */
    fun drawLiveStroke(c: Canvas, pts: FloatArray, n: Int, color: Int, width: Float, pen: PenType) {
        val path = buildStrokePath(pts, width, pen, n)
        when (pen) {
            PenType.PEN -> { fill.color = color; c.drawPath(path, fill) }
            else -> {
                line.color = color; line.alpha = if (pen == PenType.HIGHLIGHTER) 90 else 200
                line.strokeWidth = width
                if (pen == PenType.HIGHLIGHTER) line.strokeCap = Paint.Cap.SQUARE
                c.drawPath(path, line)
                line.strokeCap = Paint.Cap.ROUND; line.alpha = 255
            }
        }
    }

    // ------------------------------------------------------------------ shapes

    fun drawShape(c: Canvas, s: ShapeEl) {
        val g = s.geo
        line.color = s.color; line.strokeWidth = s.width; line.alpha = 255
        when (s.kind) {
            ShapeKind.LINE -> c.drawLine(g[0], g[1], g[2], g[3], line)
            ShapeKind.ARROW -> {
                c.drawLine(g[0], g[1], g[2], g[3], line)
                arrowHead(c, g[0], g[1], g[2], g[3], s.width, s.color)
            }
            ShapeKind.POLYGON -> {
                tmpPath.reset()
                tmpPath.moveTo(g[0], g[1])
                var i = 2
                while (i + 1 < g.size) { tmpPath.lineTo(g[i], g[i + 1]); i += 2 }
                tmpPath.close()
                if (s.fill != 0) { fill.color = s.fill; c.drawPath(tmpPath, fill) }
                c.drawPath(tmpPath, line)
            }
            ShapeKind.ELLIPSE -> {
                c.save()
                c.rotate(Math.toDegrees(g[4].toDouble()).toFloat(), g[0], g[1])
                tmpRect.set(g[0] - g[2], g[1] - g[3], g[0] + g[2], g[1] + g[3])
                if (s.fill != 0) { fill.color = s.fill; c.drawOval(tmpRect, fill) }
                c.drawOval(tmpRect, line)
                c.restore()
            }
        }
    }

    private fun arrowHead(c: Canvas, x0: Float, y0: Float, x1: Float, y1: Float, w: Float, color: Int) {
        val ang = atan2(y1 - y0, x1 - x0)
        val len = max(10f, w * 4.5f)
        val spread = 0.45f
        tmpPath.reset()
        tmpPath.moveTo(x1, y1)
        tmpPath.lineTo(x1 - len * cos(ang - spread), y1 - len * sin(ang - spread))
        tmpPath.lineTo(x1 - len * cos(ang + spread), y1 - len * sin(ang + spread))
        tmpPath.close()
        fill.color = color
        c.drawPath(tmpPath, fill)
    }

    // -------------------------------------------------------------------- text

    private fun drawText(c: Canvas, t: TextEl) {
        text.color = t.color; text.textSize = t.size
        val lh = TextMetrics.lineHeight(t.size)
        var y = t.y + t.size
        for (ln in t.text.split('\n')) {
            c.drawText(ln, t.x, y, text)
            y += lh
        }
    }

    private fun layout(s: String, size: Float, width: Float, color: Int, align: Layout.Alignment): StaticLayout {
        text.textSize = size; text.color = color
        return StaticLayout.Builder.obtain(s, 0, s.length, text, max(1, width.toInt()))
            .setAlignment(align).setIncludePad(false).build()
    }

    private fun drawSticky(c: Canvas, s: StickyEl) {
        val r = s.w * 0.04f
        fill.color = 0x22000000
        tmpRect.set(s.x + r * 0.4f, s.y + r * 0.8f, s.x + s.w + r * 0.4f, s.y + s.h + r * 0.8f)
        c.drawRoundRect(tmpRect, r, r, fill)
        fill.color = s.color
        tmpRect.set(s.x, s.y, s.x + s.w, s.y + s.h)
        c.drawRoundRect(tmpRect, r, r, fill)
        if (s.text.isNotEmpty()) {
            val pad = s.size * 0.8f
            val l = layout(s.text, s.size, s.w - 2 * pad, 0xFF333333.toInt(), Layout.Alignment.ALIGN_NORMAL)
            c.save(); c.clipRect(tmpRect); c.translate(s.x + pad, s.y + pad); l.draw(c); c.restore()
        }
    }

    private fun drawNode(c: Canvas, n: Node) {
        tmpRect.set(n.x, n.y, n.x + n.w, n.y + n.h)
        fill.color = n.fill
        line.color = darken(n.fill, 0.55f); line.strokeWidth = max(1f, n.size * 0.1f); line.alpha = 255
        when (n.kind) {
            NodeKind.ROUND -> { val r = min(n.h / 2, n.size); c.drawRoundRect(tmpRect, r, r, fill); c.drawRoundRect(tmpRect, r, r, line) }
            NodeKind.RECT -> { c.drawRect(tmpRect, fill); c.drawRect(tmpRect, line) }
            NodeKind.ELLIPSE -> { c.drawOval(tmpRect, fill); c.drawOval(tmpRect, line) }
            NodeKind.DIAMOND -> {
                tmpPath.reset()
                tmpPath.moveTo(n.x + n.w / 2, n.y); tmpPath.lineTo(n.x + n.w, n.y + n.h / 2)
                tmpPath.lineTo(n.x + n.w / 2, n.y + n.h); tmpPath.lineTo(n.x, n.y + n.h / 2); tmpPath.close()
                c.drawPath(tmpPath, fill); c.drawPath(tmpPath, line)
            }
        }
        if (n.text.isNotEmpty()) {
            val inset = if (n.kind == NodeKind.DIAMOND || n.kind == NodeKind.ELLIPSE) n.w * 0.15f else n.size * 0.6f
            val l = layout(n.text, n.size, n.w - 2 * inset, 0xFF222222.toInt(), Layout.Alignment.ALIGN_CENTER)
            c.save()
            c.translate(n.x + inset, n.y + (n.h - l.height) / 2)
            l.draw(c)
            c.restore()
        }
    }

    private fun drawTable(c: Canvas, t: TableEl) {
        line.color = t.color; line.strokeWidth = t.width; line.alpha = 255
        // Header row tint
        fill.color = (t.color and 0x00FFFFFF) or 0x14000000
        c.drawRect(t.x, t.y, t.x + t.w, t.y + t.h / t.rows, fill)
        for (i in 0..t.cols) { val x = t.x + t.w * i / t.cols; c.drawLine(x, t.y, x, t.y + t.h, line) }
        for (j in 0..t.rows) { val y = t.y + t.h * j / t.rows; c.drawLine(t.x, y, t.x + t.w, y, line) }
    }

    // ------------------------------------------------------------------ images

    private fun drawImage(c: Canvas, doc: Document, e: ImageEl) {
        val bmp = images.get(e.key) ?: doc.images[e.key]?.let { bytes ->
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.also { images.put(e.key, it) }
        }
        tmpRect.set(e.x, e.y, e.x + e.w, e.y + e.h)
        if (bmp != null) c.drawBitmap(bmp, null, tmpRect, bmpPaint)
        else { fill.color = Color.LTGRAY; c.drawRect(tmpRect, fill) }
    }

    // -------------------------------------------------------------- background

    /** Screen-space background pattern for camera (scale, tx, ty). */
    fun drawBackground(c: Canvas, bg: Background, w: Int, h: Int, scale: Float, tx: Float, ty: Float, dark: Boolean) {
        c.drawColor(if (dark) 0xFF1E1F22.toInt() else 0xFFFBFBF9.toInt())
        if (bg == Background.PLAIN) return
        var step = 32f * scale
        while (step < 18f) step *= 4f
        while (step > 18f * 4f) step /= 4f
        val ox = ((tx % step) + step) % step
        val oy = ((ty % step) + step) % step
        bgPaint.color = if (dark) 0x33FFFFFF else 0x22000000
        when (bg) {
            Background.DOTS -> {
                bgPaint.strokeWidth = 2.6f; bgPaint.strokeCap = Paint.Cap.ROUND
                val cols = (w / step).toInt() + 2; val rows = (h / step).toInt() + 2
                val pts = FloatArray(cols * rows * 2)
                var k = 0
                for (i in 0 until cols) for (j in 0 until rows) { pts[k++] = ox + i * step; pts[k++] = oy + j * step }
                c.drawPoints(pts, bgPaint)
            }
            Background.GRID -> {
                bgPaint.strokeWidth = 1f
                var x = ox; while (x < w) { c.drawLine(x, 0f, x, h.toFloat(), bgPaint); x += step }
                var y = oy; while (y < h) { c.drawLine(0f, y, w.toFloat(), y, bgPaint); y += step }
            }
            Background.LINES -> {
                bgPaint.strokeWidth = 1f
                var y = oy; while (y < h) { c.drawLine(0f, y, w.toFloat(), y, bgPaint); y += step }
            }
            Background.PLAIN -> Unit
        }
    }

    /** Render a whole document (exports, thumbnails). [a] maps world → output. */
    fun drawAll(c: Canvas, doc: Document, a: Affine) {
        c.save()
        c.translate(a.tx, a.ty); c.scale(a.s, a.s)
        drawConnections(c, doc) { doc.byId(it) as? Node }
        for (e in doc.elements) draw(c, doc, e)
        c.restore()
    }

    companion object {
        val dash = DashPathEffect(floatArrayOf(14f, 10f), 0f)

        fun darken(color: Int, f: Float): Int {
            val a = Color.alpha(color).coerceAtLeast(200)
            return Color.argb(a, (Color.red(color) * f).toInt(), (Color.green(color) * f).toInt(), (Color.blue(color) * f).toInt())
        }

        /**
         * Builds the outline of a stroke. PEN strokes become a filled,
         * pressure-modulated polygon; other pens become a smoothed centre line.
         */
        fun buildStrokePath(pts: FloatArray, width: Float, pen: PenType, count: Int = pts.size / 3): Path {
            val path = Path()
            val n = count
            if (n == 0) return path
            if (pen != PenType.PEN) {
                path.moveTo(pts[0], pts[1])
                if (n == 1) { path.lineTo(pts[0] + 0.01f, pts[1]); return path }
                for (i in 1 until n - 1) {
                    val mx = (pts[3 * i] + pts[3 * i + 3]) / 2; val my = (pts[3 * i + 1] + pts[3 * i + 4]) / 2
                    path.quadTo(pts[3 * i], pts[3 * i + 1], mx, my)
                }
                path.lineTo(pts[3 * (n - 1)], pts[3 * (n - 1) + 1])
                return path
            }
            fun half(i: Int) = width / 2f * (0.3f + 0.7f * pts[3 * i + 2].coerceIn(0f, 1f))
            if (n == 1) { path.addCircle(pts[0], pts[1], half(0), Path.Direction.CW); return path }
            // Light smoothing of positions (3-tap) to remove sensor jitter.
            val xs = FloatArray(n); val ys = FloatArray(n); val hs = FloatArray(n)
            for (i in 0 until n) {
                val a = max(0, i - 1); val b = min(n - 1, i + 1)
                xs[i] = (pts[3 * a] + 2 * pts[3 * i] + pts[3 * b]) / 4f
                ys[i] = (pts[3 * a + 1] + 2 * pts[3 * i + 1] + pts[3 * b + 1]) / 4f
                hs[i] = half(i)
            }
            xs[0] = pts[0]; ys[0] = pts[1]; xs[n - 1] = pts[3 * (n - 1)]; ys[n - 1] = pts[3 * (n - 1) + 1]
            val lx = FloatArray(n); val ly = FloatArray(n); val rx = FloatArray(n); val ry = FloatArray(n)
            for (i in 0 until n) {
                val a = max(0, i - 1); val b = min(n - 1, i + 1)
                var dx = xs[b] - xs[a]; var dy = ys[b] - ys[a]
                val len = hypot(dx, dy)
                if (len < 1e-4f) { dx = 1f; dy = 0f } else { dx /= len; dy /= len }
                lx[i] = xs[i] - dy * hs[i]; ly[i] = ys[i] + dx * hs[i]
                rx[i] = xs[i] + dy * hs[i]; ry[i] = ys[i] - dx * hs[i]
            }
            path.moveTo(lx[0], ly[0])
            for (i in 1 until n) path.quadTo(lx[i - 1], ly[i - 1], (lx[i - 1] + lx[i]) / 2, (ly[i - 1] + ly[i]) / 2)
            path.lineTo(lx[n - 1], ly[n - 1])
            path.lineTo(rx[n - 1], ry[n - 1])
            for (i in n - 1 downTo 1) path.quadTo(rx[i], ry[i], (rx[i] + rx[i - 1]) / 2, (ry[i] + ry[i - 1]) / 2)
            path.lineTo(rx[0], ry[0])
            path.close()
            path.addCircle(xs[0], ys[0], hs[0], Path.Direction.CW)
            path.addCircle(xs[n - 1], ys[n - 1], hs[n - 1], Path.Direction.CW)
            path.fillType = Path.FillType.WINDING
            return path
        }
    }
}
