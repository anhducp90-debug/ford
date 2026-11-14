package com.inkboard.model

/**
 * What happens when a selection is dropped somewhere. One table of rules
 * shared by pen and finger drags, previewed while dragging and applied as a
 * single undo step.
 *
 * | Dragged                 | Dropped on            | Result                                   |
 * |-------------------------|-----------------------|------------------------------------------|
 * | Text                    | closed shape          | becomes the shape's label, auto-fitted   |
 * | Text                    | flowchart block       | becomes / extends the block's text       |
 * | Text                    | sticky note           | appended to the note                     |
 * | Text                    | table                 | goes into the cell under the pointer     |
 * | Text / sticky / block   | mind-map node         | becomes a child branch                   |
 * | Mind-map branch         | another mind-map node | moves there (re-parent + re-layout)      |
 * | Anything else           | shape / block / note / table | attached: moves, scales, deletes with it |
 * | Attached item           | outside its container | detached                                 |
 */
object DropRules {

    sealed class Action(val targetId: Long, val hint: String) {
        class LabelShape(val textId: Long, target: Long) : Action(target, "Thả để đặt chữ vào hình")
        class LabelNode(val textId: Long, target: Long) : Action(target, "Thả để đặt chữ vào khối")
        class AppendSticky(val textId: Long, target: Long) : Action(target, "Thả để thêm vào ghi chú")
        class TableCell(val textId: Long, target: Long, val cell: Int) : Action(target, "Thả vào ô bảng")
        class MindmapChild(val itemId: Long, target: Long) : Action(target, "Thả để thành nhánh con")
        class Reparent(val nodeId: Long, target: Long) : Action(target, "Thả để chuyển nhánh tới đây")
        class Attach(val ids: List<Long>, target: Long, name: String) : Action(target, "Thả để gắn vào $name")
        class Detach(val ids: List<Long>) : Action(0L, "Thả để tách khỏi khung")
    }

    private val PLACEHOLDERS = setOf("Bước", "Điều kiện?", "Bắt đầu", "Ý mới", "Khối", "")

    private fun isContainer(e: Element) = when (e) {
        is ShapeEl -> e.closed
        is Node, is StickyEl, is TableEl -> true
        else -> false
    }

    private fun containerName(e: Element) = when (e) {
        is ShapeEl -> "hình"
        is Node -> "khối"
        is StickyEl -> "ghi chú"
        is TableEl -> "bảng"
        else -> "khung"
    }

    /**
     * Decide the action for dropping [moving] (with [primary] being the item
     * the user grabbed) at world point (x, y). Positions are pre-drop; the
     * pointer location decides the target.
     */
    fun resolve(doc: Document, moving: Set<Long>, primary: Long, x: Float, y: Float): Action? {
        val items = moving.mapNotNull { doc.byId(it) }.filter { it !is Edge }
        if (items.isEmpty()) return null
        val target = findTarget(doc, moving, x, y)
        val prim = doc.byId(primary)

        val singleText = items.singleOrNull() as? TextEl
        val singleSticky = items.singleOrNull() as? StickyEl
        val primNode = prim as? Node
        val isSubtree = primNode != null && items.all { it.id == primNode.id || it.id in (if (primNode.mindmap) doc.descendants(primNode.id) else emptySet()) || doc.containerOf(it.id) != 0L && doc.containerOf(it.id) in moving }

        if (target == null) {
            // Dragged out of a container that is not moving with it → detach.
            val out = items.filter { e ->
                val c = doc.containerOf(e.id)
                c != 0L && c !in moving && doc.byId(c)?.let { !doc.boundsOf(it).contains(x, y) } == true
            }.map { it.id }
            return if (out.isNotEmpty()) Action.Detach(out) else null
        }

        if (target is Node && target.mindmap) {
            if (primNode != null && isSubtree && primNode.id != target.id && target.id !in doc.descendants(primNode.id)) {
                if (primNode.parentId == target.id) return null
                return Action.Reparent(primNode.id, target.id)
            }
            if (singleText != null) return Action.MindmapChild(singleText.id, target.id)
            if (singleSticky != null) return Action.MindmapChild(singleSticky.id, target.id)
        }
        if (singleText != null) {
            when (target) {
                is ShapeEl -> return Action.LabelShape(singleText.id, target.id)
                is Node -> return Action.LabelNode(singleText.id, target.id)
                is StickyEl -> return Action.AppendSticky(singleText.id, target.id)
                is TableEl -> return Action.TableCell(singleText.id, target.id, target.cellAt(x, y))
                else -> Unit
            }
        }
        // Generic attach (top-level items only: those not attached to another moving item).
        val top = items.filter { e -> doc.containerOf(e.id).let { it == 0L || it !in moving } }
            .filter { doc.containerOf(it.id) != target.id }
            .filter { !(it is Node && it.mindmap && it.parentId != 0L && it.parentId in moving) }
            .map { it.id }
        if (top.isEmpty()) return null
        return Action.Attach(top, target.id, containerName(target))
    }

