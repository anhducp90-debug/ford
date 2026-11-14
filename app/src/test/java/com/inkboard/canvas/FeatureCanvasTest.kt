package com.inkboard.canvas

import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import androidx.test.core.app.ApplicationProvider
import com.inkboard.model.Document
import com.inkboard.model.Edge
import com.inkboard.model.Node
import com.inkboard.model.NodeKind
import com.inkboard.model.StickyEl
import com.inkboard.model.Stroke
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner

@RunWith(RobolectricTestRunner::class)
class FeatureCanvasTest {
    private lateinit var view: InkCanvasView
    private lateinit var doc: Document
    private var t0 = 0L
    private data class P(val id: Int, val x: Float, val y: Float, val tool: Int)

    private fun ev(action: Int, pts: List<P>, actionIndex: Int = 0, buttons: Int = 0): MotionEvent {
        val props = pts.map { p -> MotionEvent.PointerProperties().apply { id = p.id; toolType = p.tool } }.toTypedArray()
        val coords = pts.map { p -> MotionEvent.PointerCoords().apply { x = p.x; y = p.y; pressure = 0.7f } }.toTypedArray()
        val a = if (action == MotionEvent.ACTION_POINTER_DOWN || action == MotionEvent.ACTION_POINTER_UP)
            action or (actionIndex shl MotionEvent.ACTION_POINTER_INDEX_SHIFT) else action
        t0 += 16
        return MotionEvent.obtain(0, t0, a, pts.size, props, coords, 0, buttons, 1f, 1f, 0, 0, 0, 0)
    }
    private fun send(e: MotionEvent) { view.dispatchTouchEvent(e); e.recycle() }
    private fun pen(xy: FloatArray, buttons: Int = 0) {
        val s = MotionEvent.TOOL_TYPE_STYLUS
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, xy[0], xy[1], s)), buttons = buttons))
        var i = 2
        while (i < xy.size) { send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, xy[i], xy[i + 1], s)), buttons = buttons)); i += 2 }
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, xy[xy.size - 2], xy[xy.size - 1], s)), buttons = buttons))
    }
    private fun line(x0: Float, y0: Float, x1: Float, y1: Float, n: Int = 20) =
        FloatArray((n + 1) * 2) { k -> val i = k / 2; if (k % 2 == 0) x0 + (x1 - x0) * i / n else y0 + (y1 - y0) * i / n }
    private fun sx(w: Float) = w * view.scale + view.tx
    private fun sy(w: Float) = w * view.scale + view.ty

    @Before
    fun setUp() {
        view = InkCanvasView(ApplicationProvider.getApplicationContext())
        doc = Document()
        view.doc = doc
        view.showMinimap = false
        view.measure(View.MeasureSpec.makeMeasureSpec(1000, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1600, View.MeasureSpec.EXACTLY))
        view.layout(0, 0, 1000, 1600)
        t0 = SystemClock.uptimeMillis()
    }

    @Test
    fun scratchOutErasesWriting() {
        pen(line(300f, 400f, 500f, 410f))
        assertEquals(1, doc.elements.size)
        val zz = ArrayList<Float>()
        for (i in 0..8) for (k in 0 until 6) {
            val t = k / 6f
            val xa = if (i % 2 == 0) 290f else 510f; val xb = if (i % 2 == 0) 510f else 290f
            zz += xa + (xb - xa) * t; zz += 395f + i * 2f + t * 2f
        }
        pen(zz.toFloatArray())
        assertEquals(0, doc.elements.size)
        doc.undo()
        assertEquals(1, doc.elements.size)
    }

    @Test
    fun handDrawnLineConnectsBlocks() {
        val a = Node(doc.nextId(), 0f, 0f, 120f, 60f, "A", NodeKind.ROUND, 0, 16f)
        val b = Node(doc.nextId(), 0f, 400f, 120f, 60f, "B", NodeKind.ROUND, 0, 16f)
        doc.add(a, b); view.onDocMutated()
        pen(line(sx(60f), sy(30f), sx(62f), sy(430f)))
        val e = doc.elements.filterIsInstance<Edge>().single()
        assertEquals(a.id, e.from); assertEquals(b.id, e.to)
        assertEquals(0, doc.elements.count { it is Stroke })
    }

    @Test
    fun laserLeavesNothing() {
        view.laser = true
        pen(line(100f, 100f, 400f, 300f))
        assertEquals(0, doc.elements.size)
    }

    @Test
    fun penButtonCanErase() {
        pen(line(100f, 500f, 500f, 500f))
        view.penButton = InkCanvasView.PenButton.ERASE
        pen(line(300f, 400f, 300f, 600f), buttons = MotionEvent.BUTTON_STYLUS_PRIMARY)
        assertEquals(0, doc.elements.size)
    }

    @Test
    fun dragSnapsToNeighbourEdge() {
        val a = StickyEl(doc.nextId(), 0f, 0f, 100f, 100f, "", 0, 16f)
        val b = StickyEl(doc.nextId(), 300f, 3f, 100f, 100f, "", 0, 16f) // 3 units off a's top
        doc.add(a, b); view.onDocMutated()
        view.tool = Tool.SELECT
        view.select(listOf(b.id))
        val r = view.selectionScreenRect()!!
        pen(line(r.centerX(), r.centerY(), r.centerX() + 50f, r.centerY() + 1f))
        assertEquals(0f, (doc.byId(b.id) as StickyEl).y, 0.01f) // snapped to a.top
    }

    @Test
    fun foldedBranchIsHiddenFromTapAndLasso() {
        val root = Node(doc.nextId(), 0f, 0f, 120f, 50f, "R", NodeKind.ROUND, 0, 16f, 0L, true)
        doc.add(root)
        val child = doc.addMindmapChild(root, "C")
        doc.toggleCollapsed(root.id); view.onDocMutated()
        assertEquals(null, doc.hitTop(child.x + 5, child.y + 5, 1f))
        view.tool = Tool.SELECT
        val big = floatArrayOf(sx(-500f), sy(-500f), sx(1500f), sy(-500f), sx(1500f), sy(1500f), sx(-500f), sy(1500f), sx(-500f), sy(-500f))
        pen(big)
        assertTrue(child.id !in view.selection || root.id in view.selection) // only via its root
    }

    @Test
    fun stylusNearEdgeExcludesBackGesture() {
        pen(line(5f, 800f, 60f, 820f))
        assertTrue(view.systemGestureExclusionRects.isNotEmpty())
    }
}
