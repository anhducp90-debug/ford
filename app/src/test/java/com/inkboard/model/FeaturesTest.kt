package com.inkboard.model

import com.inkboard.data.LibraryStore
import com.inkboard.export.SvgExport
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import javax.xml.parsers.DocumentBuilderFactory

class FeaturesTest {
    @get:Rule val tmp = TemporaryFolder()

    private fun zigzag(x0: Float, y: Float, w: Float, h: Float, n: Int): FloatArray {
        val out = ArrayList<Float>()
        for (i in 0..n) { out += x0 + (if (i % 2 == 0) 0f else w); out += y + i * h / n }
        // densify
        val d = ArrayList<Float>()
        for (i in 0 until out.size / 2 - 1) for (k in 0 until 8) {
            val t = k / 8f
            d += out[2 * i] + (out[2 * i + 2] - out[2 * i]) * t; d += out[2 * i + 1] + (out[2 * i + 3] - out[2 * i + 1]) * t
        }
        return d.toFloatArray()
    }

    @Test
    fun scratchOutDetection() {
        assertTrue(InkGestures.isScratch(zigzag(0f, 0f, 100f, 30f, 8)))
        val straight = FloatArray(40) { k -> if (k % 2 == 0) k * 5f else 0f }
        assertFalse(InkGestures.isScratch(straight))
        val circle = FloatArray(80) { k -> val a = (k / 2) / 40.0 * 2 * Math.PI; if (k % 2 == 0) (100 * Math.cos(a)).toFloat() else (100 * Math.sin(a)).toFloat() }
        assertFalse(InkGestures.isScratch(circle))
    }

    @Test
    fun scratchOutTargetsCrossedWords() {
        val d = Document()
        val word = Stroke(d.nextId(), floatArrayOf(10f, 10f, 0.5f, 60f, 15f, 0.5f, 90f, 12f, 0.5f), 0, 3f, PenType.PEN)
        val far = Stroke(d.nextId(), floatArrayOf(500f, 500f, 0.5f, 560f, 505f, 0.5f), 0, 3f, PenType.PEN)
        d.add(word, far)
        val z = zigzag(0f, 0f, 100f, 25f, 8)
        assertEquals(listOf(word.id), InkGestures.scratchTargets(d, z, 4f))
    }

    @Test
    fun handDrawnConnector() {
        val d = Document()
        val a = Node(d.nextId(), 0f, 0f, 100f, 50f, "A", NodeKind.ROUND, 0, 16f)
        val b = Node(d.nextId(), 300f, 0f, 100f, 50f, "B", NodeKind.ROUND, 0, 16f)
        d.add(a, b)
        val line = FloatArray(22) { k -> if (k % 2 == 0) 50f + (k / 2) * 30f else 25f + (k / 2 % 2) * 3f }
        val pair = InkGestures.connectorFor(d, line, 6f)
        assertNotNull(pair); assertEquals(a.id, pair!!.first.id); assertEquals(b.id, pair.second.id)
        // Scribble between nodes is not a connector
        val scribble = zigzag(50f, 25f, 300f, 2f, 12)
        assertNull(InkGestures.connectorFor(d, scribble.copyOf(scribble.size - 2) + floatArrayOf(350f, 25f), 6f))
    }

    @Test
    fun collapseHidesDescendants() {
        val d = Document()
        val root = Node(d.nextId(), 0f, 0f, 100f, 40f, "R", NodeKind.ROUND, 0, 16f, 0L, true)
        d.add(root)
        val a = d.addMindmapChild(root, "A"); val a1 = d.addMindmapChild(a, "A1")
        d.toggleCollapsed(a.id)
        assertEquals(setOf(a1.id), d.hiddenIds())
        d.toggleCollapsed(a.id)
        assertTrue(d.hiddenIds().isEmpty())
    }

    @Test
    fun codecV4ExtrasRoundTrip() {
        val d = Document()
        val a = Node(d.nextId(), 0f, 0f, 100f, 40f, "A", NodeKind.ROUND, 0, 16f)
        val b = Node(d.nextId(), 300f, 0f, 100f, 40f, "B", NodeKind.ROUND, 0, 16f)
        val e = Edge(d.nextId(), a.id, b.id, 0, 2f, "Có")
        d.add(a, b, e)
        d.locked += a.id; d.collapsed += b.id
        d.bookmarks += Bookmark("Góc trái", 2f, 10f, 20f)
        val bytes = ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray()
        val r = DocCodec.read(ByteArrayInputStream(bytes))
        assertEquals("Có", (r.byId(e.id) as Edge).label)
        assertEquals(setOf(a.id), r.locked)
        assertEquals(setOf(b.id), r.collapsed)
        assertEquals(d.bookmarks, r.bookmarks)
    }

