package com.inkboard.model

import kotlin.math.max

/** One reversible edit step. */
sealed class Op {
    class Insert(val index: Int, val el: Element) : Op()
    class Delete(val index: Int, val el: Element) : Op()
    class Update(val index: Int, val old: Element, val new: Element) : Op()
}

class Change(val ops: List<Op>) {
    val isEmpty get() = ops.isEmpty()
}

enum class Background { PLAIN, GRID, DOTS, LINES }

/**
 * In-memory document: an ordered element list (z-order = list order) with
 * unlimited undo/redo. All mutations go through [apply] so history is exact.
 */
class Document {
    val elements = ArrayList<Element>()
    val images = HashMap<String, ByteArray>()
    var background = Background.DOTS
    /** Saved camera: world→screen scale and translation. */
    var camScale = 1f
    var camX = 0f
    var camY = 0f

    /** Bumped on every content change; used for render caches and autosave. */
    var version = 0L
        private set

    private var idCounter = 1L
    private val undoStack = ArrayDeque<Change>()
    private val redoStack = ArrayDeque<Change>()
    private var indexCache: HashMap<Long, Int>? = null

    val canUndo get() = undoStack.isNotEmpty()
    val canRedo get() = redoStack.isNotEmpty()

    fun nextId(): Long = idCounter++

    /** Called after loading so new ids never collide. */
    fun syncIds() {
        idCounter = (elements.maxOfOrNull { it.id } ?: 0L) + 1
    }

    fun indexOf(id: Long): Int {
        val cache = indexCache ?: HashMap<Long, Int>(elements.size * 2).also { m ->
            elements.forEachIndexed { i, e -> m[e.id] = i }
            indexCache = m
        }
        return cache[id] ?: -1
    }

    fun byId(id: Long): Element? = indexOf(id).let { if (it >= 0) elements[it] else null }

    // ---------------------------------------------------------------- history

    fun apply(change: Change, record: Boolean = true) {
        if (change.isEmpty) return
        for (op in change.ops) applyOp(op)
        if (record) {
            undoStack.addLast(change)
            redoStack.clear()
        }
        touch()
    }

    fun undo(): Change? {
        val c = undoStack.removeLastOrNull() ?: return null
        for (op in c.ops.asReversed()) applyOp(invert(op))
        redoStack.addLast(c)
        touch()
        return c
    }

    fun redo(): Change? {
        val c = redoStack.removeLastOrNull() ?: return null
        for (op in c.ops) applyOp(op)
        undoStack.addLast(c)
        touch()
        return c
    }

    private fun touch() {
        version++
    }

    private fun invert(op: Op): Op = when (op) {
        is Op.Insert -> Op.Delete(op.index, op.el)
        is Op.Delete -> Op.Insert(op.index, op.el)
        is Op.Update -> Op.Update(op.index, op.new, op.old)
    }

    private fun applyOp(op: Op) {
        when (op) {
            is Op.Insert -> { elements.add(op.index.coerceIn(0, elements.size), op.el); indexCache = null }
            is Op.Delete -> {
                val i = if (op.index in elements.indices && elements[op.index].id == op.el.id) op.index else indexOf(op.el.id)
                if (i >= 0) elements.removeAt(i)
                indexCache = null
            }
            is Op.Update -> {
                val i = if (op.index in elements.indices && elements[op.index].id == op.old.id) op.index else indexOf(op.old.id)
                if (i >= 0) elements[i] = op.new
                indexCache?.set(op.new.id, i)
            }
        }
    }

    // ---------------------------------------------------------- edit helpers

    fun add(vararg els: Element) = add(els.toList())

    fun add(els: List<Element>) {
        var i = elements.size
        apply(Change(els.map { Op.Insert(i++, it) }))
    }

    /** Ids that a delete of [ids] must also remove (edges, mind-map subtrees). */
    fun cascade(ids: Collection<Long>): Set<Long> {
        val out = HashSet(ids)
        for (id in ids) (byId(id) as? Node)?.let { if (it.mindmap) out += descendants(id) }
        for (e in elements) if (e is Edge && (e.from in out || e.to in out)) out += e.id
        return out
    }

    fun remove(ids: Collection<Long>) {
        val all = cascade(ids)
        // Delete from the end so recorded indices stay valid on undo (reverse order).
        val ops = elements.withIndex().filter { it.value.id in all }.sortedByDescending { it.index }
            .map { Op.Delete(it.index, it.value) }
        apply(Change(ops))
    }

    fun replace(updated: List<Element>) {
        val ops = updated.mapNotNull { n ->
            val i = indexOf(n.id)
            if (i < 0) null else Op.Update(i, elements[i], n)
        }
        apply(Change(ops))
    }

    /** Remove [removed] and add [added] in one undo step (e.g. ink → text). */
    fun swap(removed: Collection<Long>, added: List<Element>) {
        val del = elements.withIndex().filter { it.value.id in removed }.sortedByDescending { it.index }
            .map { Op.Delete(it.index, it.value) }
        var i = elements.size - del.size
        val ins = added.map { Op.Insert(i++, it) }
        apply(Change(del + ins))
    }

    fun transform(ids: Collection<Long>, a: Affine) {
        if (a.isIdentity) return
        replace(ids.mapNotNull { byId(it)?.transformed(a) })
    }

    /** Move elements to the top (front=true) or bottom of the z-order. */
    fun reorder(ids: Collection<Long>, front: Boolean) {
        val set = ids.toSet()
        val moving = elements.filter { it.id in set }
        if (moving.isEmpty()) return
        val del = elements.withIndex().filter { it.value.id in set }.sortedByDescending { it.index }
            .map { Op.Delete(it.index, it.value) }
        val base = if (front) elements.size - moving.size else 0
        val ins = moving.mapIndexed { k, e -> Op.Insert(base + k, e) }
        apply(Change(del + ins))
    }

