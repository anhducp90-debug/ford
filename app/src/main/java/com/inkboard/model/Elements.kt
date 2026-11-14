package com.inkboard.model

import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.sin
import kotlin.math.sqrt

enum class PenType { PEN, HIGHLIGHTER, PENCIL }
enum class ShapeKind { LINE, ARROW, POLYGON, ELLIPSE }
enum class NodeKind { ROUND, RECT, ELLIPSE, DIAMOND }

/** Text width measurement; replaced by a Paint-based measurer in the app. */
object TextMetrics {
    @Volatile
    var measure: (String, Float) -> Float = { s, size -> s.length * size * 0.55f }
    fun lineHeight(size: Float) = size * 1.3f
    fun width(text: String, size: Float) = text.split('\n').maxOf { measure(it, size) }
    fun lines(text: String) = text.split('\n').size
}

/**
 * Immutable canvas element. Edits create new instances (same id), which keeps
 * undo/redo trivial and lets renderers cache per instance.
 */
sealed class Element {
    abstract val id: Long
    abstract val bounds: Box
    abstract fun transformed(a: Affine): Element
    abstract fun withId(newId: Long): Element
    /** Hit test; [solid] also counts the interior of closed elements. */
    abstract fun hit(x: Float, y: Float, tol: Float, solid: Boolean): Boolean
    /** True if this element counts as selected by a lasso polygon. */
    open fun inLasso(poly: FloatArray): Boolean = Geo.pointInPolygon(bounds.cx, bounds.cy, poly)

    /** Render cache slot (not part of identity). */
    @Transient
    var cache: Any? = null

    /** Encoded bytes cache for fast saves (elements are immutable, so it never goes stale). */
    @Transient
    @Volatile
    var enc: ByteArray? = null
}

class Stroke(
    override val id: Long,
    /** Packed x, y, pressure triples. */
    val pts: FloatArray,
    val color: Int,
    val width: Float,
    val pen: PenType,
    /** Thin the stroke at its start and end (calligraphic look). */
    val taper: Boolean = false,
) : Element() {
    val count get() = pts.size / 3
    override val bounds: Box by lazy { Box.of(pts, 3, width) }

    override fun transformed(a: Affine): Element {
        val out = FloatArray(pts.size)
        var i = 0
        while (i < pts.size) {
            out[i] = a.x(pts[i]); out[i + 1] = a.y(pts[i + 1]); out[i + 2] = pts[i + 2]
            i += 3
        }
        return Stroke(id, out, color, width * a.s, pen, taper)
    }

    override fun withId(newId: Long) = Stroke(newId, pts, color, width, pen, taper)
    fun withColor(c: Int) = Stroke(id, pts, c, width, pen, taper)

    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean): Boolean {
        val pad = tol + width / 2
        val bb = bounds
        if (x < bb.l - pad || x > bb.r + pad || y < bb.t - pad || y > bb.b + pad) return false
        return Geo.distToPolyline(x, y, pts, 3) <= pad
    }

    override fun inLasso(poly: FloatArray): Boolean {
        val n = count
        if (n == 0) return false
        var inside = 0
        for (i in 0 until n) if (Geo.pointInPolygon(pts[3 * i], pts[3 * i + 1], poly)) inside++
        return inside >= 0.6f * n
    }

    /** x,y only (for recognizers). */
    fun xy(): FloatArray = FloatArray(count * 2) { k -> pts[(k / 2) * 3 + (k % 2)] }
}

