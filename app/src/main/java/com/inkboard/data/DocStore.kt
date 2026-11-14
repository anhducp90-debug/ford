package com.inkboard.data

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import com.inkboard.canvas.Renderer
import com.inkboard.model.Affine
import com.inkboard.model.DocCodec
import com.inkboard.model.Document
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID
import kotlin.math.min

data class DocMeta(
    val id: String,
    val name: String,
    val modified: Long,
    val folder: String = "",
    val trashedAt: Long = 0L,
)

/**
 * Local, offline document storage:
 *   files/docs/<id>.inkb   document
 *   files/docs/<id>.png    thumbnail
 *   files/docs/index.json  names, folders, dates, trash
 */
class DocStore(context: Context) {
    private val dir = File(context.filesDir, "docs").apply { mkdirs() }
    private val indexFile = File(dir, "index.json")
    private val lock = Any()

    fun list(): List<DocMeta> = synchronized(lock) { readIndex() }

    fun folders(): List<String> = list().filter { it.trashedAt == 0L }.map { it.folder }.filter { it.isNotEmpty() }.distinct().sorted()

    fun create(name: String, folder: String = "", doc: Document = Document()): DocMeta {
        val meta = DocMeta(UUID.randomUUID().toString(), name, System.currentTimeMillis(), folder)
        save(meta.id, doc)
        synchronized(lock) { writeIndex(readIndex() + meta) }
        return meta
    }

    fun load(id: String): Document = File(dir, "$id.inkb").inputStream().buffered().use { DocCodec.read(it) }

    /** Atomic save (write temp then rename) so a crash never corrupts a document. */
    private val saveLocks = java.util.concurrent.ConcurrentHashMap<String, Any>()

    /** Atomic save (write temp then rename); saves of the same document never overlap. */
    fun save(id: String, doc: Document) = synchronized(saveLocks.getOrPut(id) { Any() }) { saveLocked(id, doc) }

    private fun saveLocked(id: String, doc: Document) {
        val tmp = File(dir, "$id.inkb.tmp")
        tmp.outputStream().buffered().use { DocCodec.write(doc, it) }
        if (!tmp.renameTo(File(dir, "$id.inkb"))) {
            File(dir, "$id.inkb").delete(); tmp.renameTo(File(dir, "$id.inkb"))
        }
        update(id) { it.copy(modified = System.currentTimeMillis()) }
    }

    fun thumbnailFile(id: String) = File(dir, "$id.png")

