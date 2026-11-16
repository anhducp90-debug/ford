package com.inkboard.model

import kotlin.math.PI
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.sin

/**
 * Partial erasing. Ink strokes and drawn shapes (circles, rectangles,
 * polygons, lines) are turned into dense point runs that the eraser cuts;
 * whatever survives becomes ordinary ink with the same colour and width.
 */
object Erase {

    /** Erasable by the partial eraser (cut, not deleted whole). */
    fun cuttable(e: Element) = e is Stroke || e is ShapeEl

    /** Insert points so no two consecutive points are farther than [step]. Triples (x, y, p). */
    fun densify(pts: FloatArray, step: Float): FloatArray {
        val n = pts.size / 3
        if (n < 2 || step <= 0f) return pts
        val out = ArrayList<Float>(pts.size)
        for (i in 0 until n - 1) {
            val x0 = pts[3 * i]; val y0 = pts[3 * i + 1]; val p0 = pts[3 * i + 2]
            val x1 = pts[3 * i + 3]; val y1 = pts[3 * i + 4]; val p1 = pts[3 * i + 5]
            val k = max(1, ceil(hypot(x1 - x0, y1 - y0) / step).toInt())
            for (j in 0 until k) {
                val t = j.toFloat() / k
                out += x0 + (x1 - x0) * t; out += y0 + (y1 - y0) * t; out += p0 + (p1 - p0) * t
            }
        }
        out += pts[3 * (n - 1)]; out += pts[3 * (n - 1) + 1]; out += pts[3 * (n - 1) + 2]
        return out.toFloatArray()
    }

    /**
     * Dense outline of an element as (x, y, p) triples, ready to cut. Closed
     * outlines start next to (sx, sy) so the first cut never splits the loop
     * into two arcs that should be one.
     */
    fun outline(e: Element, step: Float, sx: Float, sy: Float): FloatArray? = when (e) {
        is Stroke -> densify(e.pts, step)
        is ShapeEl -> when (e.kind) {
            ShapeKind.LINE, ShapeKind.ARROW -> densify(floatArrayOf(e.geo[0], e.geo[1], 1f, e.geo[2], e.geo[3], 1f), step)
            ShapeKind.POLYGON -> {
                val n = e.geo.size / 2
                val loop = FloatArray((n + 1) * 3) { k -> val i = (k / 3) % n; when (k % 3) { 0 -> e.geo[2 * i]; 1 -> e.geo[2 * i + 1]; else -> 1f } }
                rotateClosed(densify(loop, step), sx, sy)
            }
            ShapeKind.ELLIPSE -> {
                val (cx, cy, rx, ry) = listOf(e.geo[0], e.geo[1], e.geo[2], e.geo[3])
                val a = e.geo[4].toDouble()
                val per = (2 * PI * max(rx, ry)).toFloat()
                val n = max(24, ceil(per / step).toInt())
                val pts = FloatArray((n + 1) * 3) { k ->
                    val t = 2 * PI * ((k / 3) % n) / n
                    val ux = rx * cos(t); val uy = ry * sin(t)
                    when (k % 3) {
                        0 -> (cx + ux * cos(a) - uy * sin(a)).toFloat()
                        1 -> (cy + ux * sin(a) + uy * cos(a)).toFloat()
                        else -> 1f
                    }
                }
                rotateClosed(pts, sx, sy)
            }
        }
        else -> null
    }

    /** Re-start a closed loop (last point == first) at the point nearest (x, y). */
    private fun rotateClosed(loop: FloatArray, x: Float, y: Float): FloatArray {
        val n = loop.size / 3 - 1 // last repeats the first
        if (n < 3) return loop
        var best = 0; var bd = Float.MAX_VALUE
        for (i in 0 until n) { val d = hypot(loop[3 * i] - x, loop[3 * i + 1] - y); if (d < bd) { bd = d; best = i } }
        return FloatArray((n + 1) * 3) { k -> loop[3 * ((k / 3 + best) % n) + k % 3] }
    }

    /**
     * Remove points within [rr] of (x, y) from every run. Returns the new runs
     * (runs shorter than two points disappear), or null if nothing was touched.
     */
    fun split(runs: List<FloatArray>, x: Float, y: Float, rr: Float): List<FloatArray>? {
        var touched = false
        val out = ArrayList<FloatArray>()
        for (p in runs) {
            var run = ArrayList<Float>()
            val n = p.size / 3
            for (i in 0 until n) {
                if (hypot(p[3 * i] - x, p[3 * i + 1] - y) <= rr) {
                    touched = true
                    if (run.size >= 6) out += run.toFloatArray()
                    run = ArrayList()
                } else { run += p[3 * i]; run += p[3 * i + 1]; run += p[3 * i + 2] }
            }
            if (run.size >= 6) out += run.toFloatArray()
        }
        return if (touched) out else null
    }

    /** Style of the ink that replaces a cut element. */
    fun style(e: Element): Triple<Int, Float, PenType> = when (e) {
        is Stroke -> Triple(e.color, e.width, e.pen)
        is ShapeEl -> Triple(e.color, e.width, PenType.PEN)
        else -> Triple(0, 1f, PenType.PEN)
    }
}
