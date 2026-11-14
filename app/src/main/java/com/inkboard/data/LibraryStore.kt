package com.inkboard.data

import com.inkboard.model.Affine
import com.inkboard.model.DocCodec
import com.inkboard.model.Document
import com.inkboard.model.Edge
import com.inkboard.model.Element
import com.inkboard.model.ImageEl
import com.inkboard.model.Node
import java.io.File
import java.util.UUID

/** "My library": reusable groups saved from any document (stored as small .inkb files). */
class LibraryStore(root: File) {
    private val dir = File(root, "library").apply { mkdirs() }

    data class Item(val id: String, val name: String, val file: File)

    fun list(): List<Item> = (dir.listFiles { f -> f.name.endsWith(".inkb") } ?: emptyArray())
        .sortedByDescending { it.lastModified() }
        .map { f -> val id = f.name.removeSuffix(".inkb"); Item(id, File(dir, "$id.name").takeIf { it.exists() }?.readText() ?: "Mẫu", f) }

    /** Save [ids] of [doc] (with their attachments, images and internal relations) under [name]. */
    fun save(doc: Document, ids: Collection<Long>, name: String): Item {
        val sub = Document()
        val set = ids.toSet()
        val els = doc.elements.filter { it.id in set && (it !is Edge || (it.from in set && it.to in set)) }
        sub.elements.addAll(els)
        for (e in els) if (e is ImageEl) doc.images[e.key]?.let { sub.images[e.key] = it }
        for (e in els) doc.attach[e.id]?.takeIf { it in set }?.let { sub.attach[e.id] = it }
        val id = UUID.randomUUID().toString()
        val f = File(dir, "$id.inkb")
        f.outputStream().buffered().use { DocCodec.write(sub, it) }
        File(dir, "$id.name").writeText(name.ifBlank { "Mẫu" })
        return Item(id, name, f)
    }

    fun delete(item: Item) { item.file.delete(); File(dir, "${item.id}.name").delete() }

    fun load(item: Item): Document = item.file.inputStream().buffered().use { DocCodec.read(it) }

    companion object {
        /**
         * Copy a library document's elements into [target] centred on (cx, cy),
         * with fresh ids and remapped relations. Returns the new elements.
         */
        fun instantiate(lib: Document, target: Document, cx: Float, cy: Float): List<Element> {
            val b = lib.contentBounds()
            if (b.isEmpty) return emptyList()
            val a = Affine.translate(cx - b.cx, cy - b.cy)
            val map = HashMap<Long, Long>()
            for (e in lib.elements) map[e.id] = target.nextId()
            val keyMap = HashMap<String, String>()
            val out = lib.elements.map { e ->
                val nid = map.getValue(e.id)
                when (e) {
                    is Edge -> Edge(nid, map[e.from] ?: e.from, map[e.to] ?: e.to, e.color, e.width, e.label)
                    is Node -> (e.transformed(a) as Node).copy(parentId = map[e.parentId] ?: 0L).withId(nid)
                    is ImageEl -> {
                        val k = keyMap.getOrPut(e.key) { UUID.randomUUID().toString() }
                        lib.images[e.key]?.let { target.images[k] = it }
                        ImageEl(nid, e.x + a.tx, e.y + a.ty, e.w, e.h, k)
                    }
                    else -> e.transformed(a).withId(nid)
                }
            }
            target.batch {
                target.add(out)
                for ((c, p) in lib.attach) { val nc = map[c]; val np = map[p]; if (nc != null && np != null) target.setContainer(nc, np) }
            }
            return out
        }
    }
}
