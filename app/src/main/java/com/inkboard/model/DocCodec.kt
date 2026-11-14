package com.inkboard.model

import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.util.zip.DeflaterOutputStream
import java.util.zip.InflaterInputStream

/**
 * `.inkb` binary format (v1):
 *   magic "INKB", int version, deflate stream of:
 *   background, camera, element count, elements (tagged), image count, images.
 */
object DocCodec {
    private const val MAGIC = 0x494E4B42 // "INKB"
    private const val VERSION = 1

    private const val T_STROKE = 1
    private const val T_SHAPE = 2
    private const val T_TEXT = 3
    private const val T_STICKY = 4
    private const val T_NODE = 5
    private const val T_EDGE = 6
    private const val T_IMAGE = 7
    private const val T_TABLE = 8

    fun write(doc: Document, out: OutputStream) {
        val head = DataOutputStream(out)
        head.writeInt(MAGIC)
        head.writeInt(VERSION)
        head.flush()
        val def = DeflaterOutputStream(out)
        val d = DataOutputStream(def.buffered())
        d.writeByte(doc.background.ordinal)
        d.writeFloat(doc.camScale); d.writeFloat(doc.camX); d.writeFloat(doc.camY)
        d.writeInt(doc.elements.size)
        for (e in doc.elements) writeElement(d, e)
        val usedKeys = doc.elements.filterIsInstance<ImageEl>().map { it.key }.toSet()
        val imgs = doc.images.filterKeys { it in usedKeys }
        d.writeInt(imgs.size)
        for ((k, v) in imgs) {
            d.writeStr(k); d.writeInt(v.size); d.write(v)
        }
        d.flush()
        def.finish()
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
        repeat(n) { readElement(d)?.let { doc.elements.add(it) } }
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

    private fun writeElement(d: DataOutputStream, e: Element) {
        when (e) {
            is Stroke -> {
                d.writeByte(T_STROKE); d.writeLong(e.id); d.writeInt(e.color); d.writeFloat(e.width)
                d.writeByte(e.pen.ordinal); d.writeFloats(e.pts)
            }
            is ShapeEl -> {
                d.writeByte(T_SHAPE); d.writeLong(e.id); d.writeByte(e.kind.ordinal); d.writeInt(e.color)
                d.writeFloat(e.width); d.writeInt(e.fill); d.writeFloats(e.geo)
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
            }
            is ImageEl -> {
                d.writeByte(T_IMAGE); d.writeLong(e.id); d.writeFloat(e.x); d.writeFloat(e.y)
                d.writeFloat(e.w); d.writeFloat(e.h); d.writeStr(e.key)
            }
        }
    }

    private fun readElement(d: DataInputStream): Element? = when (d.readUnsignedByte()) {
        T_STROKE -> {
            val id = d.readLong(); val color = d.readInt(); val w = d.readFloat()
            val pen = PenType.entries.getOrElse(d.readUnsignedByte()) { PenType.PEN }
            Stroke(id, d.readFloats(), color, w, pen)
        }
        T_SHAPE -> {
            val id = d.readLong(); val kind = ShapeKind.entries[d.readUnsignedByte()]
            val color = d.readInt(); val w = d.readFloat(); val fill = d.readInt()
            ShapeEl(id, kind, d.readFloats(), color, w, fill)
        }
        T_TEXT -> TextEl(d.readLong(), d.readFloat(), d.readFloat(), d.readStr(), d.readFloat(), d.readInt())
        T_STICKY -> StickyEl(d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readStr(), d.readInt(), d.readFloat())
        T_NODE -> Node(
            d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readStr(),
            NodeKind.entries[d.readUnsignedByte()], d.readInt(), d.readFloat(), d.readLong(), d.readBoolean()
        )
        T_EDGE -> Edge(d.readLong(), d.readLong(), d.readLong(), d.readInt(), d.readFloat())
        T_IMAGE -> ImageEl(d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readStr())
        T_TABLE -> TableEl(d.readLong(), d.readFloat(), d.readFloat(), d.readFloat(), d.readFloat(), d.readInt(), d.readInt(), d.readInt(), d.readFloat())
        else -> throw IOException("Phần tử không xác định")
    }
}
