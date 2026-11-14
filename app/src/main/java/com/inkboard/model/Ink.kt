package com.inkboard.model

import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max

/** Smart interpretations of freshly drawn ink. */
object InkGestures {

    /**
     * Scratch-out: a back-and-forth zigzag. Returns true when the stroke
     * reverses direction along its main axis at least 4 times and its path is
     * much longer than its extent.
     */
    fun isScratch(xy: FloatArray): Boolean {
        val n = xy.size / 2
        if (n < 8) return false
        val b = Box.of(xy)
        val major = max(b.w, b.h)
        if (major < 4f) return false
        var len = 0f
        for (i in 1 until n) len += hypot(xy[2 * i] - xy[2 * i - 2], xy[2 * i + 1] - xy[2 * i - 1])
        if (len < major * 3.2f) return false
        val horizontal = b.w >= b.h
        var reversals = 0
        var dirSign = 0
        var acc = 0f
        val minRun = major * 0.25f
        for (i in 1 until n) {
            val d = if (horizontal) xy[2 * i] - xy[2 * i - 2] else xy[2 * i + 1] - xy[2 * i - 1]
            acc += d
            if (abs(acc) >= minRun) {
                val s = if (acc > 0) 1 else -1
                if (dirSign != 0 && s != dirSign) reversals++
                dirSign = s
                acc = 0f
            }
        }
        return reversals >= 4
    }

    /** Elements a scratch-out removes: crossed by the stroke and mostly under its extent. */
    fun scratchTargets(doc: Document, xy: FloatArray, tol: Float): List<Long> {
        val b = Box.of(xy).inset(-tol)
        val out = ArrayList<Long>()
        for (e in doc.query(b)) {
            if (e.id in doc.locked) continue
            val eb = e.bounds
            val ix = minOf(eb.r, b.r) - maxOf(eb.l, b.l)
            val iy = minOf(eb.b, b.b) - maxOf(eb.t, b.t)
            if (ix <= 0 || iy <= 0) continue
            val cover = (ix * iy) / max(1e-3f, eb.w * eb.h)
            if (cover < 0.5f) continue
            var crossed = false
            var i = 0
            while (i + 1 < xy.size && !crossed) { if (e.hit(xy[i], xy[i + 1], tol, solid = true)) crossed = true; i += 2 }
            if (crossed) out += e.id
        }
        return out
    }

    /** Connectable element under (x, y): the smallest one, so lines inside a frame hit the inner boxes. */
    fun targetAt(doc: Document, x: Float, y: Float, tol: Float, exclude: Set<Long> = emptySet()): Element? =
        doc.query(Box(x - tol, y - tol, x + tol, y + tol))
            .filter { it.id !in exclude && it.id !in doc.hiddenIds() && it.id !in doc.locked && Anchors.connectable(it) && Anchors.contains(it, x, y, tol) }
            .minByOrNull { it.bounds.w * it.bounds.h }

    /**
     * Hand-drawn connector: a stroke (or a straight line) that starts on one
     * element and ends on another becomes an arrow between them.
     */
    fun connectorFor(doc: Document, xy: FloatArray, tol: Float, exclude: Set<Long> = emptySet()): Pair<Element, Element>? {
        val n = xy.size / 2
        if (n < 2) return null
        val a = targetAt(doc, xy[0], xy[1], tol, exclude) ?: return null
        val b = targetAt(doc, xy[2 * n - 2], xy[2 * n - 1], tol, exclude) ?: return null
        if (a.id == b.id) return null
        // One end must not contain the other (a line drawn inside a frame to a box in it is not a connector).
        if (a.bounds.inset(-tol).let { it.l <= b.bounds.l && it.r >= b.bounds.r && it.t <= b.bounds.t && it.b >= b.bounds.b }) return null
        if (b.bounds.inset(-tol).let { it.l <= a.bounds.l && it.r >= a.bounds.r && it.t <= a.bounds.t && it.b >= a.bounds.b }) return null
        // Reject scribbles: the path should not be much longer than the gap it bridges.
        var len = 0f
        for (i in 1 until n) len += hypot(xy[2 * i] - xy[2 * i - 2], xy[2 * i + 1] - xy[2 * i - 1])
        val chord = hypot(xy[2 * n - 2] - xy[0], xy[2 * n - 1] - xy[1])
        if (len > chord * 2.2f + tol * 4) return null
        return a to b
    }

    /** Connectors crossed by a scratch-out stroke. */
    fun scratchEdges(doc: Document, xy: FloatArray, tol: Float): List<Long> {
        val out = ArrayList<Long>()
        for (e in doc.connections()) {
            if (e !is Edge) continue
            val ends = doc.edgeEnds(e) ?: continue
            var i = 0
            while (i + 1 < xy.size) {
                if (Geo.distToSegment(xy[i], xy[i + 1], ends[0], ends[1], ends[2], ends[3]) <= tol + e.width) { out += e.id; break }
                i += 2
            }
        }
        return out
    }
}