    // --------------------------------------------------------------- queries

    fun edgeEnds(e: Edge): FloatArray? {
        val a = byId(e.from) as? Node ?: return null
        val b = byId(e.to) as? Node ?: return null
        val (x0, y0) = a.borderToward(b.x + b.w / 2, b.y + b.h / 2)
        val (x1, y1) = b.borderToward(a.x + a.w / 2, a.y + a.h / 2)
        return floatArrayOf(x0, y0, x1, y1)
    }

    fun boundsOf(e: Element): Box = when (e) {
        is Edge -> edgeEnds(e)?.let { Box.of(it, 2, e.width * 4 + 12f) } ?: Box.EMPTY
        else -> e.bounds
    }

    fun contentBounds(): Box = elements.fold(Box.EMPTY) { acc, e -> acc.union(boundsOf(e)) }

    fun boundsOf(ids: Collection<Long>): Box = ids.fold(Box.EMPTY) { acc, id -> byId(id)?.let { acc.union(boundsOf(it)) } ?: acc }

    /** Topmost element hit at (x, y). */
    fun hitTop(x: Float, y: Float, tol: Float): Element? {
        for (i in elements.indices.reversed()) {
            val e = elements[i]
            if (e is Edge) {
                val ends = edgeEnds(e) ?: continue
                if (Geo.distToSegment(x, y, ends[0], ends[1], ends[2], ends[3]) <= tol + e.width) return e
            } else if (e.hit(x, y, tol, solid = true)) return e
        }
        return null
    }

    fun children(id: Long): List<Node> = elements.filterIsInstance<Node>().filter { it.parentId == id }

    fun descendants(id: Long): Set<Long> {
        val out = LinkedHashSet<Long>()
        val queue = ArrayDeque(listOf(id))
        val byParent = elements.filterIsInstance<Node>().groupBy { it.parentId }
        while (queue.isNotEmpty()) {
            val p = queue.removeFirst()
            for (c in byParent[p].orEmpty()) if (out.add(c.id)) queue.addLast(c.id)
        }
        return out
    }

    fun rootOf(id: Long): Node? {
        var n = byId(id) as? Node ?: return null
        val seen = HashSet<Long>()
        while (n.parentId != 0L && seen.add(n.id)) n = byId(n.parentId) as? Node ?: break
        return n
    }

    // ------------------------------------------------------------- mind maps

    /** Add a child branch to [parent]; returns it (already committed). */
    fun addMindmapChild(parent: Node, text: String = "Ý mới"): Node {
        val siblings = children(parent.id)
        val size = parent.size * if (parent.parentId == 0L) 0.85f else 1f
        val x = parent.x + parent.w + size * 4f
        val y = if (siblings.isEmpty()) parent.y + parent.h / 2
        else siblings.maxOf { it.y + it.h } + size * 1.2f
        val proto = Node(nextId(), x, y, 10f, 10f, text, NodeKind.ROUND, branchColor(parent), size, parent.id, true)
        val child = proto.fitText(text).let { it.copy(x = x, y = y - if (siblings.isEmpty()) it.h / 2 else 0f) }
        add(child)
        return child
    }

    private fun branchColor(parent: Node): Int {
        if (parent.parentId != 0L) return parent.fill
        val palette = intArrayOf(0xFFFFE0B2.toInt(), 0xFFC8E6C9.toInt(), 0xFFBBDEFB.toInt(), 0xFFF8BBD0.toInt(), 0xFFD1C4E9.toInt(), 0xFFFFF9C4.toInt())
        return palette[children(parent.id).size % palette.size]
    }

    /** Tidy-tree layout of the whole mind map containing [anyId], to the right of the root. */
    fun layoutMindmap(anyId: Long) {
        val root = rootOf(anyId) ?: return
        val byParent = elements.filterIsInstance<Node>().groupBy { it.parentId }
        val updates = HashMap<Long, Node>()
        val gapY = root.size * 1.0f
        val gapX = root.size * 4f

        fun height(n: Node): Float {
            val kids = byParent[n.id].orEmpty()
            if (kids.isEmpty()) return n.h
            val sum = kids.sumOf { height(it).toDouble() }.toFloat() + gapY * (kids.size - 1)
            return max(n.h, sum)
        }

        fun place(n: Node, left: Float, top: Float) {
            val hTotal = height(n)
            val nn = n.copy(x = left, y = top + hTotal / 2 - n.h / 2)
            if (nn.x != n.x || nn.y != n.y) updates[n.id] = nn
            val kids = byParent[n.id].orEmpty().sortedBy { it.y }
            val kidsH = kids.sumOf { height(it).toDouble() }.toFloat() + gapY * max(0, kids.size - 1)
            var y = top + hTotal / 2 - kidsH / 2
            val x = left + n.w + gapX
            for (k in kids) {
                place(k, x, y)
                y += height(k) + gapY
            }
        }

        val total = height(root)
        place(root, root.x, root.y + root.h / 2 - total / 2)
        if (updates.isNotEmpty()) replace(updates.values.toList())
    }

    /** Shallow copy safe to serialize on another thread (elements are immutable). */
    fun snapshot(): Document {
        val d = Document()
        d.elements.addAll(elements)
        d.images.putAll(images)
        d.background = background
        d.camScale = camScale; d.camX = camX; d.camY = camY
        return d
    }

    fun clearHistory() {
        undoStack.clear(); redoStack.clear()
    }
}