    fun saveThumbnail(id: String, doc: Document) {
        val b = doc.contentBounds()
        val size = 480
        val bmp = Bitmap.createBitmap(size, (size * 0.75f).toInt(), Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        c.drawColor(0xFFFFFFFF.toInt())
        if (!b.isEmpty) {
            val pad = 16f
            val s = min((bmp.width - 2 * pad) / maxOf(b.w, 1f), (bmp.height - 2 * pad) / maxOf(b.h, 1f)).coerceAtMost(2f)
            val a = Affine(s, bmp.width / 2f - b.cx * s, bmp.height / 2f - b.cy * s)
            Renderer().drawAll(c, doc, a)
        }
        thumbnailFile(id).outputStream().use { bmp.compress(Bitmap.CompressFormat.PNG, 90, it) }
        bmp.recycle()
    }

    fun rename(id: String, name: String) = update(id) { it.copy(name = name) }
    fun move(id: String, folder: String) = update(id) { it.copy(folder = folder) }
    fun trash(id: String) = update(id) { it.copy(trashedAt = System.currentTimeMillis()) }
    fun restore(id: String) = update(id) { it.copy(trashedAt = 0L) }

    fun duplicate(id: String): DocMeta? {
        val src = list().find { it.id == id } ?: return null
        val doc = load(id)
        return create(src.name + " (bản sao)", src.folder, doc).also { m ->
            thumbnailFile(id).takeIf { it.exists() }?.copyTo(thumbnailFile(m.id), overwrite = true)
        }
    }

    fun deleteForever(id: String) = synchronized(lock) {
        File(dir, "$id.inkb").delete(); thumbnailFile(id).delete()
        writeIndex(readIndex().filter { it.id != id })
    }

    // ------------------------------------------------------- backup / import

    /** Write every document (not in the trash) into a zip backup. Returns the count. */
    fun exportBackup(out: java.io.OutputStream): Int {
        val items = list().filter { it.trashedAt == 0L }
        java.util.zip.ZipOutputStream(out.buffered()).use { zip ->
            val arr = JSONArray()
            for (m in items) arr.put(JSONObject().put("id", m.id).put("name", m.name).put("modified", m.modified).put("folder", m.folder))
            zip.putNextEntry(java.util.zip.ZipEntry("inkboard-backup.json"))
            zip.write(JSONObject().put("format", 1).put("docs", arr).toString().toByteArray())
            zip.closeEntry()
            for (m in items) {
                val f = File(dir, "${m.id}.inkb")
                if (!f.exists()) continue
                zip.putNextEntry(java.util.zip.ZipEntry("docs/${m.id}.inkb"))
                f.inputStream().use { it.copyTo(zip) }
                zip.closeEntry()
            }
        }
        return items.size
    }

    /** Restore a zip backup; documents that already exist are imported as copies. Returns the count. */
    fun importBackup(input: java.io.InputStream): Int {
        var manifest: JSONObject? = null
        val files = HashMap<String, ByteArray>()
        java.util.zip.ZipInputStream(input.buffered()).use { zip ->
            while (true) {
                val e = zip.nextEntry ?: break
                val bytes = zip.readBytes()
                if (bytes.size > 512 * 1024 * 1024) throw java.io.IOException("Tệp quá lớn")
                when {
                    e.name == "inkboard-backup.json" -> manifest = JSONObject(String(bytes))
                    e.name.startsWith("docs/") && e.name.endsWith(".inkb") && !e.name.contains("..") -> files[e.name.removePrefix("docs/").removeSuffix(".inkb")] = bytes
                }
            }
        }
        val docs = manifest?.optJSONArray("docs") ?: throw java.io.IOException("Không phải bản sao lưu InkBoard")
        val existing = list().map { it.id }.toSet()
        var n = 0
        for (i in 0 until docs.length()) {
            val o = docs.getJSONObject(i)
            val bytes = files[o.getString("id")] ?: continue
            val doc = runCatching { DocCodec.read(bytes.inputStream()) }.getOrNull() ?: continue
            val name = o.optString("name", "Bản vẽ") + if (o.getString("id") in existing) " (khôi phục)" else ""
            val meta = create(name, o.optString("folder", ""), doc)
            runCatching { saveThumbnail(meta.id, doc) }
            n++
        }
        return n
    }

    /** Import a single .inkb file. */
    fun importInkb(input: java.io.InputStream, name: String): DocMeta {
        val doc = DocCodec.read(input.buffered())
        return create(name, "", doc).also { runCatching { saveThumbnail(it.id, doc) } }
    }

    /**
     * New document whose pages are pictures stacked vertically and locked
     * (PDF annotation / scanned paper). [pages] = JPEG bytes with pixel size.
     */
    fun createFromPages(name: String, pages: List<Triple<ByteArray, Int, Int>>): DocMeta {
        val doc = Document()
        doc.background = com.inkboard.model.Background.PLAIN
        val pageW = 1000f
        var y = 0f
        for ((bytes, w, h) in pages) {
            val key = UUID.randomUUID().toString()
            doc.images[key] = bytes
            val ph = pageW * h / w.coerceAtLeast(1)
            val el = com.inkboard.model.ImageEl(doc.nextId(), -pageW / 2, y, pageW, ph, key)
            doc.add(el)
            doc.locked += el.id
            y += ph + 40f
        }
        doc.clearHistory()
        return create(name, "", doc).also { runCatching { saveThumbnail(it.id, doc) } }
    }

    /** Remove items that have been in the trash for more than 30 days. */
    fun purgeTrash() {
        val cutoff = System.currentTimeMillis() - 30L * 24 * 3600 * 1000
        list().filter { it.trashedAt in 1 until cutoff }.forEach { deleteForever(it.id) }
    }

    private fun update(id: String, f: (DocMeta) -> DocMeta) = synchronized(lock) {
        writeIndex(readIndex().map { if (it.id == id) f(it) else it })
    }

    private fun readIndex(): List<DocMeta> {
        if (!indexFile.exists()) return emptyList()
        return try {
            val arr = JSONArray(indexFile.readText())
            (0 until arr.length()).map { i ->
                val o = arr.getJSONObject(i)
                DocMeta(o.getString("id"), o.getString("name"), o.getLong("modified"), o.optString("folder", ""), o.optLong("trashedAt", 0L))
            }.filter { File(dir, "${it.id}.inkb").exists() }
        } catch (e: Exception) {
            emptyList()
        }
    }

    private fun writeIndex(items: List<DocMeta>) {
        val arr = JSONArray()
        for (m in items) arr.put(JSONObject().put("id", m.id).put("name", m.name).put("modified", m.modified).put("folder", m.folder).put("trashedAt", m.trashedAt))
        val tmp = File(dir, "index.json.tmp")
        tmp.writeText(arr.toString())
        if (!tmp.renameTo(indexFile)) { indexFile.delete(); tmp.renameTo(indexFile) }
    }
}
