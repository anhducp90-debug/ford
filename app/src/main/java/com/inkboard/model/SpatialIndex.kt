package com.inkboard.model

import kotlin.math.floor

/**
 * Uniform-grid spatial index over element bounds (world units). Keeps
 * hit tests, erasing, lasso and culling near O(local) instead of O(all)
 * for drawings with 100k+ elements. Very large elements live in a side list.
 */
class SpatialIndex(private val cell: Float = 512f) {
    private val grid = HashMap<Long, MutableList<Long>>()
    private val big = LinkedHashSet<Long>()
    private val boxes = HashMap<Long, Box>()

    val size get() = boxes.size

    private fun key(gx: Int, gy: Int) = (gx.toLong() shl 32) or (gy.toLong() and 0xffffffffL)

    private inline fun cells(b: Box, f: (Long) -> Unit): Boolean {
        val x0 = floor(b.l / cell).toInt(); val x1 = floor(b.r / cell).toInt()
        val y0 = floor(b.t / cell).toInt(); val y1 = floor(b.b / cell).toInt()
        if ((x1 - x0 + 1).toLong() * (y1 - y0 + 1) > MAX_CELLS) return false
        for (gx in x0..x1) for (gy in y0..y1) f(key(gx, gy))
        return true
    }

    fun insert(id: Long, b: Box) {
        if (b.isEmpty) return
        remove(id)
        boxes[id] = b
        if (!cells(b) { grid.getOrPut(it) { ArrayList(4) } += id }) big += id
    }

    fun remove(id: Long) {
        val b = boxes.remove(id) ?: return
        if (big.remove(id)) return
        cells(b) { k -> grid[k]?.let { l -> l.remove(id); if (l.isEmpty()) grid.remove(k) } }
    }

    /** Ids whose bounds intersect [q] (exact box test, no duplicates). */
    fun query(q: Box): Set<Long> {
        val out = LinkedHashSet<Long>()
        if (q.isEmpty) return out
        val x0 = floor(q.l / cell).toInt(); val x1 = floor(q.r / cell).toInt()
        val y0 = floor(q.t / cell).toInt(); val y1 = floor(q.b / cell).toInt()
        if ((x1 - x0 + 1).toLong() * (y1 - y0 + 1) > grid.size.toLong() * 2 + 16) {
            // Query larger than the populated grid: scan boxes directly.
            for ((id, b) in boxes) if (b.intersects(q)) out += id
            return out
        }
        for (gx in x0..x1) for (gy in y0..y1) {
            grid[key(gx, gy)]?.forEach { id -> if (boxes[id]?.intersects(q) == true) out += id }
        }
        for (id in big) if (boxes[id]?.intersects(q) == true) out += id
        return out
    }

    fun clear() { grid.clear(); big.clear(); boxes.clear() }

    companion object { const val MAX_CELLS = 256L }
}
