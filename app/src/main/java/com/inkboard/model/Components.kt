package com.inkboard.model

import kotlin.math.floor

enum class ComponentKind { MINDMAP, NODE, TEXT, STICKY, TABLE, IMAGE, SHAPE, SKETCH }

/** One entry of the "Components" navigator: what it is, a label and where it is. */
data class Component(val kind: ComponentKind, val label: String, val box: Box, val count: Int = 1)

/**
 * Builds the components list of a document:
 * - each mind map (root + its branches) as one entry
 * - nodes, text, sticky notes, tables, images, shapes individually
 * - free-hand strokes grouped into spatial clusters ("sketches")
 */
object Components {
    fun collect(doc: Document, clusterGap: Float = 120f): List<Component> {
        val out = ArrayList<Component>()
        val inMindmap = HashSet<Long>()
        for (e in doc.elements) {
            if (e is Node && e.mindmap && e.parentId == 0L) {
                val ids = doc.descendants(e.id) + e.id
                inMindmap += ids
                out += Component(ComponentKind.MINDMAP, e.text.ifBlank { "Mindmap" }.short(), doc.boundsOf(ids), ids.size)
            }
        }
        val strokes = ArrayList<Stroke>()
        for (e in doc.elements) {
            when (e) {
                is Node -> if (e.id !in inMindmap) out += Component(ComponentKind.NODE, e.text.ifBlank { "Khối" }.short(), e.bounds)
                is TextEl -> out += Component(ComponentKind.TEXT, e.text.short(), e.bounds)
                is StickyEl -> out += Component(ComponentKind.STICKY, e.text.ifBlank { "Ghi chú dán" }.short(), e.bounds)
                is TableEl -> out += Component(ComponentKind.TABLE, "Bảng ${e.rows}×${e.cols}", e.bounds)
                is ImageEl -> out += Component(ComponentKind.IMAGE, "Ảnh", e.bounds)
                is ShapeEl -> out += Component(ComponentKind.SHAPE, shapeName(e), e.bounds)
                is Stroke -> strokes += e
                else -> Unit
            }
        }
        for ((box, n) in clusterStrokes(strokes, clusterGap)) {
            out += Component(ComponentKind.SKETCH, "Nét vẽ ($n nét)", box, n)
        }
        return out
    }

    private fun shapeName(s: ShapeEl) = when (s.kind) {
        ShapeKind.LINE -> "Đường thẳng"
        ShapeKind.ARROW -> "Mũi tên"
        ShapeKind.ELLIPSE -> if (s.geo[2] == s.geo[3]) "Hình tròn" else "Hình elip"
        ShapeKind.POLYGON -> when (s.geo.size / 2) {
            3 -> "Tam giác"
            4 -> "Tứ giác"
            else -> "Đa giác"
        }
    }

    private fun String.short(max: Int = 40): String {
        val one = replace('\n', ' ').trim()
        return if (one.length <= max) one else one.take(max - 1) + "…"
    }

    /**
     * Groups strokes whose (padded) bounds touch. Uses a uniform grid so it
     * stays near-linear for large drawings. Returns cluster bounds and sizes,
     * ordered top-to-bottom, left-to-right.
     */
    fun clusterStrokes(strokes: List<Stroke>, gap: Float): List<Pair<Box, Int>> {
        if (strokes.isEmpty()) return emptyList()
        val n = strokes.size
        val parent = IntArray(n) { it }
        fun find(i: Int): Int {
            var x = i
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x] }
            return x
        }
        fun union(a: Int, b: Int) { val ra = find(a); val rb = find(b); if (ra != rb) parent[ra] = rb }

        val cell = gap * 4
        val grid = HashMap<Long, MutableList<Int>>()
        val boxes = strokes.map { it.bounds.inset(-gap / 2) }
        for ((i, b) in boxes.withIndex()) {
            val x0 = floor(b.l / cell).toInt(); val x1 = floor(b.r / cell).toInt()
            val y0 = floor(b.t / cell).toInt(); val y1 = floor(b.b / cell).toInt()
            // Very large strokes: cap the cells visited.
            if ((x1 - x0 + 1).toLong() * (y1 - y0 + 1) > 4096) {
                grid.getOrPut(Long.MIN_VALUE) { mutableListOf() } += i
                continue
            }
            for (gx in x0..x1) for (gy in y0..y1) {
                val key = (gx.toLong() shl 32) or (gy.toLong() and 0xffffffffL)
                val list = grid.getOrPut(key) { mutableListOf() }
                for (j in list) if (boxes[j].intersects(b)) union(i, j)
                list += i
            }
        }
        grid[Long.MIN_VALUE]?.let { big -> for (i in big) for (j in 0 until n) if (i != j && boxes[i].intersects(boxes[j])) union(i, j) }

        val groups = HashMap<Int, MutableList<Int>>()
        for (i in 0 until n) groups.getOrPut(find(i)) { mutableListOf() } += i
        return groups.values.map { idx ->
            idx.fold(Box.EMPTY) { acc, i -> acc.union(strokes[i].bounds) } to idx.size
        }.sortedWith(compareBy({ Math.round(it.first.t / 200f) }, { it.first.l }))
    }
}
