package com.inkboard.ui

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import com.inkboard.canvas.InkCanvasView
import com.inkboard.core.InkCore
import com.inkboard.core.RecognizedShape
import com.inkboard.model.Affine
import com.inkboard.model.Bookmark
import com.inkboard.model.Box
import com.inkboard.model.ShapeLibrary
import com.inkboard.model.TextFit
import com.inkboard.model.Document
import com.inkboard.model.Edge
import com.inkboard.model.Element
import com.inkboard.model.ImageEl
import com.inkboard.model.Node
import com.inkboard.model.NodeKind
import com.inkboard.model.ShapeEl
import com.inkboard.model.ShapeKind
import com.inkboard.model.StickyEl
import com.inkboard.model.Stroke
import com.inkboard.model.TableEl
import com.inkboard.model.TextEl
import com.inkboard.recog.InkToText
import java.io.ByteArrayOutputStream
import java.util.UUID
import kotlin.math.max
import kotlin.math.min

/** Document operations triggered from the editor UI (toolbar, insert menu, context bar). */
class EditorActions(private val view: InkCanvasView) {
    private val doc: Document get() = view.doc
    private val density = view.resources.displayMetrics.density

    /** World size of something that should appear [dp] large on screen now. */
    private fun w(dp: Float) = dp * density / view.scale

    private fun committed(select: List<Long>? = null) {
        view.onDocMutated()
        if (select != null) view.select(select)
    }

    // ------------------------------------------------------------- inserting

    fun insertText(text: String, color: Int) {
        if (text.isBlank()) return
        val (cx, cy) = view.centerWorld()
        val t = TextEl(doc.nextId(), cx, cy, text, w(22f), color)
        val placed = t.transformed(Affine.translate(-t.bounds.w / 2, -t.bounds.h / 2))
        doc.add(placed); committed(listOf(placed.id))
    }

    fun insertSticky() {
        val (cx, cy) = view.centerWorld()
        val s = w(170f)
        val e = StickyEl(doc.nextId(), cx - s / 2, cy - s / 2, s, s, "", STICKY_COLORS[doc.elements.count { it is StickyEl } % STICKY_COLORS.size], w(18f))
        doc.add(e); committed(listOf(e.id))
        view.listener?.onEditRequest(e)
    }