    @Test
    fun lockedElementsAreNotHitOrScratched() {
        val d = Document()
        val s = StickyEl(d.nextId(), 0f, 0f, 100f, 100f, "", 0, 16f)
        d.add(s); d.locked += s.id
        assertNull(d.hitTop(50f, 50f, 1f))
        assertNotNull(d.hitTop(50f, 50f, 1f, includeLocked = true))
    }

    @Test
    fun svgExportIsWellFormed() {
        val d = Document()
        d.add(
            Stroke(d.nextId(), floatArrayOf(0f, 0f, 0.5f, 50f, 20f, 0.8f, 100f, 0f, 0.4f), 0xFF112233.toInt(), 4f, PenType.PEN, taper = true),
            Stroke(d.nextId(), floatArrayOf(0f, 50f, 0.5f, 100f, 50f, 0.5f), 0xFFFFEB3B.toInt(), 20f, PenType.HIGHLIGHTER),
            ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(200f, 50f, 40f, 30f, 0.3f), 0, 2f, label = "A & <B>"),
            TextEl(d.nextId(), 0f, 120f, "Xin chào \"bạn\"", 18f, 0),
            StickyEl(d.nextId(), 300f, 0f, 120f, 120f, "Ghi chú dài cần xuống dòng", 0xFFFFF59D.toInt(), 14f),
            TableEl(d.nextId(), 0f, 200f, 200f, 60f, 2, 2, 0, 1f, mapOf(0 to "x")),
            ImageEl(d.nextId(), 500f, 0f, 40f, 40f, "k"),
        )
        d.images["k"] = byteArrayOf(1, 2, 3)
        val a = Node(d.nextId(), 0f, 400f, 100f, 40f, "A", NodeKind.DIAMOND, 0, 16f)
        val b = Node(d.nextId(), 300f, 400f, 100f, 40f, "B", NodeKind.ELLIPSE, 0, 16f)
        d.add(a, b, Edge(d.nextId(), a.id, b.id, 0, 2f, "Có"))
        val svg = SvgExport.export(d, d.contentBounds())
        val xml = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(ByteArrayInputStream(svg.toByteArray()))
        assertEquals("svg", xml.documentElement.tagName)
        assertTrue(xml.getElementsByTagName("text").length >= 5)
        assertTrue(svg.contains("&amp;") && svg.contains("&lt;B&gt;")) // escaped (label may wrap)
        assertTrue(svg.contains("data:image/jpeg;base64,"))
    }

    @Test
    fun shapeLibraryBuildsEverything() {
        assertTrue("entries: ${ShapeLibrary.entries.size}", ShapeLibrary.entries.size >= 50)
        val d = Document()
        for (e in ShapeLibrary.entries) {
            val els = e.build(d, 0f, 0f, 100f, 0xFF000000.toInt(), 2f)
            assertTrue(e.name, els.isNotEmpty())
            for (el in els) assertFalse(e.name, el.bounds.isEmpty)
        }
        assertTrue(ShapeLibrary.entries.first { it.name == "Quyết định" }.matches("quyet dinh"))
        assertTrue(ShapeLibrary.entries.first { it.name == "Ngôi sao 5 cánh" }.matches("sao"))
        assertEquals(4, ShapeLibrary.entries.count { it.matches("mui ten khoi") })
    }

    @Test
    fun personalLibraryRoundTripKeepsRelations() {
        val d = Document()
        val box = ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(0f, 0f, 100f, 0f, 100f, 100f, 0f, 100f), 0, 2f)
        val ink = Stroke(d.nextId(), floatArrayOf(10f, 10f, 0.5f, 20f, 20f, 0.5f), 0, 2f, PenType.PEN)
        val a = Node(d.nextId(), 200f, 0f, 80f, 40f, "A", NodeKind.ROUND, 0, 16f)
        val b = Node(d.nextId(), 400f, 0f, 80f, 40f, "B", NodeKind.ROUND, 0, 16f)
        val e = Edge(d.nextId(), a.id, b.id, 0, 2f)
        d.add(box, ink, a, b, e); d.setContainer(ink.id, box.id)
        val store = LibraryStore(tmp.root)
        val item = store.save(d, listOf(box.id, ink.id, a.id, b.id, e.id), "Mẫu A")
        assertEquals(1, store.list().size)
        val target = Document()
        val out = LibraryStore.instantiate(store.load(item), target, 1000f, 1000f)
        assertEquals(5, out.size)
        val newBox = out[0]; val newInk = out[1]
        assertEquals(newBox.id, target.containerOf(newInk.id))
        val ne = out.filterIsInstance<Edge>().single()
        assertTrue(target.byId(ne.from) is Node && target.byId(ne.to) is Node)
        assertEquals(1000f, target.contentBounds().cx, 1f)
    }
}
