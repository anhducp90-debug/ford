package com.inkboard.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ComponentsTest {
    private fun stroke(d: Document, x: Float, y: Float): Stroke =
        Stroke(d.nextId(), floatArrayOf(x, y, 0.5f, x + 30, y + 10, 0.5f), 0, 3f, PenType.PEN)

    @Test
    fun strokesClusterBySpace() {
        val d = Document()
        // Two sketches far apart: 3 strokes near (0,0), 2 strokes near (5000,5000).
        d.add(stroke(d, 0f, 0f), stroke(d, 40f, 5f), stroke(d, 80f, 0f), stroke(d, 5000f, 5000f), stroke(d, 5040f, 5010f))
        val sketches = Components.collect(d).filter { it.kind == ComponentKind.SKETCH }
        assertEquals(2, sketches.size)
        assertEquals(listOf(3, 2), sketches.map { it.count })
        assertTrue(sketches[1].box.l >= 5000f - 5f)
    }

    @Test
    fun mindmapIsOneEntryWithBranches() {
        val d = Document()
        val root = Node(d.nextId(), 0f, 0f, 120f, 50f, "Dự án", NodeKind.ROUND, 0, 20f, 0L, true)
        d.add(root)
        d.addMindmapChild(root, "A"); d.addMindmapChild(root, "B")
        d.add(TextEl(d.nextId(), 900f, 900f, "Ghi chú\ndòng 2", 20f, 0))
        val list = Components.collect(d)
        val mm = list.single { it.kind == ComponentKind.MINDMAP }
        assertEquals("Dự án", mm.label)
        assertEquals(3, mm.count)
        assertEquals(0, list.count { it.kind == ComponentKind.NODE })
        assertEquals("Ghi chú dòng 2", list.single { it.kind == ComponentKind.TEXT }.label)
    }

    @Test
    fun manyStrokesAreFast() {
        val d = Document()
        val els = ArrayList<Element>()
        for (i in 0 until 20000) els += stroke(d, (i % 200) * 300f, (i / 200) * 300f)
        d.add(els)
        val t = System.nanoTime()
        val sk = Components.collect(d).filter { it.kind == ComponentKind.SKETCH }
        val ms = (System.nanoTime() - t) / 1e6
        assertEquals(20000, sk.sumOf { it.count })
        assertTrue("took $ms ms", ms < 3000)
    }
}
