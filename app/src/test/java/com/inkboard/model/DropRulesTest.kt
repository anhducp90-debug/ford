package com.inkboard.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class DropRulesTest {
    private fun rect(d: Document, l: Float, t: Float, r: Float, b: Float) =
        ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(l, t, r, t, r, b, l, b), 0, 2f)

    private fun drop(d: Document, ids: Set<Long>, primary: Long, x: Float, y: Float): DropRules.Action? =
        DropRules.resolve(d, ids, primary, x, y)

    private fun applyOne(d: Document, a: DropRules.Action) = d.batch { DropRules.apply(d, a) }

    @Test
    fun textIntoShapeBecomesFittedLabel() {
        val d = Document()
        val box = rect(d, 0f, 0f, 300f, 150f)
        val t = TextEl(d.nextId(), 500f, 500f, "Kế hoạch quý 4", 20f, 0xFF112233.toInt())
        d.add(box, t)
        val a = drop(d, setOf(t.id), t.id, 150f, 75f)
        assertTrue(a is DropRules.Action.LabelShape)
        applyOne(d, a!!)
        val s = d.byId(box.id) as ShapeEl
        assertEquals("Kế hoạch quý 4", s.label)
        assertEquals(0xFF112233.toInt(), s.labelColor)
        assertNull(d.byId(t.id))
        // Fitted inside the shape
        val f = s.labelFit
        assertTrue(f.size > 10f)
        assertTrue(f.lines.size * TextMetrics.lineHeight(f.size) <= s.labelBox.h + 0.01f)
        assertTrue(f.lines.all { TextMetrics.measure(it, f.size) <= s.labelBox.w + 0.01f })
        // Single undo restores both
        d.undo()
        assertEquals("", (d.byId(box.id) as ShapeEl).label)
        assertNotNull(d.byId(t.id))
    }

    @Test
    fun labelRefitsWhenShapeScales() {
        val d = Document()
        val s = rect(d, 0f, 0f, 200f, 100f).withLabel("Xin chào")
        d.add(s)
        val small = (d.byId(s.id) as ShapeEl).labelFit.size
        d.transform(listOf(s.id), Affine(2f, 0f, 0f))
        val big = (d.byId(s.id) as ShapeEl).labelFit.size
        assertEquals(small * 2, big, small * 0.05f)
    }

    @Test
    fun textIntoEllipseStaysInside() {
        val d = Document()
        val e = ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(0f, 0f, 100f, 60f, 0f), 0, 2f, label = "Một câu khá dài cần xuống dòng")
        val b = e.labelBox
        // Inner box corners must lie inside the ellipse.
        for ((x, y) in listOf(b.l to b.t, b.r to b.t, b.r to b.b, b.l to b.b)) {
            assertTrue((x / 100f) * (x / 100f) + (y / 60f) * (y / 60f) <= 1f)
        }
        assertTrue(e.labelFit.lines.size >= 2)
    }

    @Test
    fun textIntoFlowNodeReplacesPlaceholder() {
        val d = Document()
        val n = Node(d.nextId(), 0f, 0f, 100f, 40f, "Bước", NodeKind.ROUND, 0, 16f)
        val t = TextEl(d.nextId(), 300f, 300f, "Kiểm tra đơn hàng", 16f, 0)
        d.add(n, t)
        applyOne(d, drop(d, setOf(t.id), t.id, 50f, 20f)!!)
        assertEquals("Kiểm tra đơn hàng", (d.byId(n.id) as Node).text)
    }

    @Test
    fun textIntoTableCell() {
        val d = Document()
        val tb = TableEl(d.nextId(), 0f, 0f, 300f, 90f, 3, 3, 0, 1f)
        val t = TextEl(d.nextId(), 500f, 0f, "B2", 16f, 0)
        d.add(tb, t)
        val a = drop(d, setOf(t.id), t.id, 150f, 45f) as DropRules.Action.TableCell
        assertEquals(4, a.cell)
        applyOne(d, a)
        assertEquals("B2", (d.byId(tb.id) as TableEl).cells[4])
    }

    @Test
    fun textOntoMindmapNodeBecomesBranch() {
        val d = Document()
        val root = Node(d.nextId(), 0f, 0f, 120f, 50f, "Gốc", NodeKind.ROUND, 0, 20f, 0L, true)
        val t = TextEl(d.nextId(), 500f, 500f, "Ý từ chữ", 16f, 0)
        d.add(root, t)
        applyOne(d, drop(d, setOf(t.id), t.id, 60f, 25f)!!)
        val kids = d.children(root.id)
        assertEquals(1, kids.size)
        assertEquals("Ý từ chữ", kids[0].text)
        assertNull(d.byId(t.id))
    }

    @Test
    fun branchMovesToAnotherNode() {
        val d = Document()
        val root = Node(d.nextId(), 0f, 0f, 120f, 50f, "Gốc", NodeKind.ROUND, 0, 20f, 0L, true)
        d.add(root)
        val a = d.addMindmapChild(root, "A")
        val b = d.addMindmapChild(root, "B")
        val a1 = d.addMindmapChild(a, "A1")
        d.layoutMindmap(root.id)
        val bNow = d.byId(b.id) as Node
        val moving = setOf(a.id, a1.id)
        val act = drop(d, moving, a.id, bNow.x + 5, bNow.y + 5)
        assertTrue(act is DropRules.Action.Reparent)
        applyOne(d, act!!)
        assertEquals(b.id, (d.byId(a.id) as Node).parentId)
        assertEquals(a.id, (d.byId(a1.id) as Node).parentId)
        // Cannot drop a node into its own subtree
        val a1Now = d.byId(a1.id) as Node
        assertNull(drop(d, setOf(a.id, a1.id), a.id, a1Now.x + 2, a1Now.y + 2)?.takeIf { it is DropRules.Action.Reparent })
    }

    @Test
    fun strokesAttachToStickyAndMoveWithIt() {
        val d = Document()
        val st = StickyEl(d.nextId(), 0f, 0f, 200f, 200f, "", 0, 16f)
        val s = Stroke(d.nextId(), floatArrayOf(400f, 400f, 0.5f, 420f, 410f, 0.5f), 0, 2f, PenType.PEN)
        d.add(st, s)
        val a = drop(d, setOf(s.id), s.id, 100f, 100f)
        assertTrue(a is DropRules.Action.Attach)
        applyOne(d, a!!)
        assertEquals(st.id, d.containerOf(s.id))
        assertEquals(setOf(s.id), d.attachedTree(listOf(st.id)))
        // Deleting the note deletes attached ink
        d.remove(listOf(st.id))
        assertNull(d.byId(s.id))
        d.undo()
        assertNotNull(d.byId(s.id))
        assertEquals(st.id, d.containerOf(s.id))
    }

    @Test
    fun dragOutDetaches() {
        val d = Document()
        val box = rect(d, 0f, 0f, 200f, 200f)
        val s = Stroke(d.nextId(), floatArrayOf(50f, 50f, 0.5f, 60f, 60f, 0.5f), 0, 2f, PenType.PEN)
        d.add(box, s)
        d.setContainer(s.id, box.id)
        val a = drop(d, setOf(s.id), s.id, 900f, 900f)
        assertTrue(a is DropRules.Action.Detach)
        applyOne(d, a!!)
        assertEquals(0L, d.containerOf(s.id))
    }

    @Test
    fun noCycles() {
        val d = Document()
        val a = rect(d, 0f, 0f, 100f, 100f); val b = rect(d, 10f, 10f, 50f, 50f)
        d.add(a, b)
        d.setContainer(b.id, a.id)
        d.setContainer(a.id, b.id) // would create a cycle: ignored
        assertEquals(0L, d.containerOf(a.id))
    }

    @Test
    fun enclosingContainerFound() {
        val d = Document()
        val st = StickyEl(d.nextId(), 0f, 0f, 200f, 200f, "", 0, 16f)
        d.add(st)
        assertEquals(st.id, DropRules.enclosingContainer(d, Box(20f, 20f, 80f, 60f))?.id)
        assertNull(DropRules.enclosingContainer(d, Box(150f, 150f, 260f, 260f)))
    }

    @Test
    fun codecV2KeepsLabelsCellsAttachments() {
        val d = Document()
        val box = rect(d, 0f, 0f, 100f, 100f).withLabel("Nhãn", 7)
        val tb = TableEl(d.nextId(), 0f, 200f, 100f, 60f, 2, 2, 0, 1f).withCell(3, "ô")
        val s = Stroke(d.nextId(), floatArrayOf(10f, 10f, 0.5f), 0, 2f, PenType.PEN)
        d.add(box, tb, s)
        d.setContainer(s.id, box.id)
        val bytes = ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray()
        val r = DocCodec.read(ByteArrayInputStream(bytes))
        assertEquals("Nhãn", (r.byId(box.id) as ShapeEl).label)
        assertEquals(7, (r.byId(box.id) as ShapeEl).labelColor)
        assertEquals("ô", (r.byId(tb.id) as TableEl).cells[3])
        assertEquals(box.id, r.containerOf(s.id))
    }

    @Test
    fun wrapAndFit() {
        val lines = TextFit.wrap("một hai ba bốn năm", 10f, 60f)
        assertTrue(lines.size > 1)
        lines.forEach { assertTrue(TextMetrics.measure(it, 10f) <= 60f) }
        val f = TextFit.fit("A", 100f, 100f)
        assertTrue(f.size > 50f) // a single letter grows to fill the box
        val capped = TextFit.fit("A", 100f, 100f, maxSize = 20f)
        assertEquals(20f, capped.size, 0.01f)
    }
}
