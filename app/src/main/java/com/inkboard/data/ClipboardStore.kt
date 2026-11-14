package com.inkboard.data

import com.inkboard.model.DocCodec
import com.inkboard.model.Document
import com.inkboard.model.Edge
import com.inkboard.model.ImageEl
import java.io.File

/** In-app clipboard shared by all documents (kept as a small .inkb so it survives restarts). */
class ClipboardStore(root: File) {
    private val file = File(root, "clipboard.inkb")

    val hasContent get() = file.exists() && file.length() > 0

    fun copy(doc: Document, ids: Collection<Long>) {
        val set = ids.toSet()
        val sub = Document()
        val els = doc.elements.filter { it.id in set && (it !is Edge || (it.from in set && it.to in set)) }
        sub.elements.addAll(els)
        for (e in els) if (e is ImageEl) doc.images[e.key]?.let { sub.images[e.key] = it }
        for (e in els) doc.attach[e.id]?.takeIf { it in set }?.let { sub.attach[e.id] = it }
        val tmp = File(file.path + ".tmp")
        tmp.outputStream().buffered().use { DocCodec.write(sub, it) }
        tmp.renameTo(file)
    }

    fun load(): Document? = if (!hasContent) null else runCatching { file.inputStream().buffered().use { DocCodec.read(it) } }.getOrNull()
}
