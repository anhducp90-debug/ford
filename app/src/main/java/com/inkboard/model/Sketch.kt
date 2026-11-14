package com.inkboard.model

import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.pow
import kotlin.math.sin

enum class SymmetryMode { OFF, VERTICAL, HORIZONTAL, QUAD, RADIAL }

/** Drawing aids saved with a document. Coordinates are world units. */
data class Guides(
    val symmetry: SymmetryMode = SymmetryMode.OFF,
    val radialN: Int = 6,
    val symX: Float = 0f,
    val symY: Float = 0f,
    /** 0 = off, 1 = one-point, 2 = two-point perspective. */
    val perspective: Int = 0,
    val vp1x: Float = 0f,
    val vp1y: Float = 0f,
    val vp2x: Float = 0f,
    val vp2y: Float = 0f,
    /** Perspective drawing assist: strokes snap to guide lines. */
    val assist: Boolean = true,
)

/** Pressure response curves (p' = p^gamma). */
enum class PressureCurve(val gamma: Float, val label: String) {
    SOFT(0.55f, "Nhẹ tay"), NORMAL(1f, "Vừa"), FIRM(1.7f, "Mạnh tay");

    fun map(p: Float): Float = p.coerceIn(0f, 1f).pow(gamma)
}

/**
 * "Lazy string" stabilizer: the ink point trails the pen by up to [radius]
 * world units, which removes hand jitter while keeping corners crisp.
 */
class Stabilizer(var radius: Float) {
    private var x = Float.NaN
    private var y = Float.NaN

    fun reset(px: Float, py: Float) { x = px; y = py }

    /** Feed a raw pen point; returns the stabilized point, or null when it did not move. */
    fun feed(px: Float, py: Float): Pair<Float, Float>? {
        if (x.isNaN() || radius <= 0f) { x = px; y = py; return x to y }
        val d = hypot(px - x, py - y)
        if (d <= radius) return null
        val k = (d - radius) / d
        x += (px - x) * k
        y += (py - y) * k
        return x to y
    }
}

object Symmetry {
    /**
     * Mirrored/rotated copies of a stroke (x, y, p triples), excluding the
     * original. Each copy is a transform of the input around (symX, symY).
     */
    fun copies(pts: FloatArray, g: Guides): List<FloatArray> {
        val cx = g.symX; val cy = g.symY
        fun map(f: (Float, Float) -> Pair<Float, Float>): FloatArray {
            val out = pts.copyOf()
            var i = 0
            while (i + 2 < out.size) {
                val (nx, ny) = f(pts[i], pts[i + 1]); out[i] = nx; out[i + 1] = ny; i += 3
            }
            return out
        }
        val mx = { x: Float, y: Float -> (2 * cx - x) to y }
        val my = { x: Float, y: Float -> x to (2 * cy - y) }
        return when (g.symmetry) {
            SymmetryMode.OFF -> emptyList()
            SymmetryMode.VERTICAL -> listOf(map(mx))
            SymmetryMode.HORIZONTAL -> listOf(map(my))
            SymmetryMode.QUAD -> listOf(map(mx), map(my), map { x, y -> (2 * cx - x) to (2 * cy - y) })
            SymmetryMode.RADIAL -> {
                val n = g.radialN.coerceIn(2, 24)
                (1 until n).map { k ->
                    val a = 2 * Math.PI * k / n
                    val c = cos(a).toFloat(); val s = sin(a).toFloat()
                    map { x, y -> val dx = x - cx; val dy = y - cy; (cx + dx * c - dy * s) to (cy + dx * s + dy * c) }
                }
            }
        }
    }
}

object Perspective {
    /**
     * Snap a stroke (start → end) to the closest guide direction: toward a
     * vanishing point, horizontal, or vertical. Returns x0, y0, x1, y1.
     */
    fun snap(x0: Float, y0: Float, x1: Float, y1: Float, g: Guides): FloatArray {
        val len = hypot(x1 - x0, y1 - y0)
        if (len < 1e-3f || g.perspective == 0) return floatArrayOf(x0, y0, x1, y1)
        val dirs = ArrayList<Pair<Float, Float>>()
        dirs += 1f to 0f
        dirs += 0f to 1f
        fun toward(vx: Float, vy: Float) {
            val dx = vx - x0; val dy = vy - y0; val l = hypot(dx, dy)
            if (l > 1e-3f) dirs += (dx / l) to (dy / l)
        }
        toward(g.vp1x, g.vp1y)
        if (g.perspective >= 2) toward(g.vp2x, g.vp2y)
        val ux = (x1 - x0) / len; val uy = (y1 - y0) / len
        // Pick the guide most parallel to the drawn direction (either sign).
        val best = dirs.maxBy { (dx, dy) -> abs(dx * ux + dy * uy) }
        val sign = if (best.first * ux + best.second * uy >= 0) 1f else -1f
        val proj = (x1 - x0) * best.first + (y1 - y0) * best.second
        val l = abs(proj)
        return floatArrayOf(x0, y0, x0 + best.first * l * sign, y0 + best.second * l * sign)
    }

    fun angleDeg(x0: Float, y0: Float, x1: Float, y1: Float) = Math.toDegrees(atan2((y1 - y0).toDouble(), (x1 - x0).toDouble()))
}
