package com.inkboard.model

import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min

/** Axis-aligned box in world coordinates (pure Kotlin so it is unit-testable). */
data class Box(val l: Float, val t: Float, val r: Float, val b: Float) {
    val w get() = r - l
    val h get() = b - t
    val cx get() = (l + r) / 2f
    val cy get() = (t + b) / 2f
    val isEmpty get() = r < l || b < t

    fun intersects(o: Box) = !isEmpty && !o.isEmpty && l <= o.r && r >= o.l && t <= o.b && b >= o.t
    fun contains(x: Float, y: Float) = x in l..r && y in t..b
    fun inset(d: Float) = Box(l + d, t + d, r - d, b - d)
    fun union(o: Box): Box = when {
        isEmpty -> o
        o.isEmpty -> this
        else -> Box(min(l, o.l), min(t, o.t), max(r, o.r), max(b, o.b))
    }
    fun map(a: Affine) = Box(a.x(l), a.y(t), a.x(r), a.y(b))

    companion object {
        val EMPTY = Box(1f, 1f, 0f, 0f)
        fun of(xs: FloatArray, stride: Int = 2, pad: Float = 0f): Box {
            if (xs.size < 2) return EMPTY
            var l = Float.MAX_VALUE; var t = Float.MAX_VALUE
            var r = -Float.MAX_VALUE; var b = -Float.MAX_VALUE
            var i = 0
            while (i + 1 < xs.size) {
                val x = xs[i]; val y = xs[i + 1]
                if (x < l) l = x; if (x > r) r = x
                if (y < t) t = y; if (y > b) b = y
                i += stride
            }
            return Box(l - pad, t - pad, r + pad, b + pad)
        }
    }
}

/** Uniform scale + translate: p' = p * s + t. Rotation is not needed for v0.1. */
data class Affine(val s: Float = 1f, val tx: Float = 0f, val ty: Float = 0f) {
    fun x(v: Float) = v * s + tx
    fun y(v: Float) = v * s + ty
    fun then(o: Affine) = Affine(s * o.s, tx * o.s + o.tx, ty * o.s + o.ty)
    val isIdentity get() = s == 1f && tx == 0f && ty == 0f

    companion object {
        val IDENTITY = Affine()
        fun translate(dx: Float, dy: Float) = Affine(1f, dx, dy)
        /** Scale by [s] around the fixed point (fx, fy). */
        fun scaleAround(s: Float, fx: Float, fy: Float) = Affine(s, fx - fx * s, fy - fy * s)
    }
}

object Geo {
    fun distToSegment(px: Float, py: Float, ax: Float, ay: Float, bx: Float, by: Float): Float {
        val dx = bx - ax; val dy = by - ay
        val l2 = dx * dx + dy * dy
        if (l2 <= 1e-9f) return hypot(px - ax, py - ay)
        val t = (((px - ax) * dx + (py - ay) * dy) / l2).coerceIn(0f, 1f)
        return hypot(px - (ax + t * dx), py - (ay + t * dy))
    }

    /** Even-odd point-in-polygon. [poly] is x,y pairs. */
    fun pointInPolygon(x: Float, y: Float, poly: FloatArray): Boolean {
        val n = poly.size / 2
        if (n < 3) return false
        var inside = false
        var j = n - 1
        for (i in 0 until n) {
            val xi = poly[2 * i]; val yi = poly[2 * i + 1]
            val xj = poly[2 * j]; val yj = poly[2 * j + 1]
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside
            j = i
        }
        return inside
    }

    /** Distance from a point to a polyline given as x,y pairs. */
    fun distToPolyline(px: Float, py: Float, pts: FloatArray, stride: Int = 2, closed: Boolean = false): Float {
        val n = pts.size / stride
        if (n == 0) return Float.MAX_VALUE
        if (n == 1) return hypot(px - pts[0], py - pts[1])
        var best = Float.MAX_VALUE
        for (i in 0 until n - 1) {
            val a = i * stride; val b = (i + 1) * stride
            best = min(best, distToSegment(px, py, pts[a], pts[a + 1], pts[b], pts[b + 1]))
        }
        if (closed) {
            val a = (n - 1) * stride
            best = min(best, distToSegment(px, py, pts[a], pts[a + 1], pts[0], pts[1]))
        }
        return best
    }

    /** Snap an angle (radians) to the nearest multiple of [step] if within [tol]. */
    fun snapAngle(angle: Double, step: Double, tol: Double = step / 2): Double {
        val q = Math.round(angle / step) * step
        return if (abs(angle - q) <= tol) q else angle
    }
}
