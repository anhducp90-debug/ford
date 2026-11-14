package com.inkboard.audit

import android.graphics.Bitmap
import android.graphics.Canvas
import android.os.Looper
import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import androidx.test.core.app.ApplicationProvider
import com.inkboard.canvas.InkCanvasView
import com.inkboard.canvas.Tool
import com.inkboard.export.SvgExport
import com.inkboard.model.*
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.GraphicsMode
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.time.Duration
import kotlin.math.cos
import kotlin.math.sin

/**
 * Real user flows driven through the live canvas (stylus/finger events, the
 * hold-to-shape timer, autosave codec, export). Each test is one scenario.
 */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class ScenarioAuditTest {
    private lateinit var view: InkCanvasView
    private lateinit var doc: Document
    private var t0 = 0L
    private var edits = 0
    private val editRequests = ArrayList<Element>()
    private data class P(val id: Int, val x: Float, val y: Float, val tool: Int)

    private fun ev(action: Int, pts: List<P>, actionIndex: Int = 0): MotionEvent {
        val props = pts.map { p -> MotionEvent.PointerProperties().apply { id = p.id; toolType = p.tool } }.toTypedArray()
        val coords = pts.map { p -> MotionEvent.PointerCoords().apply { x = p.x; y = p.y; pressure = 0.6f } }.toTypedArray()
        val a = if (action == MotionEvent.ACTION_POINTER_DOWN || action == MotionEvent.ACTION_POINTER_UP)
            action or (actionIndex shl MotionEvent.ACTION_POINTER_INDEX_SHIFT) else action
        t0 += 16
        return MotionEvent.obtain(0, t0, a, pts.size, props, coords, 0, 0, 1f, 1f, 0, 0, 0, 0)
    }
    private fun send(e: MotionEvent) { view.dispatchTouchEvent(e); e.recycle() }
    private val S = MotionEvent.TOOL_TYPE_STYLUS
    private val F = MotionEvent.TOOL_TYPE_FINGER

    /** Pen stroke through screen points; [hold] keeps the pen still at the end (shape snapping). */
    private fun pen(pts: List<Pair<Float, Float>>, hold: Boolean = false) {
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, pts[0].first, pts[0].second, S))))
        for (i in 1 until pts.size) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, pts[i].first, pts[i].second, S))))
        if (hold) shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(700))
        val l = pts.last()
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, l.first, l.second, S))))
    }
    private fun fingerTap(x: Float, y: Float) {
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, x, y, F)))); send(ev(MotionEvent.ACTION_UP, listOf(P(0, x, y, F))))
    }
    private fun fingerDrag(x0: Float, y0: Float, x1: Float, y1: Float) {
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, x0, y0, F))))
        for (k in 1..12) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, x0 + (x1 - x0) * k / 12, y0 + (y1 - y0) * k / 12, F))))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, x1, y1, F))))
    }
    private fun seg(x0: Float, y0: Float, x1: Float, y1: Float, n: Int = 16, wobble: Float = 1.5f) =
        (0..n).map { i -> (x0 + (x1 - x0) * i / n + if (i % 2 == 0) wobble else -wobble) to (y0 + (y1 - y0) * i / n + if (i % 3 == 0) wobble else -wobble) }
    /** Hand-drawn rectangle (screen px) as one stroke, slightly imperfect. */
    private fun rectStroke(l: Float, t: Float, r: Float, b: Float) =
        seg(l, t, r, t + 3) + seg(r, t + 3, r - 2, b).drop(1) + seg(r - 2, b, l + 2, b - 2).drop(1) + seg(l + 2, b - 2, l + 4, t + 6).drop(1)
    private fun circleStroke(cx: Float, cy: Float, r: Float) = (0..48).map { i -> val a = i / 48.0 * 2 * Math.PI * 1.02; (cx + r * cos(a).toFloat()) to (cy + r * sin(a).toFloat()) }
    private fun sx(w: Float) = w * view.scale + view.tx
    private fun sy(w: Float) = w * view.scale + view.ty
    private fun edges() = doc.elements.filterIsInstance<Edge>()

    @Before
    fun setUp() {
        view = InkCanvasView(ApplicationProvider.getApplicationContext())
        doc = Document()
        view.doc = doc
        view.showMinimap = false
        view.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(2000, View.MeasureSpec.EXACTLY))
        view.layout(0, 0, 1080, 2000)
        view.listener = object : InkCanvasView.Listener {
            override fun onSelectionChanged(screenRect: android.graphics.RectF?, ids: Set<Long>) {}
            override fun onDocChanged() { edits++ }
            override fun onCameraChanged(scale: Float) {}
            override fun onEditRequest(el: Element, wx: Float, wy: Float) { editRequests += el }
        }
        t0 = SystemClock.uptimeMillis()
    }

    private fun render() { val b = Bitmap.createBitmap(1080, 2000, Bitmap.Config.ARGB_8888); view.draw(Canvas(b)) }

    // ------------------------------------------------------------- drawing

    @Test fun s01_freehandStrokeIsSaved() { pen(seg(100f, 100f, 500f, 300f)); assertTrue(doc.elements.single() is Stroke) }

    @Test fun s02_handRectWithHoldBecomesRectangle() {
        pen(rectStroke(100f, 300f, 400f, 500f), hold = true)
        val s = doc.elements.single() as ShapeEl
        assertEquals(ShapeKind.POLYGON, s.kind); assertEquals(8, s.geo.size)
    }

    @Test fun s03_handCircleWithHoldBecomesEllipse() {
        pen(circleStroke(500f, 800f, 150f), hold = true)
        assertEquals(ShapeKind.ELLIPSE, (doc.elements.single() as ShapeEl).kind)
    }

    @Test fun s04_straightLineWithHoldBecomesLine() {
        pen(seg(100f, 1000f, 700f, 1010f), hold = true)
        assertEquals(ShapeKind.LINE, (doc.elements.single() as ShapeEl).kind)
    }

    // ----------------------------------------------------------- connectors

    private fun twoHandRects() {
        pen(rectStroke(100f, 300f, 400f, 500f), hold = true)
        pen(rectStroke(650f, 300f, 950f, 500f), hold = true)
        assertEquals(2, doc.elements.count { it is ShapeEl })
    }

    @Test fun s05_lineBetweenHandRectsBecomesConnector() {
        twoHandRects()
        pen(seg(390f, 400f, 660f, 405f))
        val e = edges().single()
        assertEquals(setOf(doc.elements[0].id, doc.elements[1].id), setOf(e.from, e.to))
        assertEquals(0, doc.elements.count { it is Stroke })
    }

    @Test fun s06_heldStraightLineBetweenRectsBecomesConnector() {
        twoHandRects()
        pen(seg(390f, 400f, 660f, 405f), hold = true)
        assertEquals(1, edges().size)
        assertEquals(0, doc.elements.count { it is ShapeEl && it.kind == ShapeKind.LINE })
    }

    @Test fun s07_fingerHeldStraightLineBetweenRectsBecomesConnector() {
        twoHandRects()
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(5, 1000f, 1900f, F))))
        val l = seg(390f, 400f, 660f, 410f)
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(5, 1000f, 1900f, F), P(0, l[0].first, l[0].second, S)), actionIndex = 1))
        for (p in l.drop(1)) send(ev(MotionEvent.ACTION_MOVE, listOf(P(5, 1000f, 1900f, F), P(0, p.first, p.second, S))))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(5, 1000f, 1900f, F), P(0, l.last().first, l.last().second, S)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_UP, listOf(P(5, 1000f, 1900f, F))))
        assertEquals(1, edges().size)
    }

    @Test fun s08_lineToEmptySpaceStaysInk() {
        twoHandRects()
        pen(seg(390f, 400f, 560f, 900f))
        assertEquals(0, edges().size)
        assertEquals(1, doc.elements.count { it is Stroke })
    }

    @Test fun s09_lineBetweenUnsnappedHandBoxesBecomesConnector() {
        pen(rectStroke(100f, 300f, 400f, 500f)) // no hold: stays ink, but is a closed loop
        pen(rectStroke(650f, 300f, 950f, 500f))
        pen(seg(390f, 400f, 660f, 405f))
        assertEquals(1, edges().size)
    }

    @Test fun s10_libraryRectanglesConnect() {
        val lib = ShapeLibrary.entries.first { it.name == "Hình chữ nhật" }
        doc.add(lib.build(doc, -300f, 0f, 80f, 0, 2f)); doc.add(lib.build(doc, 300f, 0f, 80f, 0, 2f)); view.onDocMutated()
        pen(seg(sx(-200f), sy(0f), sx(200f), sy(5f)))
        assertEquals(1, edges().size)
    }

    @Test fun s11_stickyToImageConnects() {
        doc.add(StickyEl(doc.nextId(), -400f, -100f, 200f, 200f, "A", 0xFFFFF59D.toInt(), 16f))
        doc.add(ImageEl(doc.nextId(), 200f, -100f, 200f, 200f, "k")); doc.images["k"] = byteArrayOf(1)
        view.onDocMutated()
        pen(seg(sx(-210f), sy(0f), sx(210f), sy(0f)))
        assertEquals(1, edges().size)
    }

    @Test fun s12_connectorFollowsMovedShape() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val e = edges().single()
        val before = doc.edgeEnds(e)!!.copyOf()
        val target = doc.byId(e.to)!!
        view.tool = Tool.SELECT
        view.select(listOf(target.id))
        val r = view.selectionScreenRect()!!
        fingerDrag(r.centerX(), r.centerY(), r.centerX(), r.centerY() + 400f)
        val after = doc.edgeEnds(e)!!
        assertTrue("end moved: ${before.toList()} -> ${after.toList()}", after[3] - before[3] > 100f)
        render()
    }

    @Test fun s13_deletingShapeRemovesConnectorAndUndoRestores() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val a = doc.elements.first { it is ShapeEl }
        doc.remove(listOf(a.id))
        assertEquals(0, edges().size)
        doc.undo()
        assertEquals(1, edges().size)
    }

    @Test fun s14_lineInsideFrameConnectsInnerBoxes() {
        doc.add(ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(-600f, -400f, 600f, -400f, 600f, 400f, -600f, 400f), 0, 2f))
        val a = ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(-400f, -80f, -200f, -80f, -200f, 80f, -400f, 80f), 0, 2f)
        val b = ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(200f, -80f, 400f, -80f, 400f, 80f, 200f, 80f), 0, 2f)
        doc.add(a, b); view.onDocMutated()
        pen(seg(sx(-210f), sy(0f), sx(210f), sy(0f)))
        val e = edges().single()
        assertEquals(setOf(a.id, b.id), setOf(e.from, e.to))
    }

    @Test fun s15_lineWithinOneShapeStaysInk() {
        twoHandRects()
        pen(seg(150f, 350f, 350f, 450f))
        assertEquals(0, edges().size)
    }

    @Test fun s16_undoConnectorIsOneStep() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        doc.undo()
        assertEquals(0, edges().size); assertEquals(0, doc.elements.count { it is Stroke })
    }

    @Test fun s17_connectorAnchorsSitOnOutlines() {
        val c1 = ShapeEl(doc.nextId(), ShapeKind.ELLIPSE, floatArrayOf(-300f, 0f, 100f, 60f, 0f), 0, 2f)
        val c2 = ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(300f, -100f, 400f, 0f, 300f, 100f, 200f, 0f), 0, 2f) // diamond
        doc.add(c1, c2); doc.add(Edge(doc.nextId(), c1.id, c2.id, 0, 2f))
        val ends = doc.edgeEnds(edges().single())!!
        assertEquals(-200f, ends[0], 0.5f)   // right edge of the ellipse
        assertEquals(200f, ends[2], 0.5f)    // left vertex of the diamond
    }

    @Test fun s18_connectButtonWorksForAnyTwoItems() {
        doc.add(StickyEl(doc.nextId(), 0f, 0f, 100f, 100f, "", 0, 16f), TextEl(doc.nextId(), 400f, 0f, "Chữ", 20f, 0))
        view.onDocMutated()
        view.select(doc.elements.map { it.id })
        val ui = com.inkboard.ui.EditorActions(view)
        assertNotNull(ui.connectPair()); ui.connect()
        assertEquals(1, edges().size)
    }

    // ------------------------------------------------------------- text etc.

    @Test fun s19_textDroppedIntoHandRectFits() {
        pen(rectStroke(100f, 300f, 500f, 600f), hold = true)
        val rect = doc.elements.single() as ShapeEl
        val t = TextEl(doc.nextId(), 0f, 1500f, "Kế hoạch", 24f, 0); doc.add(t); view.onDocMutated()
        view.tool = Tool.SELECT; view.select(listOf(t.id))
        val r = view.selectionScreenRect()!!
        pen(listOf(r.centerX() to r.centerY()) + seg(r.centerX(), r.centerY(), 300f, 450f).drop(1))
        assertEquals("Kế hoạch", (doc.byId(rect.id) as ShapeEl).label)
    }

    @Test fun s20_textDroppedOnConnectorBecomesLabel() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val t = TextEl(doc.nextId(), 0f, 1500f, "Có", 24f, 0); doc.add(t); view.onDocMutated()
        view.tool = Tool.SELECT; view.select(listOf(t.id))
        val r = view.selectionScreenRect()!!
        val e = edges().single(); val ends = doc.edgeEnds(e)!!
        pen(listOf(r.centerX() to r.centerY()) + seg(r.centerX(), r.centerY(), sx((ends[0] + ends[2]) / 2), sy((ends[1] + ends[3]) / 2), wobble = 0f).drop(1))
        assertEquals("Có", (doc.byId(e.id) as Edge).label)
    }

    @Test fun s21_eraserRemovesConnector() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        view.tool = Tool.ERASER
        pen(seg(525f, 300f, 525f, 500f))
        assertEquals(0, edges().size)
        assertEquals(2, doc.elements.size)
    }

    @Test fun s22_scratchOutRemovesConnector() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val zz = ArrayList<Pair<Float, Float>>()
        for (i in 0..8) for (k in 0 until 6) { val t = k / 6f; val xa = if (i % 2 == 0) 480f else 570f; val xb = if (i % 2 == 0) 570f else 480f; zz += (xa + (xb - xa) * t) to (390f + i * 2f + t * 2f) }
        pen(zz)
        assertEquals(0, edges().size)
        assertEquals(2, doc.elements.size)
    }

    @Test fun s23_fingerTapSelectsShapeAndDragUndoesInOneStep() {
        pen(rectStroke(100f, 300f, 400f, 500f), hold = true)
        val s = doc.elements.single()
        fingerTap(250f, 400f)
        assertTrue(s.id in view.selection)
        fingerDrag(250f, 400f, 550f, 700f)
        assertTrue((doc.byId(s.id) as ShapeEl).geo[0] > (s as ShapeEl).geo[0] + 100f)
        doc.undo()
        assertEquals(s.geo[0], (doc.byId(s.id) as ShapeEl).geo[0], 0.01f)
    }

    @Test fun s24_inkInsideShapeMovesWithIt() {
        pen(rectStroke(100f, 300f, 600f, 700f), hold = true)
        val box = doc.elements.single()
        pen(seg(200f, 450f, 450f, 470f))
        val ink = doc.elements.last()
        assertEquals(box.id, doc.containerOf(ink.id))
    }

    // ------------------------------------------------------- persistence etc.

    @Test fun s25_connectorsBetweenShapesSurviveSaveLoad() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val bytes = ByteArrayOutputStream().also { DocCodec.write(doc, it) }.toByteArray()
        val r = DocCodec.read(ByteArrayInputStream(bytes))
        val e = r.elements.filterIsInstance<Edge>().single()
        assertNotNull(r.edgeEnds(e))
    }

    @Test fun s26_svgAndPngIncludeShapeConnectors() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val svg = SvgExport.export(doc, doc.contentBounds())
        assertTrue(svg.contains("<line"))
        val bmp = Bitmap.createBitmap(400, 200, Bitmap.Config.ARGB_8888)
        com.inkboard.canvas.Renderer().drawAll(Canvas(bmp), doc, Affine(0.3f, 0f, 0f))
    }

    @Test fun s27_flowBlocksAndShapesMix() {
        val n = Node(doc.nextId(), -400f, -40f, 160f, 80f, "Bước", NodeKind.ROUND, 0, 18f)
        val s = ShapeEl(doc.nextId(), ShapeKind.ELLIPSE, floatArrayOf(300f, 0f, 80f, 80f, 0f), 0, 2f)
        doc.add(n, s); view.onDocMutated()
        pen(seg(sx(-250f), sy(0f), sx(225f), sy(0f)))
        assertEquals(1, edges().size)
    }

    @Test fun s28_lassoSelectsShapesAndTheirConnector() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        view.tool = Tool.SELECT
        pen(listOf(50f to 250f, 1000f to 250f, 1000f to 550f, 50f to 550f, 50f to 250f))
        assertEquals(3, view.selection.size)
    }

    @Test fun s29_nothingCrashesOnEmptyAndHugeZoom() {
        view.setZoom(0.002f); render(); view.setZoom(90f); render()
        pen(seg(100f, 100f, 300f, 300f)); render()
        assertEquals(1, doc.elements.size)
    }

    @Test fun s30_secondFingerDuringFingerDrawDoesNotLeaveDot() {
        view.fingerDraws = true
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 400f, 400f, F))))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(0, 400f, 400f, F), P(1, 600f, 400f, F)), actionIndex = 1))
        for (k in 1..8) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, 400f - k * 10, 400f, F), P(1, 600f + k * 10, 400f, F))))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(0, 320f, 400f, F), P(1, 680f, 400f, F)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, 320f, 400f, F))))
        assertEquals("pinch with finger-drawing on must not draw", 0, doc.elements.size)
    }

    // ------------------------------------------------------ core audit #2

    @Test fun s31_slowSmallLetterIsNotTurnedIntoAShape() {
        pen(circleStroke(300f, 300f, 22f), hold = true) // an "o" written slowly
        assertTrue(doc.elements.single() is Stroke)
    }

    @Test fun s32_addBranchUndoesInOneStep() {
        val root = Node(doc.nextId(), 0f, 0f, 160f, 60f, "Gốc", NodeKind.ROUND, 0, 20f, 0L, true)
        doc.add(root); doc.clearHistory(); view.onDocMutated()
        view.select(listOf(root.id))
        com.inkboard.ui.EditorActions(view).addBranch()
        assertEquals(2, doc.elements.size)
        doc.undo()
        assertEquals(1, doc.elements.size)
        assertTrue(!doc.canUndo)
    }

    @Test fun s33_partialEraserSplitsStroke() {
        pen(seg(100f, 800f, 900f, 800f, n = 80, wobble = 0f))
        view.tool = Tool.ERASER
        view.eraserPartial = true
        pen(seg(500f, 700f, 500f, 900f, wobble = 0f))
        val parts = doc.elements.filterIsInstance<Stroke>()
        assertEquals(2, parts.size)
        assertTrue(parts.all { it.count > 5 })
        doc.undo()
        assertEquals(1, doc.elements.size)
    }

    @Test fun s34_fillShape() {
        pen(rectStroke(100f, 300f, 400f, 500f), hold = true)
        view.select(listOf(doc.elements.single().id))
        com.inkboard.ui.EditorActions(view).setFill(0xFFBBDEFB.toInt())
        assertEquals(0xFFBBDEFB.toInt(), (doc.elements.single() as ShapeEl).fill)
        render()
    }

    @Test fun s35_reverseConnector() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val e = edges().single()
        view.select(listOf(e.id))
        com.inkboard.ui.EditorActions(view).reverseEdge()
        val r = edges().single()
        assertEquals(e.from, r.to); assertEquals(e.to, r.from)
    }

    @Test fun s36_copyPasteAcrossDocuments() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val dir = java.nio.file.Files.createTempDirectory("clip").toFile()
        val clip = com.inkboard.data.ClipboardStore(dir)
        clip.copy(doc, doc.elements.map { it.id })
        val other = Document()
        view.doc = other
        com.inkboard.ui.EditorActions(view).paste(clip.load()!!)
        assertEquals(3, other.elements.size)
        val e = other.elements.filterIsInstance<Edge>().single()
        assertNotNull(other.edgeEnds(e))
    }

    @Test fun s37_picturesDecodeAtDisplayedSize() {
        val cache = com.inkboard.canvas.ImageCache()
        val bmp = Bitmap.createBitmap(1600, 2200, Bitmap.Config.ARGB_8888)
        val bytes = ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.JPEG, 80, it) }.toByteArray()
        assertEquals(8, cache.sampleFor("p", bytes, 150f))   // a page shown 150 px wide
        assertEquals(1, cache.sampleFor("p", bytes, 1600f))  // full size when zoomed in
        val small = cache.get("p", bytes, 8, sync = true)!!
        assertEquals(200, small.width)
    }

    @Test fun s38_draggingManyStrokesKeepsWorking() {
        val els = (0 until 2000).map { i -> Stroke(doc.nextId(), floatArrayOf((i % 50) * 20f, (i / 50) * 20f, 0.5f, (i % 50) * 20f + 10f, (i / 50) * 20f + 5f, 0.5f), 0, 2f, PenType.PEN) }
        doc.add(els); view.onDocMutated()
        view.tool = Tool.SELECT
        view.select(els.map { it.id })
        val r = view.selectionScreenRect()!!
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, r.centerX(), r.centerY(), S))))
        val t = System.nanoTime()
        for (k in 1..20) { send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, r.centerX() + k * 5, r.centerY(), S)))); render() }
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, r.centerX() + 100, r.centerY(), S))))
        assertEquals(els[0].pts[0] + 100f / view.scale, (doc.byId(els[0].id) as Stroke).pts[0], 1f)
        println("AUDIT drag 2000 strokes: ${(System.nanoTime() - t) / 1e6 / 20} ms/frame (software canvas)")
    }

    @Test fun s39_connectWhileCanvasRotated() {
        val a = ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(-100f, -400f, 100f, -400f, 100f, -200f, -100f, -200f), 0, 2f)
        val b = ShapeEl(doc.nextId(), ShapeKind.POLYGON, floatArrayOf(-100f, 200f, 100f, 200f, 100f, 400f, -100f, 400f), 0, 2f)
        doc.add(a, b); view.onDocMutated()
        view.setViewRotation(90f)
        // World vertical line; on a 90°-rotated screen it is horizontal. Map world → screen through the view.
        fun scr(wx: Float, wy: Float): Pair<Float, Float> {
            val px = wx * view.scale + view.tx; val py = wy * view.scale + view.ty
            val m = android.graphics.Matrix(); m.postTranslate(-540f, -1000f); m.postRotate(90f); m.postTranslate(540f, 1000f)
            val f = floatArrayOf(px, py); m.mapPoints(f); return f[0] to f[1]
        }
        pen((0..16).map { i -> scr(0f, -210f + 420f * i / 16) })
        assertEquals(1, edges().size)
    }

    @Test fun s40_doubleTapConnectorOpensLabelEditor() {
        twoHandRects(); pen(seg(390f, 400f, 660f, 405f))
        val ends = doc.edgeEnds(edges().single())!!
        val mx = sx((ends[0] + ends[2]) / 2); val my = sy((ends[1] + ends[3]) / 2)
        fingerTap(mx, my); fingerTap(mx, my)
        assertTrue(editRequests.any { it is Edge })
    }

    @Test fun s41_annotatingLockedPdfPageIsSafe() {
        val page = ImageEl(doc.nextId(), -300f, -400f, 600f, 800f, "pg"); doc.images["pg"] = byteArrayOf(1)
        doc.add(page); doc.locked += page.id; view.onDocMutated()
        pen(seg(sx(-200f), sy(0f), sx(150f), sy(20f)))
        val ink = doc.elements.last()
        assertEquals(page.id, doc.containerOf(ink.id))          // note travels with its page
        view.tool = Tool.ERASER
        pen(seg(sx(250f), sy(-350f), sx(250f), sy(350f)))      // erasing over the page leaves it
        assertNotNull(doc.byId(page.id))
        view.tool = Tool.SELECT
        pen(listOf(0f to 0f, 1079f to 0f, 1079f to 1999f, 0f to 1999f, 0f to 0f))
        assertTrue(page.id !in view.selection)
    }
}
