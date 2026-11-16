package com.inkboard.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class SearchTest {
    private fun word(d: Document, x: Float, y: Float) = (0 until 4).map { k ->
        Stroke(d.nextId(), floatArrayOf(x + k * 12f, y, 0.5f, x + k * 12f + 8f, y + 14f, 0.5f), 0, 2f, PenType.PEN)
    }

    @Test
    fun findsTypedTextLabelsNotesCellsAccentInsensitive() {
        val d = Document()
        d.add(TextEl(d.nextId(), 0f, 0f, "Kế hoạch quý 4", 20f, 0))
        d.add(ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(300f, 0f, 50f, 50f, 0f), 0, 2f, label = "Ngân sách"))
        d.add(StickyEl(d.nextId(), 0f, 200f, 100f, 100f, "Gọi khách hàng", 0, 14f))
        d.add(TableEl(d.nextId(), 0f, 400f, 200f, 60f, 2, 2, 0, 1f, mapOf(3 to "Hạn chót")))
        assertEquals(1, Search.find(d, "ke hoach").size)
        assertEquals(1, Search.find(d, "NGAN SACH").size)
        assertEquals(Search.Kind.NOTE, Search.find(d, "khach").single().kind)
        assertEquals(Search.Kind.CELL, Search.find(d, "han chot").single().kind)
        assertTrue(Search.find(d, "   ").isEmpty())
    }

    @Test
    fun recognisedHandwritingIsSearchable() {
        val d = Document()
        val w1 = word(d, 0f, 0f); val w2 = word(d, 2000f, 0f)
        d.add(w1); d.add(w2)
        val clusters = Search.inkClusters(d)
        assertEquals(2, clusters.size)
        val k1 = clusters.first { c -> c.second.any { it.id == w1[0].id } }.first
        d.inkText[k1] = "Ý tưởng mới"
        val hit = Search.find(d, "y tuong").single()
        assertEquals(Search.Kind.INK, hit.kind)
        assertEquals(w1.map { it.id }.toSet(), hit.ids.toSet())
        assertTrue(Search.plainText(d).contains("Ý tưởng mới"))
    }

    @Test
    fun clusterKeyIsOrderIndependentAndChangesWithContent() {
        assertEquals(Search.clusterKey(listOf(3L, 1L, 2L)), Search.clusterKey(listOf(1L, 2L, 3L)))
        assertTrue(Search.clusterKey(listOf(1L, 2L)) != Search.clusterKey(listOf(1L, 2L, 3L)))
    }

    @Test
    fun indexSurvivesSaveAndLoad() {
        val d = Document(); d.add(word(d, 0f, 0f))
        val k = Search.inkClusters(d).single().first
        d.inkText[k] = "xin chào"
        val bytes = ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray()
        val r = DocCodec.read(ByteArrayInputStream(bytes))
        assertEquals("xin chào", r.inkText[k])
        assertEquals(1, Search.find(r, "xin chao").size)
    }

    @Test
    fun highlighterAndHugeDrawingsAreNotIndexedAsWords() {
        val d = Document()
        d.add(Stroke(d.nextId(), floatArrayOf(0f, 0f, 0.5f, 100f, 0f, 0.5f), 0, 20f, PenType.HIGHLIGHTER))
        d.add(Stroke(d.nextId(), floatArrayOf(0f, 0f, 0.5f, 20000f, 20000f, 0.5f), 0, 2f, PenType.PEN))
        assertTrue(Search.inkClusters(d).all { c -> c.second.none { it.pen == PenType.HIGHLIGHTER } })
    }
}
