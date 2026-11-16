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

    /** Box used for side ports (the outline, without stroke padding). */
    private fun portBox(e: Element): Box = when (e) {
        is ShapeEl -> if (e.kind == ShapeKind.ELLIPSE) Box(e.geo[0] - e.geo[2], e.geo[1] - e.geo[3], e.geo[0] + e.geo[2], e.geo[1] + e.geo[3]) else Box.of(e.geo)
        is Stroke -> Box.of(e.pts, 3)
        else -> e.bounds
    }

    /**
     * Connector path as x,y pairs. Blocks use fixed side ports (middle of the
     * facing side); elbow connectors route at right angles. Otherwise a straight
     * line between the outlines.
     */
    fun route(a: Element, b: Element, elbow: Boolean): FloatArray {
        val ports = elbow || a is Node || b is Node
        if (!ports) return ends(a, b)
        val ba = portBox(a); val bb = portBox(b)
        val dx = bb.cx - ba.cx; val dy = bb.cy - ba.cy
        val vertical = abs(dy) >= abs(dx)
        val p: FloatArray = if (vertical) {
            val ay = if (dy > 0) ba.b else ba.t; val by = if (dy > 0) bb.t else bb.b
            floatArrayOf(ba.cx, ay, bb.cx, by)
        } else {
            val ax = if (dx > 0) ba.r else ba.l; val bx = if (dx > 0) bb.l else bb.r
            floatArrayOf(ax, ba.cy, bx, bb.cy)
        }
        if (!elbow || abs(p[0] - p[2]) < 0.5f || abs(p[1] - p[3]) < 0.5f) return p
        return if (vertical) {
            val my = (p[1] + p[3]) / 2
            floatArrayOf(p[0], p[1], p[0], my, p[2], my, p[2], p[3])
        } else {
            val mx = (p[0] + p[2]) / 2
            floatArrayOf(p[0], p[1], mx, p[1], mx, p[3], p[2], p[3])
        }
    }

    /** Point halfway along a polyline (for labels). */
    fun midpoint(path: FloatArray): Pair<Float, Float> {
        var total = 0f
        for (i in 0 until path.size / 2 - 1) total += hypot(path[2 * i + 2] - path[2 * i], path[2 * i + 3] - path[2 * i + 1])
        var acc = 0f
        for (i in 0 until path.size / 2 - 1) {
            val l = hypot(path[2 * i + 2] - path[2 * i], path[2 * i + 3] - path[2 * i + 1])
            if (acc + l >= total / 2 && l > 0f) {
                val t = (total / 2 - acc) / l
                return (path[2 * i] + (path[2 * i + 2] - path[2 * i]) * t) to (path[2 * i + 1] + (path[2 * i + 3] - path[2 * i + 1]) * t)
            }
            acc += l
        }
        return path[0] to path[1]
    }
}
