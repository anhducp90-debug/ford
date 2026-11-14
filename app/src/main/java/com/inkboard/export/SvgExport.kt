package com.inkboard.export

import com.inkboard.model.Box
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
import com.inkboard.model.TextFit
import com.inkboard.model.TextMetrics
import java.util.Base64
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

/** Vector SVG export (pure Kotlin, no Android dependencies). */
object SvgExport {

    fun export(doc: Document, region: Box, onlyIds: Set<Long>? = null): String {
        val pad = 24f
        val b = Box(region.l - pad, region.t - pad, region.r + pad, region.b + pad)
        val sb = StringBuilder()
        sb.append("""<?xml version="1.0" encoding="UTF-8"?>""").append('\n')
        sb.append("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="${f(b.l)} ${f(b.t)} ${f(b.w)} ${f(b.h)}" width="${f(b.w)}" height="${f(b.h)}">""").append('\n')
        sb.append("""<rect x="${f(b.l)}" y="${f(b.t)}" width="${f(b.w)}" height="${f(b.h)}" fill="#ffffff"/>""").append('\n')
        val hidden = doc.hiddenIds()
        fun include(e: Element) = e.id !in hidden && (onlyIds == null || e.id in onlyIds)
        // Connectors first (under the boxes)
        for (e in doc.connections()) {
            if (!include(e)) continue
            if (e is Node) {
                val p = doc.byId(e.parentId) as? Node ?: continue
                if (p.id in hidden) continue
                branch(sb, p, e)
            } else if (e is Edge) edge(sb, doc, e)
        }
        for (e in doc.elements) {
            if (!include(e)) continue
            when (e) {
                is Stroke -> stroke(sb, e)
                is ShapeEl -> shape(sb, e)
                is TextEl -> text(sb, e)
                is StickyEl -> sticky(sb, e)
                is Node -> node(sb, e)
                is TableEl -> table(sb, e)
                is ImageEl -> image(sb, doc, e)
                is Edge -> Unit
            }
        }
        sb.append("</svg>\n")
        return sb.toString()
    }

    // ---------------------------------------------------------------- pieces

    private fun f(v: Float): String {
        val r = Math.round(v * 100) / 100.0
        return if (r == Math.floor(r)) r.toLong().toString() else r.toString()
    }

    private fun color(c: Int) = "#%06x".format(c and 0xFFFFFF)
    private fun alpha(c: Int) = ((c ushr 24) and 0xFF) / 255f
    private fun esc(s: String) = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")

    /** Same outline math as the on-screen renderer (pressure + taper), as a polygon. */
    fun outline(s: Stroke): FloatArray {
        val n = s.count
        val pts = s.pts
        if (n == 0) return FloatArray(0)
        val ramp = FloatArray(n) { 1f }
        if (s.taper && n > 2) {
            val dist = FloatArray(n)
            for (i in 1 until n) dist[i] = dist[i - 1] + hypot(pts[3 * i] - pts[3 * i - 3], pts[3 * i + 1] - pts[3 * i - 2])
            val total = dist[n - 1]
            val tl = min(total * 0.2f, s.width * 8f)
            if (tl > 0f) for (i in 0 until n) {
                val e = min(dist[i], total - dist[i]) / tl
                ramp[i] = 0.15f + 0.85f * Math.sqrt(e.coerceIn(0f, 1f).toDouble()).toFloat()
            }
        }
        fun half(i: Int): Float {
            val p = pts[3 * i + 2].coerceIn(0f, 1f)
            val base = if (s.pen == PenType.PENCIL) s.width / 2f * (0.35f + 1.65f * p) else s.width / 2f * (0.3f + 0.7f * p)
            return base * ramp[i]
        }
        val left = ArrayList<Float>(); val right = ArrayList<Float>()
        for (i in 0 until n) {
            val a = max(0, i - 1); val c = min(n - 1, i + 1)
            var dx = pts[3 * c] - pts[3 * a]; var dy = pts[3 * c + 1] - pts[3 * a + 1]
            val len = hypot(dx, dy)
            if (len < 1e-4f) { dx = 1f; dy = 0f } else { dx /= len; dy /= len }
            val h = half(i)
            left += pts[3 * i] - dy * h; left += pts[3 * i + 1] + dx * h
            right += pts[3 * i] + dy * h; right += pts[3 * i + 1] - dx * h
        }
        val out = FloatArray(left.size + right.size)
        for (i in left.indices) out[i] = left[i]
        var k = left.size
        var i = right.size - 2
        while (i >= 0) { out[k++] = right[i]; out[k++] = right[i + 1]; i -= 2 }
        return out
    }

    private fun poly(pts: FloatArray): String {
        val sb = StringBuilder()
        var i = 0
        while (i + 1 < pts.size) { sb.append(if (i == 0) "M" else "L").append(f(pts[i])).append(' ').append(f(pts[i + 1])); i += 2 }
        return sb.append('Z').toString()
    }