class ShapeEl(
    override val id: Long,
    val kind: ShapeKind,
    /** LINE/ARROW: x0,y0,x1,y1. POLYGON: x,y pairs. ELLIPSE: cx,cy,rx,ry,angle. */
    val geo: FloatArray,
    val color: Int,
    val width: Float,
    val fill: Int = 0,
    /** Text placed inside a closed shape; always auto-fitted to the shape. */
    val label: String = "",
    val labelColor: Int = 0xFF202124.toInt(),
) : Element() {
    val closed get() = kind == ShapeKind.POLYGON || kind == ShapeKind.ELLIPSE

    /** Area inside the shape where a label fits (world coordinates). */
    val labelBox: Box by lazy { TextFit.innerBox(this) }

    /** Fitted label layout, computed once per instance. */
    val labelFit: TextFit.Fit by lazy { TextFit.fitLabel(label, labelBox.w, labelBox.h) }

    override val bounds: Box by lazy {
        when (kind) {
            ShapeKind.ELLIPSE -> {
                val (cx, cy, rx, ry) = listOf(geo[0], geo[1], geo[2], geo[3])
                val a = geo[4].toDouble()
                val hw = sqrt((rx * cos(a)).let { it * it } + (ry * sin(a)).let { it * it }).toFloat()
                val hh = sqrt((rx * sin(a)).let { it * it } + (ry * cos(a)).let { it * it }).toFloat()
                Box(cx - hw - width, cy - hh - width, cx + hw + width, cy + hh + width)
            }
            ShapeKind.ARROW -> Box.of(geo, 2, width * 4 + 12f)
            else -> Box.of(geo, 2, width)
        }
    }

    override fun transformed(a: Affine): Element {
        val g = geo.copyOf()
        if (kind == ShapeKind.ELLIPSE) {
            g[0] = a.x(geo[0]); g[1] = a.y(geo[1]); g[2] = geo[2] * a.s; g[3] = geo[3] * a.s
        } else {
            var i = 0
            while (i + 1 < g.size) { g[i] = a.x(geo[i]); g[i + 1] = a.y(geo[i + 1]); i += 2 }
        }
        return ShapeEl(id, kind, g, color, width * a.s, fill, label, labelColor)
    }

    override fun withId(newId: Long) = ShapeEl(newId, kind, geo, color, width, fill, label, labelColor)

    fun withStyle(color: Int = this.color, fill: Int = this.fill) = ShapeEl(id, kind, geo, color, width, fill, label, labelColor)
    fun withLabel(t: String, c: Int = labelColor) = ShapeEl(id, kind, geo, color, width, fill, t, c)

    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean): Boolean {
        val pad = tol + width / 2
        return when (kind) {
            ShapeKind.LINE, ShapeKind.ARROW -> Geo.distToPolyline(x, y, geo) <= pad
            ShapeKind.POLYGON -> Geo.distToPolyline(x, y, geo, closed = true) <= pad ||
                ((solid || fill != 0) && Geo.pointInPolygon(x, y, geo))
            ShapeKind.ELLIPSE -> {
                val a = -geo[4].toDouble()
                val dx = x - geo[0]; val dy = y - geo[1]
                val u = (dx * cos(a) - dy * sin(a)).toFloat()
                val v = (dx * sin(a) + dy * cos(a)).toFloat()
                val rx = max(geo[2], 1f); val ry = max(geo[3], 1f)
                val r = hypot(u / rx, v / ry)
                abs(r - 1f) * minOf(rx, ry) <= pad || ((solid || fill != 0) && r <= 1f)
            }
        }
    }
}

class TextEl(
    override val id: Long,
    val x: Float,
    val y: Float,
    val text: String,
    val size: Float,
    val color: Int,
) : Element() {
    override val bounds: Box by lazy {
        val w = TextMetrics.width(text, size)
        Box(x, y, x + max(w, size), y + TextMetrics.lines(text) * TextMetrics.lineHeight(size))
    }
    override fun transformed(a: Affine) = TextEl(id, a.x(x), a.y(y), text, size * a.s, color)
    override fun withId(newId: Long) = TextEl(newId, x, y, text, size, color)
    fun withText(t: String) = TextEl(id, x, y, t, size, color)
    fun withColor(c: Int) = TextEl(id, x, y, text, size, c)
    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean) = bounds.inset(-tol).contains(x, y)
}

class StickyEl(
    override val id: Long,
    val x: Float,
    val y: Float,
    val w: Float,
    val h: Float,
    val text: String,
    val color: Int,
    val size: Float,
) : Element() {
    override val bounds: Box by lazy { Box(x, y, x + w, y + h) }
    override fun transformed(a: Affine) = StickyEl(id, a.x(x), a.y(y), w * a.s, h * a.s, text, color, size * a.s)
    override fun withId(newId: Long) = StickyEl(newId, x, y, w, h, text, color, size)
    fun withText(t: String) = StickyEl(id, x, y, w, h, t, color, size)
    fun withColor(c: Int) = StickyEl(id, x, y, w, h, text, c, size)
    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean) = bounds.inset(-tol).contains(x, y)
}

/** Connectable box used by flowcharts and mind maps. */
class Node(
    override val id: Long,
    val x: Float,
    val y: Float,
    val w: Float,
    val h: Float,
    val text: String,
    val kind: NodeKind,
    val fill: Int,
    val size: Float,
    /** Mind-map parent (0 = none). */
    val parentId: Long = 0L,
    /** True for nodes that belong to a mind map (root or branch). */
    val mindmap: Boolean = false,
) : Element() {
    override val bounds: Box by lazy { Box(x, y, x + w, y + h) }
    override fun transformed(a: Affine) =
        Node(id, a.x(x), a.y(y), w * a.s, h * a.s, text, kind, fill, size * a.s, parentId, mindmap)
    override fun withId(newId: Long) = Node(newId, x, y, w, h, text, kind, fill, size, parentId, mindmap)
    fun copy(
        x: Float = this.x, y: Float = this.y, w: Float = this.w, h: Float = this.h,
        text: String = this.text, kind: NodeKind = this.kind, fill: Int = this.fill,
        parentId: Long = this.parentId, mindmap: Boolean = this.mindmap,
    ) = Node(id, x, y, w, h, text, kind, fill, size, parentId, mindmap)

    /** Resize to fit text, keeping the centre fixed. */
    fun fitText(t: String): Node {
        val pad = size * 1.4f
        val nw = max(size * 6f, TextMetrics.width(t, size) + pad * 2)
        val nh = max(size * 2.6f, TextMetrics.lines(t) * TextMetrics.lineHeight(size) + pad)
        return copy(x = x + w / 2 - nw / 2, y = y + h / 2 - nh / 2, w = nw, h = nh, text = t)
    }

    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean) = bounds.inset(-tol).contains(x, y)

    /** Point on the node border in the direction of (tx, ty) from the centre. */
    fun borderToward(tx: Float, ty: Float): Pair<Float, Float> {
        val cx = x + w / 2; val cy = y + h / 2
        val dx = tx - cx; val dy = ty - cy
        if (abs(dx) < 1e-3f && abs(dy) < 1e-3f) return cx to cy
        val hw = w / 2; val hh = h / 2
        val t = when (kind) {
            NodeKind.ELLIPSE -> 1f / sqrt((dx / hw) * (dx / hw) + (dy / hh) * (dy / hh))
            NodeKind.DIAMOND -> 1f / (abs(dx) / hw + abs(dy) / hh)
            else -> minOf(if (dx != 0f) hw / abs(dx) else Float.MAX_VALUE, if (dy != 0f) hh / abs(dy) else Float.MAX_VALUE)
        }
        return (cx + dx * t) to (cy + dy * t)
    }
}

