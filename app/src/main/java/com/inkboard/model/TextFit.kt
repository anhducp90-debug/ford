package com.inkboard.model

import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

/** Word-wrapping and "largest font that fits" for text placed inside shapes and cells. */
object TextFit {
    data class Fit(val size: Float, val lines: List<String>)

    /** Wrap [text] to [maxW] at font [size]. Explicit newlines are kept; long words are split. */
    fun wrap(text: String, size: Float, maxW: Float): List<String> {
        val out = ArrayList<String>()
        for (para in text.split('\n')) {
            if (para.isBlank()) { out += ""; continue }
            var line = ""
            for (word in para.split(' ').filter { it.isNotEmpty() }) {
                val candidate = if (line.isEmpty()) word else "$line $word"
                if (TextMetrics.measure(candidate, size) <= maxW) { line = candidate; continue }
                if (line.isNotEmpty()) { out += line; line = "" }
                if (TextMetrics.measure(word, size) <= maxW) { line = word; continue }
                // Split an over-long word by characters.
                var chunk = ""
                for (ch in word) {
                    if (chunk.isNotEmpty() && TextMetrics.measure(chunk + ch, size) > maxW) { out += chunk; chunk = "" }
                    chunk += ch
                }
                line = chunk
            }
            out += line
        }
        return out
    }

    /** Largest size (≤ [maxSize]) at which the wrapped text fits inside w × h. */
    fun fit(text: String, w: Float, h: Float, maxSize: Float = Float.MAX_VALUE, minSize: Float = 0.5f): Fit {
        if (text.isBlank() || w <= 0f || h <= 0f) return Fit(minSize, emptyList())
        var lo = minSize
        var hi = min(maxSize, h / TextMetrics.lineHeight(1f))
        if (hi <= lo) return Fit(lo, wrap(text, lo, w))
        fun fits(size: Float): Boolean {
            val lines = wrap(text, size, w)
            if (lines.size * TextMetrics.lineHeight(size) > h) return false
            return lines.all { TextMetrics.measure(it, size) <= w + 0.01f }
        }
        if (fits(hi)) return balanced(text, hi, w)
        repeat(22) {
            val mid = (lo + hi) / 2
            if (fits(mid)) lo = mid else hi = mid
        }
        return balanced(text, lo, w)
    }

    /** Re-wrap at the narrowest width that keeps the same line count (no lonely last word). */
    private fun balanced(text: String, size: Float, w: Float): Fit {
        val base = wrap(text, size, w)
        if (base.size < 2) return Fit(size, base)
        var lo = w / 2; var hi = w
        repeat(14) {
            val mid = (lo + hi) / 2
            if (wrap(text, size, mid).size <= base.size) hi = mid else lo = mid
        }
        return Fit(size, wrap(text, size, hi))
    }

    /** Label size: fill the shape but never taller than ~40% of it, so short labels stay readable. */
    fun fitLabel(text: String, w: Float, h: Float): Fit = fit(text, w, h, maxSize = h / 2.4f)

    /** Inner rectangle of a closed shape where text should go. */
    fun innerBox(s: ShapeEl): Box {
        val g = s.geo
        return when (s.kind) {
            ShapeKind.ELLIPSE -> {
                // Inscribed rectangle of an ellipse: (√2·rx) × (√2·ry); keep a margin.
                val hw = g[2] * 0.62f; val hh = g[3] * 0.62f
                Box(g[0] - hw, g[1] - hh, g[0] + hw, g[1] + hh)
            }
            ShapeKind.POLYGON -> {
                val bb = Box.of(g)
                val n = g.size / 2
                val area = abs(polygonArea(g))
                val ratio = if (bb.w * bb.h > 0) area / (bb.w * bb.h) else 1f
                val pad = min(bb.w, bb.h) * 0.08f
                when {
                    ratio > 0.85f -> bb.inset(pad) // rectangle-like
                    n == 3 -> {
                        // Triangle: rectangle in the wider part, below the centroid.
                        val cx = (g[0] + g[2] + g[4]) / 3; val cy = (g[1] + g[3] + g[5]) / 3
                        val hw = bb.w * 0.26f; val hh = bb.h * 0.2f
                        val nearTop = (0 until 3).count { g[2 * it + 1] < bb.t + bb.h * 0.2f }
                        val wideAtBottom = nearTop == 1
                        val oy = if (wideAtBottom) bb.h * 0.06f else -bb.h * 0.06f
                        Box(cx - hw, cy - hh + oy, cx + hw, cy + hh + oy)
                    }
                    else -> { // diamond and other polygons: centred half-size box
                        val hw = bb.w * 0.27f; val hh = bb.h * 0.27f
                        Box(bb.cx - hw, bb.cy - hh, bb.cx + hw, bb.cy + hh)
                    }
                }
            }
            else -> Box.of(g)
        }
    }

    fun polygonArea(g: FloatArray): Float {
        val n = g.size / 2
        var a = 0f
        for (i in 0 until n) {
            val j = (i + 1) % n
            a += g[2 * i] * g[2 * j + 1] - g[2 * j] * g[2 * i + 1]
        }
        return a / 2
    }
}