    private fun stroke(sb: StringBuilder, s: Stroke) {
        val c = color(s.color)
        if (s.pen == PenType.HIGHLIGHTER) {
            val pl = (0 until s.count).joinToString(" ") { "${f(s.pts[3 * it])},${f(s.pts[3 * it + 1])}" }
            sb.append("""<polyline points="$pl" fill="none" stroke="$c" stroke-opacity="${f(alpha(s.color) * 0.35f)}" stroke-width="${f(s.width)}" stroke-linecap="square" stroke-linejoin="round"/>""").append('\n')
            return
        }
        val op = alpha(s.color) * if (s.pen == PenType.PENCIL) 0.75f else 1f
        val o = outline(s)
        val h0 = s.width * 0.15f
        sb.append("""<path d="${poly(o)}" fill="$c" fill-opacity="${f(op)}"/>""").append('\n')
        if (s.count == 1) sb.append("""<circle cx="${f(s.pts[0])}" cy="${f(s.pts[1])}" r="${f(max(h0, s.width / 2))}" fill="$c"/>""").append('\n')
    }

    private fun arrow(sb: StringBuilder, x0: Float, y0: Float, x1: Float, y1: Float, w: Float, c: Int) {
        val ang = atan2(y1 - y0, x1 - x0)
        val len = max(10f, w * 4.5f)
        val p = floatArrayOf(x1, y1, x1 - len * cos(ang - 0.45f), y1 - len * sin(ang - 0.45f), x1 - len * cos(ang + 0.45f), y1 - len * sin(ang + 0.45f))
        sb.append("""<path d="${poly(p)}" fill="${color(c)}"/>""").append('\n')
    }

    private fun labelLines(sb: StringBuilder, fit: TextFit.Fit, box: Box, c: Int) {
        if (fit.lines.isEmpty()) return
        val lh = TextMetrics.lineHeight(fit.size)
        var y = box.cy - fit.lines.size * lh / 2 + (lh - fit.size) / 2 + fit.size * 0.92f
        for (ln in fit.lines) {
            sb.append("""<text x="${f(box.cx)}" y="${f(y)}" font-size="${f(fit.size)}" font-family="sans-serif" text-anchor="middle" fill="${color(c)}">${esc(ln)}</text>""").append('\n')
            y += lh
        }
    }

