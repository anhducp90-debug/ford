package com.inkboard.model

import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * Where connectors attach. Any element except connectors and open ink can be
 * connected: blocks, closed shapes, notes, pictures, tables, text and
 * hand-drawn closed loops. Anchors sit on the element's real outline.
 */
object Anchors {

    /** A hand-drawn stroke that closes on itself (e.g. a box drawn without snapping). */
    fun isClosedLoop(s: Stroke): Boolean {
        val n = s.count
        if (n < 8) return false
        val b = s.bounds
        val diag = hypot(b.w, b.h)
        if (diag < 20f) return false
        val gap = hypot(s.pts[0] - s.pts[3 * (n - 1)], s.pts[1] - s.pts[3 * (n - 1) + 1])
        return gap < diag * 0.25f
    }

    fun connectable(e: Element): Boolean = when (e) {
        is Edge -> false
        is Stroke -> isClosedLoop(e)
        is ShapeEl -> e.closed
        else -> true
    }

    fun center(e: Element): Pair<Float, Float> = when (e) {
        is ShapeEl -> if (e.kind == ShapeKind.ELLIPSE) e.geo[0] to e.geo[1] else Box.of(e.geo).let { it.cx to it.cy }
        is Stroke -> Box.of(e.pts, 3).let { it.cx to it.cy }
        else -> e.bounds.let { it.cx to it.cy }
    }

    /** Does the point (x, y) lie on/inside the element (for picking connector ends)? */
    fun contains(e: Element, x: Float, y: Float, tol: Float): Boolean = when (e) {
        is Stroke -> Box.of(e.pts, 3).inset(-tol).contains(x, y) &&
            (Geo.pointInPolygon(x, y, e.xy()) || Geo.distToPolyline(x, y, e.pts, 3, closed = true) <= tol + e.width)
        else -> e.hit(x, y, tol, solid = true)
    }

    /** Point on the element's outline in the direction of (tx, ty) from its centre. */
    fun borderToward(e: Element, tx: Float, ty: Float): Pair<Float, Float> {
        val (cx, cy) = center(e)
        val dx = tx - cx; val dy = ty - cy
        if (abs(dx) < 1e-3f && abs(dy) < 1e-3f) return cx to cy
        return when (e) {
            is Node -> e.borderToward(tx, ty)
            is ShapeEl -> when (e.kind) {
                ShapeKind.ELLIPSE -> {
                    val a = e.geo[4].toDouble()
                    val u = (dx * cos(-a) - dy * sin(-a)).toFloat()
                    val v = (dx * sin(-a) + dy * cos(-a)).toFloat()
                    val t = 1f / sqrt((u / e.geo[2]) * (u / e.geo[2]) + (v / e.geo[3]) * (v / e.geo[3]))
                    (cx + dx * t) to (cy + dy * t)
                }
                else -> rayPolygon(cx, cy, dx, dy, e.geo, 2) ?: rayBox(cx, cy, dx, dy, Box.of(e.geo))
            }
            is Stroke -> rayPolygon(cx, cy, dx, dy, e.pts, 3) ?: rayBox(cx, cy, dx, dy, Box.of(e.pts, 3))
            else -> rayBox(cx, cy, dx, dy, e.bounds)
        }
    }

    private fun rayBox(cx: Float, cy: Float, dx: Float, dy: Float, b: Box): Pair<Float, Float> {
        val hw = b.w / 2; val hh = b.h / 2
        val t = minOf(if (dx != 0f) hw / abs(dx) else Float.MAX_VALUE, if (dy != 0f) hh / abs(dy) else Float.MAX_VALUE)
        return (cx + dx * t) to (cy + dy * t)
    }

    /** Nearest intersection of the ray (c + t·d, t > 0) with a closed polyline. */
    private fun rayPolygon(cx: Float, cy: Float, dx: Float, dy: Float, pts: FloatArray, stride: Int): Pair<Float, Float>? {
        val n = pts.size / stride
        if (n < 3) return null
        var best = Float.MAX_VALUE
        for (i in 0 until n) {
            val j = (i + 1) % n
            val x1 = pts[i * stride]; val y1 = pts[i * stride + 1]
            val x2 = pts[j * stride]; val y2 = pts[j * stride + 1]
            val ex = x2 - x1; val ey = y2 - y1
            val den = dx * ey - dy * ex
            if (abs(den) < 1e-9f) continue
            val t = ((x1 - cx) * ey - (y1 - cy) * ex) / den
            val s = ((x1 - cx) * dy - (y1 - cy) * dx) / den
            if (t > 1e-4f && s in -1e-4f..1.0001f && t < best) best = t
        }
        return if (best == Float.MAX_VALUE) null else (cx + dx * best) to (cy + dy * best)
    }

    /** Connector end points for [a] → [b]. */
    fun ends(a: Element, b: Element): FloatArray {
        val (ax, ay) = center(a); val (bx, by) = center(b)
        val (x0, y0) = borderToward(a, bx, by)
        val (x1, y1) = borderToward(b, ax, ay)
        return floatArrayOf(x0, y0, x1, y1)
    }
}