    fun insertShape(kind: String, color: Int) {
        val (cx, cy) = view.centerWorld()
        val r = w(70f)
        val sw = w(2.5f)
        val el = when (kind) {
            "rect" -> ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(cx - r * 1.3f, cy - r, cx + r * 1.3f, cy - r, cx + r * 1.3f, cy + r, cx - r * 1.3f, cy + r), color, sw)
            "ellipse" -> ShapeEl(doc.nextId(), ShapeKind.ELLIPSE, floatArrayOf(cx, cy, r, r, 0f), color, sw)
            "triangle" -> ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(cx, cy - r, cx + r * 1.15f, cy + r, cx - r * 1.15f, cy + r), color, sw)
            "diamond" -> ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(cx, cy - r, cx + r * 1.3f, cy, cx, cy + r, cx - r * 1.3f, cy), color, sw)
            "line" -> ShapeEl(doc.nextId(), ShapeKind.LINE, floatArrayOf(cx - r * 1.5f, cy, cx + r * 1.5f, cy), color, sw)
            else -> ShapeEl(doc.nextId(), ShapeKind.ARROW, floatArrayOf(cx - r * 1.5f, cy, cx + r * 1.5f, cy), color, sw)
        }
        doc.add(el); committed(listOf(el.id))
    }

    fun insertNode(kind: NodeKind, label: String) {
        val (cx, cy) = view.centerWorld()
        val fill = when (kind) {
            NodeKind.DIAMOND -> 0xFFFFE0B2.toInt()
            NodeKind.ELLIPSE -> 0xFFC8E6C9.toInt()
            else -> 0xFFBBDEFB.toInt()
        }
        val n = Node(doc.nextId(), cx, cy, 10f, 10f, "", kind, fill, w(18f)).fitText(label)
        val placed = n.copy(x = cx - n.w / 2, y = cy - n.h / 2)
        // Auto-connect from the selected node, if exactly one is selected.
        val from = selectedNodes().singleOrNull()
        val added = mutableListOf<Element>(placed)
        var finalNode = placed
        if (from != null) {
            finalNode = placed.copy(x = from.x + from.w / 2 - placed.w / 2, y = from.y + from.h + w(60f))
            added[0] = finalNode
            added += Edge(doc.nextId(), from.id, finalNode.id, 0xFF455A64.toInt(), w(2f))
        }
        doc.add(added); committed(listOf(finalNode.id))
    }

    fun insertMindmap() {
        val (cx, cy) = view.centerWorld()
        val n = Node(doc.nextId(), cx, cy, 10f, 10f, "", NodeKind.ROUND, 0xFFFFC857.toInt(), w(24f), 0L, true).fitText("Ý tưởng trung tâm")
        val placed = n.copy(x = cx - n.w / 2, y = cy - n.h / 2)
        doc.add(placed); committed(listOf(placed.id))
    }

    fun insertTable(rows: Int = 3, cols: Int = 3) {
        val (cx, cy) = view.centerWorld()
        val cw = w(110f); val ch = w(48f)
        val t = TableEl(doc.nextId(), cx - cw * cols / 2, cy - ch * rows / 2, cw * cols, ch * rows, rows, cols, 0xFF455A64.toInt(), w(1.5f))
        doc.add(t); committed(listOf(t.id))
    }

    /** Decoded, compressed picture ready to insert (built off the UI thread). */
    class PreparedImage(val bytes: ByteArray, val w: Int, val h: Int)

    companion object Img {
        val PLACEHOLDERS = setOf("Bước", "Điều kiện?", "Bắt đầu", "Ý mới", "Khối", "")

        /** Read, downscale (≤ 2048 px) and JPEG-compress a picture. Call on a background thread. */
        fun prepareImage(context: Context, uri: Uri, maxSide: Int = 2048): PreparedImage? {
            val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: return null
            return prepareBytes(bytes, maxSide)
        }

        fun prepareBytes(bytes: ByteArray, maxSide: Int = 2048): PreparedImage? {
            val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)
            var sample = 1
            while (max(opts.outWidth, opts.outHeight) / sample > maxSide * 2) sample *= 2
            val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample }) ?: return null
            val f = min(1f, maxSide.toFloat() / max(bmp.width, bmp.height))
            val scaled = if (f < 1f) Bitmap.createScaledBitmap(bmp, (bmp.width * f).toInt().coerceAtLeast(1), (bmp.height * f).toInt().coerceAtLeast(1), true) else bmp
            val out = ByteArrayOutputStream()
            scaled.compress(Bitmap.CompressFormat.JPEG, 88, out)
            return PreparedImage(out.toByteArray(), scaled.width, scaled.height)
        }

        val STICKY_COLORS = intArrayOf(0xFFFFF59D.toInt(), 0xFFFFCC80.toInt(), 0xFFA5D6A7.toInt(), 0xFF90CAF9.toInt(), 0xFFF48FB1.toInt(), 0xFFCE93D8.toInt())
    }

    /** Insert a prepared picture at the centre of the view (UI thread, cheap). */
    fun insertPrepared(img: PreparedImage, widthFraction: Float = 0.6f): Long {
        val key = UUID.randomUUID().toString()
        doc.images[key] = img.bytes
        val (cx, cy) = view.centerWorld()
        val ww = view.width * widthFraction / view.scale
        val hh = ww * img.h / img.w
        val e = ImageEl(doc.nextId(), cx - ww / 2, cy - hh / 2, ww, hh, key)
        doc.add(e); committed(listOf(e.id))
        return e.id
    }

    // ------------------------------------------------------- selection edits

    fun selected(): List<Element> = view.selection.mapNotNull { doc.byId(it) }
    fun selectedNodes(): List<Node> = selected().filterIsInstance<Node>()
    fun selectedStrokes(): List<Stroke> = selected().filterIsInstance<Stroke>()

    fun delete() {
        doc.remove(view.selection.toList()); view.clearSelection(); committed()
    }

    fun duplicate() {
        val sel = selected()
        if (sel.isEmpty()) return
        val off = w(24f)
        val idMap = HashMap<Long, Long>()
        for (e in sel) idMap[e.id] = doc.nextId()
        val copies = sel.mapNotNull { e ->
            val nid = idMap.getValue(e.id)
            when (e) {
                is Edge -> if (e.from in idMap && e.to in idMap) Edge(nid, idMap.getValue(e.from), idMap.getValue(e.to), e.color, e.width) else null
                is Node -> {
                    val parent = if (e.parentId in idMap) idMap.getValue(e.parentId) else 0L
                    (e.transformed(Affine.translate(off, off)) as Node).copy(parentId = parent).withId(nid)
                }
                else -> e.transformed(Affine.translate(off, off)).withId(nid)
            }
        }
        doc.batch {
            doc.add(copies)
            // Keep "attached to" relations inside the copied group.
            for (e in sel) {
                val c = doc.containerOf(e.id)
                if (c in idMap) doc.setContainer(idMap.getValue(e.id), idMap.getValue(c))
            }
        }
        committed(copies.map { it.id })
    }

    fun reorder(front: Boolean) {
        doc.reorder(view.selection.toList(), front); committed(view.selection.toList())
    }

    fun recolor(color: Int, sticky: Int, fill: Int) {
        val updated = selected().mapNotNull { e ->
            when (e) {
                is Stroke -> e.withColor((color and 0x00FFFFFF) or (e.color and 0xFF000000.toInt()))
                is ShapeEl -> e.withStyle(color = color)
                is TextEl -> e.withColor(color)
                is StickyEl -> e.withColor(sticky)
                is Node -> e.copy(fill = fill)
                is TableEl -> e.withColor(color)
                is Edge -> Edge(e.id, e.from, e.to, color, e.width)
                else -> null
            }
        }
        doc.replace(updated); committed(view.selection.toList())
    }

    fun setText(el: Element, text: String, wx: Float = Float.NaN, wy: Float = Float.NaN) {
        val updated = when (el) {
            is TextEl -> if (text.isBlank()) null else el.withText(text)
            is StickyEl -> el.withText(text)
            is Node -> el.fitText(text.ifBlank { " " })
            is ShapeEl -> el.withLabel(text.trim())
            is TableEl -> el.withCell(if (wx.isNaN()) 0 else el.cellAt(wx, wy), text.trim())
            is Edge -> el.withLabel(text.trim())
            else -> return
        }
        if (updated == null) { doc.remove(listOf(el.id)); committed(); return }
        doc.batch {
            doc.replace(listOf(updated))
            if (el is Node && el.mindmap) doc.layoutMindmap(el.id)
        }
        committed(listOf(el.id))
    }

    fun addBranch() {
        val parent = doc.byId(view.primaryId) as? Node ?: return
        var child: Node? = null
        doc.batch { child = doc.addMindmapChild(parent); doc.layoutMindmap(parent.id) }
        val c = child ?: return
        committed(listOf(c.id))
        view.listener?.onEditRequest(doc.byId(c.id)!!)
    }

    fun tidyMindmap() {
        doc.layoutMindmap(view.primaryId); committed(view.selection.toList())
    }

    /** The two top-level connectable items of the selection (attachments and edges excluded), if exactly two. */
    fun connectPair(): Pair<Element, Element>? {
        val sel = view.selection
        val tops = selected().filter { com.inkboard.model.Anchors.connectable(it) && doc.containerOf(it.id).let { c -> c == 0L || c !in sel } }
            .filter { !(it is Node && it.mindmap && it.parentId in sel) }
        return if (tops.size == 2) tops[0] to tops[1] else null
    }

    fun connect() {
        val (a, b) = connectPair() ?: return
        val e = Edge(doc.nextId(), a.id, b.id, 0xFF455A64.toInt(), w(2f))
        doc.add(e); committed(view.selection.toList())
    }

    fun tableResize(dr: Int, dc: Int) {
        val t = selected().filterIsInstance<TableEl>().singleOrNull() ?: return
        doc.replace(listOf(t.resized(t.rows + dr, t.cols + dc))); committed(listOf(t.id))
    }

    /** Convert one selected stroke into a clean shape. */
    fun strokeToShape(): Boolean {
        val s = selectedStrokes().singleOrNull() ?: return false
        val shape = InkCore.recognize(s.xy(), false) ?: return false
        val el = when (shape) {
            is RecognizedShape.Line -> ShapeEl(doc.nextId(), ShapeKind.LINE, floatArrayOf(shape.x0, shape.y0, shape.x1, shape.y1), s.color, s.width)
            is RecognizedShape.Polygon -> ShapeEl(doc.nextId(), ShapeKind.POLYGON, shape.pts, s.color, s.width)
            is RecognizedShape.Ellipse -> ShapeEl(doc.nextId(), ShapeKind.ELLIPSE, floatArrayOf(shape.cx, shape.cy, shape.rx, shape.ry, shape.angle), s.color, s.width)
        }
        doc.swap(listOf(s.id), listOf(el)); committed(listOf(el.id))
        return true
    }

    /** Handwriting → editable text (one undo step). Throws when the model is missing. */
    suspend fun inkToText(lang: String): String {
        val strokes = selectedStrokes()
        if (strokes.isEmpty()) return ""
        if (!InkToText.isDownloaded(lang)) throw IllegalStateException("MODEL_MISSING")
        val text = InkToText.recognize(strokes, lang)
        if (text.isBlank()) return ""
        val b = strokes.fold(com.inkboard.model.Box.EMPTY) { a, s -> a.union(s.bounds) }
        val lines = InkToText.groupLines(strokes).size
        val size = (b.h / lines) * 0.62f
        // Handwriting inside a shape / block / note / table cell becomes that container's text.
        val container = strokes.map { doc.containerOf(it.id) }.distinct().singleOrNull()?.let { doc.byId(it) }
        val oneLine = text.replace('\n', ' ')
        when (container) {
            is ShapeEl -> if (container.closed) {
                doc.batch { doc.remove(strokes.map { it.id }); doc.replace(listOf(container.withLabel(if (container.label.isBlank()) text else container.label + "\n" + text, strokes.first().color))) }
                committed(listOf(container.id)); return text
            }
            is Node -> {
                doc.batch {
                    doc.remove(strokes.map { it.id }); doc.replace(listOf(container.fitText(if (container.text.isBlank() || container.text in PLACEHOLDERS) text else container.text + "\n" + text)))
                    if (container.mindmap) doc.layoutMindmap(container.id)
                }
                committed(listOf(container.id)); return text
            }
            is StickyEl -> {
                doc.batch { doc.remove(strokes.map { it.id }); doc.replace(listOf(container.withText(if (container.text.isBlank()) text else container.text + "\n" + text))) }
                committed(listOf(container.id)); return text
            }
            is TableEl -> {
                val cell = container.cellAt(b.cx, b.cy)
                val old = container.cells[cell].orEmpty()
                doc.batch { doc.remove(strokes.map { it.id }); doc.replace(listOf(container.withCell(cell, if (old.isBlank()) oneLine else old + "\n" + oneLine))) }
                committed(listOf(container.id)); return text
            }
            else -> Unit
        }
        val t = TextEl(doc.nextId(), b.l, b.t, text, max(size, w(10f)), strokes.first().color)
        doc.swap(strokes.map { it.id }, listOf(t)); committed(listOf(t.id))
        return text
    }

    // ------------------------------------------------------------ more tools

    fun toggleLock() {
        val ids = view.selection.toList()
        if (ids.isEmpty()) return
        doc.locked.addAll(ids); doc.markDirty()
        view.clearSelection(); committed()
    }

    fun unlockAll(): Int {
        val n = doc.locked.size
        doc.locked.clear(); doc.markDirty(); committed()
        return n
    }

    fun toggleCollapse() {
        val n = doc.byId(view.primaryId) as? Node ?: return
        doc.toggleCollapsed(n.id)
        committed(listOf(n.id))
    }

    /** Closed hand-drawn / library shapes → connectable blocks (keeps their label). */
    fun shapesToNodes(): Int {
        val shapes = selected().filterIsInstance<ShapeEl>().filter { it.closed }
        if (shapes.isEmpty()) return 0
        val nodes = shapes.map { s ->
            val b = Box.of(s.geo).let { if (s.kind == ShapeKind.ELLIPSE) s.bounds.inset(s.width) else it }
            val kind = when {
                s.kind == ShapeKind.ELLIPSE -> NodeKind.ELLIPSE
                s.geo.size == 8 && TextFit.polygonArea(s.geo).let { kotlin.math.abs(it) } < b.w * b.h * 0.7f -> NodeKind.DIAMOND
                else -> NodeKind.RECT
            }
            Node(doc.nextId(), b.l, b.t, b.w, b.h, s.label, kind, if (s.fill != 0) s.fill else 0xFFFFFFFF.toInt(), max(b.h * 0.18f, w(12f)))
        }
        doc.swap(shapes.map { it.id }, nodes); committed(nodes.map { it.id })
        return nodes.size
    }

    fun insertLibrary(entry: ShapeLibrary.Entry, color: Int) {
        val (cx, cy) = view.centerWorld()
        val els = entry.build(doc, cx, cy, w(70f), color, w(2.5f))
        doc.add(els); committed(els.map { it.id })
    }

    fun insertFromLibraryDoc(lib: Document) {
        val (cx, cy) = view.centerWorld()
        val els = com.inkboard.data.LibraryStore.instantiate(lib, doc, cx, cy)
        committed(els.map { it.id })
    }

    /** Fill colour for closed shapes / blocks / notes (0 = no fill for shapes). */
    fun setFill(color: Int) {
        val updated = selected().mapNotNull { e ->
            when (e) {
                is ShapeEl -> if (e.closed) e.withStyle(fill = color) else null
                is Node -> e.copy(fill = if (color == 0) 0xFFFFFFFF.toInt() else color)
                is StickyEl -> if (color != 0) e.withColor(color) else null
                else -> null
            }
        }
        if (updated.isEmpty()) return
        doc.replace(updated); committed(view.selection.toList())
    }

    fun reverseEdge() {
        val e = selected().singleOrNull { it is Edge } as? Edge ?: return
        doc.replace(listOf(Edge(e.id, e.to, e.from, e.color, e.width, e.label))); committed(listOf(e.id))
    }

    fun paste(clip: Document) {
        val (cx, cy) = view.centerWorld()
        val els = com.inkboard.data.LibraryStore.instantiate(clip, doc, cx, cy)
        committed(els.map { it.id })
    }

    fun addBookmark(name: String) {
        val (cx, cy) = view.centerWorld()
        doc.bookmarks += Bookmark(name.ifBlank { "Vị trí ${doc.bookmarks.size + 1}" }, view.scale, cx, cy)
        doc.markDirty(); view.listener?.onDocChanged()
    }

    fun goTo(b: Bookmark) {
        view.setZoom(b.scale); view.centerOn(b.cx, b.cy)
    }

}
