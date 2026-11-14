package com.inkboard.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class DocumentTest {

    private fun stroke(doc: Document, vararg xy: Float): Stroke {
        val pts = FloatArray(xy.size / 2 * 3)
        for (i in 0 until xy.size / 2) { pts[3 * i] = xy[2 * i]; pts[3 * i + 1] = xy[2 * i + 1]; pts[3 * i + 2] = 0.5f }
        return Stroke(doc.nextId(), pts, 0xFF000000.toInt(), 4f, PenType.PEN)
    }

    @Test
    fun addUndoRedo() {
        val d = Document()
        val a = stroke(d, 0f, 0f, 10f, 10f)
        val b = stroke(d, 20f, 20f, 30f, 30f)
        d.add(a); d.add(b)
        assertEquals(2, d.elements.size)
        d.undo()
        assertEquals(listOf(a.id), d.elements.map { it.id })
        d.redo()
        assertEquals(listOf(a.id, b.id), d.elements.map { it.id })
        assertFalse(d.canRedo)
    }

    @Test
    fun removeRestoresOrderOnUndo() {
        val d = Document()
        val els = (0 until 5).map { stroke(d, it * 10f, 0f, it * 10f + 5, 5f) }
        d.add(els)
        d.remove(listOf(els[1].id, els[3].id))
        assertEquals(listOf(0, 2, 4).map { els[it].id }, d.elements.map { it.id })
        d.undo()
        assertEquals(els.map { it.id }, d.elements.map { it.id })
    }

    @Test
    fun transformAndUndo() {
        val d = Document()
        val s = stroke(d, 0f, 0f, 10f, 0f)
        d.add(s)
        d.transform(listOf(s.id), Affine.translate(100f, 50f))
        val moved = d.byId(s.id) as Stroke
        assertEquals(100f, moved.pts[0], 1e-4f)
        assertEquals(50f, moved.pts[1], 1e-4f)
        d.undo()
        assertEquals(0f, (d.byId(s.id) as Stroke).pts[0], 1e-4f)
    }

    @Test
    fun scaleAroundKeepsFixedPoint() {
        val a = Affine.scaleAround(2f, 10f, 10f)
        assertEquals(10f, a.x(10f), 1e-5f)
        assertEquals(30f, a.x(20f), 1e-5f)
    }

    @Test
    fun reorderFrontAndBack() {
        val d = Document()
        val els = (0 until 4).map { stroke(d, 0f, 0f, 1f, 1f) }
        d.add(els)
        d.reorder(listOf(els[0].id), front = true)
        assertEquals(els[0].id, d.elements.last().id)
        d.reorder(listOf(els[3].id), front = false)
        assertEquals(els[3].id, d.elements.first().id)
        d.undo(); d.undo()
        assertEquals(els.map { it.id }, d.elements.map { it.id })
    }

    @Test
    fun swapIsOneUndoStep() {
        val d = Document()
        val s = stroke(d, 0f, 0f, 10f, 10f)
        d.add(s)
        val t = TextEl(d.nextId(), 0f, 0f, "Xin chào", 20f, 0)
        d.swap(listOf(s.id), listOf(t))
        assertEquals(listOf(t.id), d.elements.map { it.id })
        d.undo()
        assertEquals(listOf(s.id), d.elements.map { it.id })
    }

    @Test
    fun lassoSelectsStrokeMostlyInside() {
        val d = Document()
        val inside = stroke(d, 10f, 10f, 20f, 20f, 30f, 30f)
        val outside = stroke(d, 200f, 200f, 210f, 210f)
        val poly = floatArrayOf(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f)
        assertTrue(inside.inLasso(poly))
        assertFalse(outside.inLasso(poly))
    }

    @Test
    fun hitTestStrokeAndShapes() {
        val d = Document()
        val s = stroke(d, 0f, 0f, 100f, 0f)
        assertTrue(s.hit(50f, 3f, 2f, false))
        assertFalse(s.hit(50f, 30f, 2f, false))
        val circle = ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(0f, 0f, 50f, 50f, 0f), 0, 2f)
        assertTrue(circle.hit(50f, 0f, 3f, false))
        assertFalse(circle.hit(0f, 0f, 3f, false))
        assertTrue(circle.hit(0f, 0f, 3f, true))
    }

    @Test
    fun deletingMindmapNodeRemovesSubtreeAndEdges() {
        val d = Document()
        val root = Node(d.nextId(), 0f, 0f, 120f, 50f, "Gốc", NodeKind.ROUND, 0, 20f, 0L, true)
        d.add(root)
        val c1 = d.addMindmapChild(root, "A")
        val c2 = d.addMindmapChild(c1, "B")
        val other = Node(d.nextId(), 500f, 0f, 100f, 50f, "X", NodeKind.RECT, 0, 20f)
        d.add(other)
        val edge = Edge(d.nextId(), c2.id, other.id, 0, 2f)
        d.add(edge)
        d.remove(listOf(c1.id))
        assertNotNull(d.byId(root.id))
        assertNull(d.byId(c1.id))
        assertNull(d.byId(c2.id))
        assertNull(d.byId(edge.id))
        assertNotNull(d.byId(other.id))
        d.undo()
        assertEquals(5, d.elements.size)
    }

    @Test
    fun mindmapLayoutPutsChildrenRightOfParent() {
        val d = Document()
        val root = Node(d.nextId(), 0f, 0f, 120f, 50f, "Gốc", NodeKind.ROUND, 0, 20f, 0L, true)
        d.add(root)
        repeat(3) { d.addMindmapChild(d.byId(root.id) as Node, "Nhánh $it") }
        d.layoutMindmap(root.id)
        val r = d.byId(root.id) as Node
        val kids = d.children(root.id).sortedBy { it.y }
        assertEquals(3, kids.size)
        kids.forEach { assertTrue(it.x > r.x + r.w) }
        for (i in 1 until kids.size) assertTrue(kids[i].y >= kids[i - 1].y + kids[i - 1].h)
    }

    @Test
    fun codecRoundTrip() {
        val d = Document()
        d.background = Background.GRID
        d.camScale = 2.5f; d.camX = -10f; d.camY = 33f
        val s = stroke(d, 0f, 0f, 10f, 10f, 20f, 5f)
        val sh = ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(0f, 0f, 10f, 0f, 5f, 8f), 1, 2f, 3)
        val t = TextEl(d.nextId(), 1f, 2f, "Tiếng Việt có dấu ✓\nDòng 2", 18f, 7)
        val st = StickyEl(d.nextId(), 5f, 5f, 100f, 100f, "Ghi chú", 9, 16f)
        val n1 = Node(d.nextId(), 0f, 0f, 100f, 40f, "A", NodeKind.DIAMOND, 4, 16f, 0L, true)
        val n2 = Node(d.nextId(), 200f, 0f, 100f, 40f, "B", NodeKind.ELLIPSE, 4, 16f, n1.id, true)
        val e = Edge(d.nextId(), n1.id, n2.id, 2, 3f)
        val img = ImageEl(d.nextId(), 0f, 0f, 50f, 40f, "img1")
        val tab = TableEl(d.nextId(), 0f, 0f, 300f, 120f, 3, 4, 5, 2f)
        d.images["img1"] = byteArrayOf(1, 2, 3, 4)
        d.images["unused"] = byteArrayOf(9)
        d.add(s, sh, t, st, n1, n2, e, img, tab)

        val bytes = ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray()
        val r = DocCodec.read(ByteArrayInputStream(bytes))

        assertEquals(Background.GRID, r.background)
        assertEquals(2.5f, r.camScale, 0f)
        assertEquals(d.elements.map { it.id to it::class }, r.elements.map { it.id to it::class })
        assertEquals("Tiếng Việt có dấu ✓\nDòng 2", (r.byId(t.id) as TextEl).text)
        assertEquals(n1.id, (r.byId(n2.id) as Node).parentId)
        assertTrue((r.byId(s.id) as Stroke).pts.contentEquals(s.pts))
        assertTrue(r.images["img1"]!!.contentEquals(byteArrayOf(1, 2, 3, 4)))
        assertFalse(r.images.containsKey("unused"))
        assertEquals(4, (r.byId(tab.id) as TableEl).cols)
        assertTrue(r.nextId() > tab.id)
    }

    @Test
    fun hitTopReturnsTopmost() {
        val d = Document()
        val a = StickyEl(d.nextId(), 0f, 0f, 100f, 100f, "", 0, 10f)
        val b = StickyEl(d.nextId(), 50f, 50f, 100f, 100f, "", 0, 10f)
        d.add(a, b)
        assertEquals(b.id, d.hitTop(75f, 75f, 1f)?.id)
        assertEquals(a.id, d.hitTop(10f, 10f, 1f)?.id)
        assertNull(d.hitTop(500f, 500f, 1f))
    }

    @Test
    fun tableResizeKeepsCellSize() {
        val t = TableEl(1, 0f, 0f, 300f, 90f, 3, 3, 0, 1f)
        val r = t.resized(4, 5)
        assertEquals(500f, r.w, 1e-3f)
        assertEquals(120f, r.h, 1e-3f)
    }

    @Test
    fun snapshotIsIndependent() {
        val d = Document()
        d.add(stroke(d, 0f, 0f, 1f, 1f))
        val snap = d.snapshot()
        d.add(stroke(d, 2f, 2f, 3f, 3f))
        assertEquals(1, snap.elements.size)
    }
}
