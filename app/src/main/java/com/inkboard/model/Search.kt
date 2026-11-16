package com.inkboard.model

/**
 * Text search over everything readable on a board: typed text, shape and
 * connector labels, blocks, notes, table cells, and handwriting that the
 * background indexer has recognised. Accent-insensitive.
 */
object Search {
    enum class Kind { TEXT, LABEL, BLOCK, NOTE, CELL, CONNECTOR, INK }

    data class Hit(val kind: Kind, val text: String, val box: Box, val ids: List<Long>)

    /** Stable key of a cluster of strokes (order-independent). */
    fun clusterKey(ids: Collection<Long>): Long {
        var h = 1125899906842597L
        for (id in ids.sorted()) h = 31 * h + id
        return h
    }

    /** Clusters of strokes the ink indexer recognises as text (one entry per word/line group). */
    fun inkClusters(doc: Document, gap: Float = 60f): List<Pair<Long, List<Stroke>>> {
        val strokes = doc.elements.filterIsInstance<Stroke>().filter { it.pen != PenType.HIGHLIGHTER && it.id !in doc.hiddenIds() }
        if (strokes.isEmpty()) return emptyList()
        // Reuse the spatial clustering of the Components panel, then rebuild member lists.
        val n = strokes.size
        val parent = IntArray(n) { it }
        fun find(i: Int): Int { var x = i; while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x] }; return x }
        val cell = gap * 4
        val grid = HashMap<Long, MutableList<Int>>()
        val boxes = strokes.map { it.bounds.inset(-gap / 2) }
        for ((i, b) in boxes.withIndex()) {
            val x0 = Math.floor((b.l / cell).toDouble()).toInt(); val x1 = Math.floor((b.r / cell).toDouble()).toInt()
            val y0 = Math.floor((b.t / cell).toDouble()).toInt(); val y1 = Math.floor((b.b / cell).toDouble()).toInt()
            if ((x1 - x0 + 1).toLong() * (y1 - y0 + 1) > 64) continue // huge strokes are drawings, not words
            for (gx in x0..x1) for (gy in y0..y1) {
                val key = (gx.toLong() shl 32) or (gy.toLong() and 0xffffffffL)
                val list = grid.getOrPut(key) { mutableListOf() }
                for (j in list) if (boxes[j].intersects(b)) { val a = find(i); val c = find(j); if (a != c) parent[a] = c }
                list += i
            }
        }
        val groups = HashMap<Int, MutableList<Stroke>>()
        for (i in 0 until n) groups.getOrPut(find(i)) { mutableListOf() } += strokes[i]
        return groups.values.filter { it.size <= 600 }.map { g -> clusterKey(g.map { it.id }) to g }
    }

    fun collect(doc: Document): List<Hit> {
        val out = ArrayList<Hit>()
        val hidden = doc.hiddenIds()
        for (e in doc.elements) {
            if (e.id in hidden) continue
            when (e) {
                is TextEl -> out += Hit(Kind.TEXT, e.text, e.bounds, listOf(e.id))
                is ShapeEl -> if (e.label.isNotBlank()) out += Hit(Kind.LABEL, e.label, e.bounds, listOf(e.id))
                is Node -> if (e.text.isNotBlank()) out += Hit(Kind.BLOCK, e.text, e.bounds, listOf(e.id))
                is StickyEl -> if (e.text.isNotBlank()) out += Hit(Kind.NOTE, e.text, e.bounds, listOf(e.id))
                is TableEl -> for ((i, t) in e.cells) if (t.isNotBlank()) out += Hit(Kind.CELL, t, e.cellBox(i), listOf(e.id))
                is Edge -> if (e.label.isNotBlank()) doc.boundsOf(e).let { if (!it.isEmpty) out += Hit(Kind.CONNECTOR, e.label, it, listOf(e.id)) }
                else -> Unit
            }
        }
        if (doc.inkText.isNotEmpty()) for ((key, strokes) in inkClusters(doc)) {
            val t = doc.inkText[key] ?: continue
            if (t.isBlank()) continue
            out += Hit(Kind.INK, t, strokes.fold(Box.EMPTY) { a, s -> a.union(s.bounds) }, strokes.map { it.id })
        }
        return out
    }

    fun find(doc: Document, query: String): List<Hit> {
        val q = ShapeLibrary.fold(query.trim())
        if (q.isEmpty()) return emptyList()
        return collect(doc).filter { ShapeLibrary.fold(it.text).contains(q) }
            .sortedWith(compareBy({ it.box.t }, { it.box.l }))
    }

    /** All searchable text of a document, one line per item (for the home screen). */
    fun plainText(doc: Document): String = collect(doc).joinToString("\n") { it.text.replace('\n', ' ') }
}