/** Arrow connecting two nodes; follows them when they move. */
class Edge(
    override val id: Long,
    val from: Long,
    val to: Long,
    val color: Int,
    val width: Float,
) : Element() {
    /** Edge geometry depends on its nodes; the Document supplies real bounds. */
    override val bounds: Box get() = Box.EMPTY
    override fun transformed(a: Affine) = Edge(id, from, to, color, width * a.s)
    override fun withId(newId: Long) = Edge(newId, from, to, color, width)
    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean) = false
    override fun inLasso(poly: FloatArray) = false
}

class ImageEl(
    override val id: Long,
    val x: Float,
    val y: Float,
    val w: Float,
    val h: Float,
    /** Key into Document.images. */
    val key: String,
) : Element() {
    override val bounds: Box by lazy { Box(x, y, x + w, y + h) }
    override fun transformed(a: Affine) = ImageEl(id, a.x(x), a.y(y), w * a.s, h * a.s, key)
    override fun withId(newId: Long) = ImageEl(newId, x, y, w, h, key)
    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean) = bounds.inset(-tol).contains(x, y)
}

/** Simple table: uniform grid; write in cells by hand or with text on top. */
class TableEl(
    override val id: Long,
    val x: Float,
    val y: Float,
    val w: Float,
    val h: Float,
    val rows: Int,
    val cols: Int,
    val color: Int,
    val width: Float,
    /** Cell texts keyed by row * cols + col. */
    val cells: Map<Int, String> = emptyMap(),
) : Element() {
    override val bounds: Box by lazy { Box(x - width, y - width, x + w + width, y + h + width) }
    override fun transformed(a: Affine) = TableEl(id, a.x(x), a.y(y), w * a.s, h * a.s, rows, cols, color, width * a.s, cells)
    override fun withId(newId: Long) = TableEl(newId, x, y, w, h, rows, cols, color, width, cells)

    fun cellAt(px: Float, py: Float): Int {
        val c = ((px - x) / (w / cols)).toInt().coerceIn(0, cols - 1)
        val r = ((py - y) / (h / rows)).toInt().coerceIn(0, rows - 1)
        return r * cols + c
    }
    fun cellBox(i: Int): Box {
        val r = i / cols; val c = i % cols
        val cw = w / cols; val ch = h / rows
        return Box(x + c * cw, y + r * ch, x + (c + 1) * cw, y + (r + 1) * ch)
    }
    fun withCell(i: Int, t: String): TableEl {
        val m = HashMap(cells); if (t.isBlank()) m.remove(i) else m[i] = t
        return TableEl(id, x, y, w, h, rows, cols, color, width, m)
    }

    /** Thread-safe: thumbnails render on a background thread. */
    private val cellFits = java.util.concurrent.ConcurrentHashMap<Int, TextFit.Fit>()
    fun cellFit(i: Int): TextFit.Fit {
        val m = cellFits
        return m.getOrPut(i) {
            val b = cellBox(i).inset(minOf(w / cols, h / rows) * 0.08f)
            TextFit.fit(cells[i].orEmpty(), b.w, b.h, maxSize = (h / rows) * 0.45f)
        }
    }
    fun resized(rows: Int, cols: Int): TableEl {
        val r = rows.coerceIn(1, 100); val c = cols.coerceIn(1, 50)
        val m = HashMap<Int, String>()
        for ((i, t) in cells) { val rr = i / cols; val cc = i % cols; if (rr < r && cc < c) m[rr * c + cc] = t }
        return TableEl(id, x, y, w / this.cols * c, h / this.rows * r, r, c, color, width, m)
    }
    fun withColor(c: Int) = TableEl(id, x, y, w, h, rows, cols, c, width, cells)
    override fun hit(x: Float, y: Float, tol: Float, solid: Boolean): Boolean {
        val b = Box(this.x, this.y, this.x + w, this.y + h)
        if (!b.inset(-tol - width).contains(x, y)) return false
        if (solid) return true
        // Eraser: only grid lines count.
        for (i in 0..cols) if (abs(x - (this.x + w * i / cols)) <= tol + width) return true
        for (j in 0..rows) if (abs(y - (this.y + h * j / rows)) <= tol + width) return true
        return false
    }
}
