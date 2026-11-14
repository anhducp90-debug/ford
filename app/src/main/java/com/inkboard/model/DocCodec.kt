package com.inkboard.model

import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.util.zip.Deflater
import java.util.zip.DeflaterOutputStream
import java.util.zip.InflaterInputStream

/**
 * `.inkb` binary format (v1):
 *   magic "INKB", int version, deflate stream of:
 *   background, camera, element count, elements (tagged), image count, images.
 */
object DocCodec {
    private const val MAGIC = 0x494E4B42 // "INKB"
    private const val VERSION = 4

    private const val T_STROKE = 1
    private const val T_SHAPE = 2
    private const val T_TEXT = 3
    private const val T_STICKY = 4
    private const val T_NODE = 5
    private const val T_EDGE = 6
    private const val T_IMAGE = 7
    private const val T_TABLE = 8
    /** v4 compact stroke: quantized (1/64 unit) delta varints + pressure byte. */
    private const val T_STROKE2 = 9
    private const val Q = 64f

    fun write(doc: Document, out: OutputStream) {
        val head = DataOutputStream(out)
        head.writeInt(MAGIC)
        head.writeInt(VERSION)
        head.flush()
        val deflater = Deflater(Deflater.BEST_SPEED)
        val def = DeflaterOutputStream(out, deflater, 1 shl 16)
        val d = DataOutputStream(BufferedOutputStream(def, 1 shl 16))
        d.writeByte(doc.background.ordinal)
        d.writeFloat(doc.camScale); d.writeFloat(doc.camX); d.writeFloat(doc.camY)
        d.writeInt(doc.elements.size)
        for (e in doc.elements) d.write(encoded(e))
        val ids = doc.elements.map { it.id }.toHashSet()
        val att = doc.attach.filter { (c, p) -> c in ids && p in ids }
        d.writeInt(att.size)
        for ((c, p) in att) { d.writeLong(c); d.writeLong(p) }
        val g = doc.guides
        d.writeByte(g.symmetry.ordinal); d.writeInt(g.radialN); d.writeFloat(g.symX); d.writeFloat(g.symY)
        d.writeByte(g.perspective); d.writeFloat(g.vp1x); d.writeFloat(g.vp1y); d.writeFloat(g.vp2x); d.writeFloat(g.vp2y)
        d.writeBoolean(g.assist)
        val lk = doc.locked.filter { it in ids }
        d.writeInt(lk.size); for (id in lk) d.writeLong(id)
        val usedKeys = doc.elements.filterIsInstance<ImageEl>().map { it.key }.toSet()
        val imgs = doc.images.filterKeys { it in usedKeys }
        d.writeInt(imgs.size)
        for ((k, v) in imgs) {
            d.writeStr(k); d.writeInt(v.size); d.write(v)
        }
        d.flush()
        def.finish()
        deflater.end()
        out.flush()
    }