    /** Topmost container under (x, y) that is not part of the moving set (or attached inside it). */
    fun findTarget(doc: Document, moving: Set<Long>, x: Float, y: Float): Element? {
        val inside = moving + doc.attachedTree(moving)
        for (i in doc.elements.indices.reversed()) {
            val e = doc.elements[i]
            if (e.id in inside || !isContainer(e)) continue
            if (e.hit(x, y, 0f, solid = true)) return e
        }
        return null
    }

    /** Apply an action. Call inside [Document.batch] together with the move. Returns ids to select. */
    fun apply(doc: Document, a: Action): List<Long> {
        when (a) {
            is Action.LabelShape -> {
                val t = doc.byId(a.textId) as? TextEl ?: return emptyList()
                val s = doc.byId(a.targetId) as? ShapeEl ?: return emptyList()
                val text = if (s.label.isBlank()) t.text else s.label + "\n" + t.text
                doc.replace(listOf(s.withLabel(text, t.color)))
                doc.remove(listOf(t.id))
                return listOf(s.id)
            }
            is Action.LabelNode -> {
                val t = doc.byId(a.textId) as? TextEl ?: return emptyList()
                val n = doc.byId(a.targetId) as? Node ?: return emptyList()
                val text = if (n.text.trim() in PLACEHOLDERS) t.text else n.text + "\n" + t.text
                doc.replace(listOf(n.fitText(text)))
                doc.remove(listOf(t.id))
                return listOf(n.id)
            }
            is Action.AppendSticky -> {
                val t = doc.byId(a.textId) as? TextEl ?: return emptyList()
                val s = doc.byId(a.targetId) as? StickyEl ?: return emptyList()
                doc.replace(listOf(s.withText(if (s.text.isBlank()) t.text else s.text + "\n" + t.text)))
                doc.remove(listOf(t.id))
                return listOf(s.id)
            }
            is Action.TableCell -> {
                val t = doc.byId(a.textId) as? TextEl ?: return emptyList()
                val tb = doc.byId(a.targetId) as? TableEl ?: return emptyList()
                val old = tb.cells[a.cell].orEmpty()
                doc.replace(listOf(tb.withCell(a.cell, if (old.isBlank()) t.text else old + "\n" + t.text)))
                doc.remove(listOf(t.id))
                return listOf(tb.id)
            }
            is Action.MindmapChild -> {
                val parent = doc.byId(a.targetId) as? Node ?: return emptyList()
                val text = when (val e = doc.byId(a.itemId)) {
                    is TextEl -> e.text
                    is StickyEl -> e.text.ifBlank { "Ý mới" }
                    else -> return emptyList()
                }
                doc.remove(listOf(a.itemId))
                val child = doc.addMindmapChild(parent, text)
                doc.layoutMindmap(parent.id)
                return listOf(child.id)
            }
            is Action.Reparent -> {
                val n = doc.byId(a.nodeId) as? Node ?: return emptyList()
                val p = doc.byId(a.targetId) as? Node ?: return emptyList()
                val size = if (p.parentId == 0L) p.size * 0.85f else p.size
                val moved = n.copy(parentId = p.id, mindmap = true).let {
                    if (n.mindmap) it else Node(it.id, it.x, it.y, it.w, it.h, it.text, NodeKind.ROUND, it.fill, size, p.id, true).fitText(it.text)
                }
                doc.replace(listOf(moved))
                doc.setContainer(n.id, 0L)
                doc.layoutMindmap(p.id)
                return listOf(n.id)
            }
            is Action.Attach -> {
                for (id in a.ids) doc.setContainer(id, a.targetId)
                return a.ids
            }
            is Action.Detach -> {
                for (id in a.ids) doc.setContainer(id, 0L)
                return a.ids
            }
        }
    }

    /** Container that fully encloses [b] (for auto-attaching new ink). */
    fun enclosingContainer(doc: Document, b: Box, exclude: Long = 0L): Element? {
        for (i in doc.elements.indices.reversed()) {
            val e = doc.elements[i]
            if (e.id == exclude || !isContainer(e)) continue
            val cb = if (e is ShapeEl) Box.of(e.geo).let { if (e.kind == ShapeKind.ELLIPSE) e.bounds else it } else e.bounds
            if (cb.l <= b.l && cb.r >= b.r && cb.t <= b.t && cb.b >= b.b) return e
        }
        return null
    }
}