    private fun shape(sb: StringBuilder, s: ShapeEl) {
        val g = s.geo
        val st = """stroke="${color(s.color)}" stroke-width="${f(s.width)}" stroke-linecap="round" stroke-linejoin="round""""
        val fill = if (s.fill != 0) """fill="${color(s.fill)}" fill-opacity="${f(alpha(s.fill))}"""" else """fill="none""""
        when (s.kind) {
            ShapeKind.LINE -> sb.append("""<line x1="${f(g[0])}" y1="${f(g[1])}" x2="${f(g[2])}" y2="${f(g[3])}" $st/>""").append('\n')
            ShapeKind.ARROW -> {
                sb.append("""<line x1="${f(g[0])}" y1="${f(g[1])}" x2="${f(g[2])}" y2="${f(g[3])}" $st/>""").append('\n')
                arrow(sb, g[0], g[1], g[2], g[3], s.width, s.color)
            }
            ShapeKind.POLYGON -> sb.append("""<path d="${poly(g)}" $fill $st/>""").append('\n')
            ShapeKind.ELLIPSE -> {
                val deg = Math.toDegrees(g[4].toDouble()).toFloat()
                sb.append("""<ellipse cx="${f(g[0])}" cy="${f(g[1])}" rx="${f(g[2])}" ry="${f(g[3])}" transform="rotate(${f(deg)} ${f(g[0])} ${f(g[1])})" $fill $st/>""").append('\n')
            }
        }
        if (s.closed && s.label.isNotBlank()) labelLines(sb, s.labelFit, s.labelBox, s.labelColor)
    }

    private fun text(sb: StringBuilder, t: TextEl) {
        val lh = TextMetrics.lineHeight(t.size)
        var y = t.y + t.size
        for (ln in t.text.split('\n')) {
            sb.append("""<text x="${f(t.x)}" y="${f(y)}" font-size="${f(t.size)}" font-family="sans-serif" fill="${color(t.color)}">${esc(ln)}</text>""").append('\n')
            y += lh
        }
    }

    private fun sticky(sb: StringBuilder, s: StickyEl) {
        val r = s.w * 0.04f
        sb.append("""<rect x="${f(s.x)}" y="${f(s.y)}" width="${f(s.w)}" height="${f(s.h)}" rx="${f(r)}" fill="${color(s.color)}"/>""").append('\n')
        val pad = s.size * 0.8f
        val lines = TextFit.wrap(s.text, s.size, s.w - 2 * pad)
        var y = s.y + pad + s.size
        for (ln in lines) {
            if (y > s.y + s.h) break
            sb.append("""<text x="${f(s.x + pad)}" y="${f(y)}" font-size="${f(s.size)}" font-family="sans-serif" fill="#333333">${esc(ln)}</text>""").append('\n')
            y += TextMetrics.lineHeight(s.size)
        }
    }

    private fun node(sb: StringBuilder, n: Node) {
        val st = """fill="${color(n.fill)}" stroke="#555555" stroke-width="${f(max(1f, n.size * 0.1f))}""""
        when (n.kind) {
            NodeKind.ROUND -> { val r = min(n.h / 2, n.size); sb.append("""<rect x="${f(n.x)}" y="${f(n.y)}" width="${f(n.w)}" height="${f(n.h)}" rx="${f(r)}" $st/>""") }
            NodeKind.RECT -> sb.append("""<rect x="${f(n.x)}" y="${f(n.y)}" width="${f(n.w)}" height="${f(n.h)}" $st/>""")
            NodeKind.ELLIPSE -> sb.append("""<ellipse cx="${f(n.x + n.w / 2)}" cy="${f(n.y + n.h / 2)}" rx="${f(n.w / 2)}" ry="${f(n.h / 2)}" $st/>""")
            NodeKind.DIAMOND -> sb.append("""<path d="${poly(floatArrayOf(n.x + n.w / 2, n.y, n.x + n.w, n.y + n.h / 2, n.x + n.w / 2, n.y + n.h, n.x, n.y + n.h / 2))}" $st/>""")
        }
        sb.append('\n')
        if (n.text.isNotBlank()) {
            val inset = if (n.kind == NodeKind.DIAMOND || n.kind == NodeKind.ELLIPSE) n.w * 0.15f else n.size * 0.6f
            val lines = TextFit.wrap(n.text, n.size, n.w - 2 * inset)
            labelLines(sb, TextFit.Fit(n.size, lines), Box(n.x, n.y, n.x + n.w, n.y + n.h), 0xFF222222.toInt())
        }
    }

    private fun table(sb: StringBuilder, t: TableEl) {
        val st = """stroke="${color(t.color)}" stroke-width="${f(t.width)}""""
        sb.append("""<rect x="${f(t.x)}" y="${f(t.y)}" width="${f(t.w)}" height="${f(t.h / t.rows)}" fill="${color(t.color)}" fill-opacity="0.08"/>""").append('\n')
        for (i in 0..t.cols) { val x = t.x + t.w * i / t.cols; sb.append("""<line x1="${f(x)}" y1="${f(t.y)}" x2="${f(x)}" y2="${f(t.y + t.h)}" $st/>""").append('\n') }
        for (j in 0..t.rows) { val y = t.y + t.h * j / t.rows; sb.append("""<line x1="${f(t.x)}" y1="${f(y)}" x2="${f(t.x + t.w)}" y2="${f(y)}" $st/>""").append('\n') }
        for ((i, _) in t.cells) labelLines(sb, t.cellFit(i), t.cellBox(i), 0xFF202124.toInt())
    }

    private fun image(sb: StringBuilder, doc: Document, e: ImageEl) {
        val bytes = doc.images[e.key] ?: return
        val mime = if (bytes.size > 3 && bytes[0] == 0x89.toByte() && bytes[1] == 'P'.code.toByte()) "image/png" else "image/jpeg"
        sb.append("""<image x="${f(e.x)}" y="${f(e.y)}" width="${f(e.w)}" height="${f(e.h)}" preserveAspectRatio="none" href="data:$mime;base64,${Base64.getEncoder().encodeToString(bytes)}"/>""").append('\n')
    }

    private fun edge(sb: StringBuilder, doc: Document, e: Edge) {
        val ends = doc.edgeEnds(e) ?: return
        sb.append("""<line x1="${f(ends[0])}" y1="${f(ends[1])}" x2="${f(ends[2])}" y2="${f(ends[3])}" stroke="${color(e.color)}" stroke-width="${f(e.width)}"/>""").append('\n')
        arrow(sb, ends[0], ends[1], ends[2], ends[3], e.width, e.color)
        if (e.label.isNotBlank()) {
            val size = max(10f, e.width * 6f)
            val cx = (ends[0] + ends[2]) / 2; val cy = (ends[1] + ends[3]) / 2
            val w = TextMetrics.width(e.label, size) + size
            sb.append("""<rect x="${f(cx - w / 2)}" y="${f(cy - size * 0.75f)}" width="${f(w)}" height="${f(size * 1.5f)}" rx="${f(size * 0.4f)}" fill="#ffffff"/>""").append('\n')
            sb.append("""<text x="${f(cx)}" y="${f(cy + size * 0.35f)}" font-size="${f(size)}" font-family="sans-serif" text-anchor="middle" fill="${color(e.color)}">${esc(e.label)}</text>""").append('\n')
        }
    }

    private fun branch(sb: StringBuilder, p: Node, n: Node) {
        val right = n.x + n.w / 2 >= p.x + p.w / 2
        val x0 = if (right) p.x + p.w else p.x; val y0 = p.y + p.h / 2
        val x1 = if (right) n.x else n.x + n.w; val y1 = n.y + n.h / 2
        val mx = (x0 + x1) / 2
        sb.append("""<path d="M${f(x0)} ${f(y0)} C${f(mx)} ${f(y0)} ${f(mx)} ${f(y1)} ${f(x1)} ${f(y1)}" fill="none" stroke="#777777" stroke-width="${f(max(1.5f, n.size * 0.14f))}"/>""").append('\n')
    }
}