    fun read(input: InputStream): Document {
        val head = DataInputStream(input)
        if (head.readInt() != MAGIC) throw IOException("Không phải tệp InkBoard")
        val ver = head.readInt()
        if (ver > VERSION) throw IOException("Tệp được tạo bởi phiên bản mới hơn")
        val d = DataInputStream(InflaterInputStream(input).buffered())
        val doc = Document()
        doc.background = Background.entries.getOrElse(d.readUnsignedByte()) { Background.DOTS }
        doc.camScale = d.readFloat(); doc.camX = d.readFloat(); doc.camY = d.readFloat()
        val n = d.readInt()
        repeat(n) { readElement(d, ver)?.let { doc.elements.add(it) } }
        if (ver >= 2) repeat(d.readInt()) { doc.attach[d.readLong()] = d.readLong() }
        if (ver >= 3) {
            doc.guides = Guides(
                SymmetryMode.entries.getOrElse(d.readUnsignedByte()) { SymmetryMode.OFF }, d.readInt(), d.readFloat(), d.readFloat(),
                d.readUnsignedByte(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readBoolean()
            )
        }
        if (ver >= 4) repeat(d.readInt()) { doc.locked += d.readLong() }
        val ni = d.readInt()
        repeat(ni) {
            val k = d.readStr()
            val bytes = ByteArray(d.readInt())
            d.readFully(bytes)
            doc.images[k] = bytes
        }
        doc.syncIds()
        return doc
    }

    private fun DataOutputStream.writeStr(v: String) {
        val b = v.toByteArray(Charsets.UTF_8); writeInt(b.size); write(b)
    }

    private fun DataInputStream.readStr(): String {
        val n = readInt()
        if (n < 0 || n > 64_000_000) throw IOException("Dữ liệu hỏng")
        val b = ByteArray(n); readFully(b); return String(b, Charsets.UTF_8)
    }

    private fun DataOutputStream.writeFloats(a: FloatArray) {
        writeInt(a.size); for (v in a) writeFloat(v)
    }

    private fun DataInputStream.readFloats(): FloatArray {
        val n = readInt()
        if (n < 0 || n > 50_000_000) throw IOException("Dữ liệu hỏng")
        return FloatArray(n) { readFloat() }
    }

    /** Element bytes, cached on the (immutable) element so repeated autosaves are cheap. */
    fun encoded(e: Element): ByteArray = e.enc ?: ByteArrayOutputStream(64).also { bo ->
        DataOutputStream(bo).use { writeElement(it, e) }
    }.toByteArray().also { e.enc = it }

    private fun DataOutputStream.writeVar(v: Long) {
        var x = (v shl 1) xor (v shr 63) // zigzag
        while (x and 0x7FL.inv() != 0L) { writeByte(((x and 0x7F) or 0x80).toInt()); x = x ushr 7 }
        writeByte(x.toInt())
    }

    private fun DataInputStream.readVar(): Long {
        var shift = 0; var x = 0L
        while (true) {
            val b = readUnsignedByte()
            x = x or ((b and 0x7F).toLong() shl shift)
            if (b and 0x80 == 0) break
            shift += 7
            if (shift > 63) throw IOException("Dữ liệu hỏng")
        }
        return (x ushr 1) xor -(x and 1)
    }

    private fun writeElement(d: DataOutputStream, e: Element) {
        when (e) {
            is Stroke -> {
                d.writeByte(T_STROKE2); d.writeLong(e.id); d.writeInt(e.color); d.writeFloat(e.width)
                d.writeByte(e.pen.ordinal); d.writeByte(if (e.taper) 1 else 0)
                val n = e.count
                d.writeInt(n)
                var px = 0L; var py = 0L
                for (i in 0 until n) {
                    val qx = Math.round(e.pts[3 * i] * Q).toLong(); val qy = Math.round(e.pts[3 * i + 1] * Q).toLong()
                    d.writeVar(qx - px); d.writeVar(qy - py); px = qx; py = qy
                    d.writeByte(Math.round(e.pts[3 * i + 2].coerceIn(0f, 1f) * 255f))
                }
            }
            else -> writeOther(d, e)
        }
    }

    private fun writeOther(d: DataOutputStream, e: Element) {
        when (e) {
            is Stroke -> {
                d.writeByte(T_STROKE); d.writeLong(e.id); d.writeInt(e.color); d.writeFloat(e.width)
                d.writeByte(e.pen.ordinal); d.writeFloats(e.pts); d.writeByte(if (e.taper) 1 else 0)
            }
            is ShapeEl -> {
                d.writeByte(T_SHAPE); d.writeLong(e.id); d.writeByte(e.kind.ordinal); d.writeInt(e.color)
                d.writeFloat(e.width); d.writeInt(e.fill); d.writeFloats(e.geo)
                d.writeStr(e.label); d.writeInt(e.labelColor)
            }
            is TextEl -> {
                d.writeByte(T_TEXT); d.writeLong(e.id); d.writeFloat(e.x); d.writeFloat(e.y)
                d.writeStr(e.text); d.writeFloat(e.size); d.writeInt(e.color)
            }
            is StickyEl -> {
                d.writeByte(T_STICKY); d.writeLong(e.id); d.writeFloat(e.x); d.writeFloat(e.y)
                d.writeFloat(e.w); d.writeFloat(e.h); d.writeStr(e.text); d.writeInt(e.color); d.writeFloat(e.size)
            }
            is Node -> {
                d.writeByte(T_NODE); d.writeLong(e.id); d.writeFloat(e.x); d.writeFloat(e.y)
                d.writeFloat(e.w); d.writeFloat(e.h); d.writeStr(e.text); d.writeByte(e.kind.ordinal)
                d.writeInt(e.fill); d.writeFloat(e.size); d.writeLong(e.parentId); d.writeBoolean(e.mindmap)
            }
            is Edge -> {
                d.writeByte(T_EDGE); d.writeLong(e.id); d.writeLong(e.from); d.writeLong(e.to)
                d.writeInt(e.color); d.writeFloat(e.width)
            }
            is TableEl -> {
                d.writeByte(T_TABLE); d.writeLong(e.id); d.writeFloat(e.x); d.writeFloat(e.y)
                d.writeFloat(e.w); d.writeFloat(e.h); d.writeInt(e.rows); d.writeInt(e.cols)
                d.writeInt(e.color); d.writeFloat(e.width)
                d.writeInt(e.cells.size)
                for ((k, v) in e.cells) { d.writeInt(k); d.writeStr(v) }
            }
            is ImageEl -> {
                d.writeByte(T_IMAGE); d.writeLong(e.id); d.writeFloat(e.x); d.writeFloat(e.y)
                d.writeFloat(e.w); d.writeFloat(e.h); d.writeStr(e.key)
            }
        }
    }

    private fun readElement(d: DataInputStream, ver: Int): Element? = when (d.readUnsignedByte()) {
        T_STROKE2 -> {
            val id = d.readLong(); val color = d.readInt(); val w = d.readFloat()
            val pen = PenType.entries.getOrElse(d.readUnsignedByte()) { PenType.PEN }
            val taper = d.readUnsignedByte() == 1
            val n = d.readInt()
            if (n < 0 || n > 20_000_000) throw IOException("Dữ liệu hỏng")
            val pts = FloatArray(n * 3)
            var px = 0L; var py = 0L
            for (i in 0 until n) {
                px += d.readVar(); py += d.readVar()
                pts[3 * i] = px / Q; pts[3 * i + 1] = py / Q; pts[3 * i + 2] = d.readUnsignedByte() / 255f
            }
            Stroke(id, pts, color, w, pen, taper)
        }
        T_STROKE -> {
            val id = d.readLong(); val color = d.readInt(); val w = d.readFloat()
            val pen = PenType.entries.getOrElse(d.readUnsignedByte()) { PenType.PEN }
            val pts = d.readFloats()
            Stroke(id, pts, color, w, pen, if (ver >= 3) d.readUnsignedByte() == 1 else false)
        }
        T_SHAPE -> {
            val id = d.readLong(); val kind = ShapeKind.entries[d.readUnsignedByte()]
            val color = d.readInt(); val w = d.readFloat(); val fill = d.readInt()
            val geo = d.readFloats()
            if (ver >= 2) ShapeEl(id, kind, geo, color, w, fill, d.readStr(), d.readInt())
            else ShapeEl(id, kind, geo, color, w, fill)
        }
        T_TEXT -> TextEl(d.readLong(), d.readFloat(), d.readFloat(), d.readStr(), d.readFloat(), d.readInt())
        T_STICKY -> StickyEl(d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readStr(), d.readInt(), d.readFloat())
        T_NODE -> Node(
            d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readStr(),
            NodeKind.entries[d.readUnsignedByte()], d.readInt(), d.readFloat(), d.readLong(), d.readBoolean()
        )
        T_EDGE -> Edge(d.readLong(), d.readLong(), d.readLong(), d.readInt(), d.readFloat())
        T_IMAGE -> ImageEl(d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readStr())
        T_TABLE -> {
            val t = TableEl(d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readInt(), d.readInt(), d.readInt(), d.readFloat())
            if (ver >= 2) {
                val m = HashMap<Int, String>()
                repeat(d.readInt()) { m[d.readInt()] = d.readStr() }
                TableEl(t.id, t.x, t.y, t.w, t.h, t.rows, t.cols, t.color, t.width, m)
            } else t
        }
        else -> throw IOException("Phần tử không xác định")
    }
}
