package com.inkboard.canvas

import android.graphics.Bitmap
import android.graphics.Canvas
import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import androidx.test.core.app.ApplicationProvider
import com.inkboard.model.Document
import com.inkboard.model.ShapeEl
import com.inkboard.model.ShapeKind
import com.inkboard.model.StickyEl
import com.inkboard.model.Stroke
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import android.os.Looper

/** Drives the canvas with synthetic stylus / finger events (no device needed). */
@RunWith(RobolectricTestRunner::class)
class CanvasGestureTest {
    private lateinit var view: InkCanvasView
    private lateinit var doc: Document
    private var t0 = 0L
    private var undoCount = 0
    private var redoCount = 0

    private data class P(val id: Int, val x: Float, val y: Float, val tool: Int)

    private fun ev(action: Int, pts: List<P>, actionIndex: Int = 0, buttons: Int = 0): MotionEvent {
        val props = pts.map { p -> MotionEvent.PointerProperties().apply { id = p.id; toolType = p.tool } }.toTypedArray()
        val coords = pts.map { p -> MotionEvent.PointerCoords().apply { x = p.x; y = p.y; pressure = 0.7f; size = 0.1f } }.toTypedArray()
        val a = if (action == MotionEvent.ACTION_POINTER_DOWN || action == MotionEvent.ACTION_POINTER_UP)
            action or (actionIndex shl MotionEvent.ACTION_POINTER_INDEX_SHIFT) else action
        t0 += 16
        return MotionEvent.obtain(0, t0, a, pts.size, props, coords, 0, buttons, 1f, 1f, 0, 0, 0, 0)
    }

    private fun send(e: MotionEvent) { view.dispatchTouchEvent(e); e.recycle() }

    private fun penStroke(vararg xy: Float, tool: Int = MotionEvent.TOOL_TYPE_STYLUS) {
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, xy[0], xy[1], tool))))
        var i = 2
        while (i < xy.size) { send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, xy[i], xy[i + 1], tool)))); i += 2 }
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, xy[xy.size - 2], xy[xy.size - 1], tool))))
    }

    private fun line(x0: Float, y0: Float, x1: Float, y1: Float, n: Int = 20): FloatArray {
        val out = FloatArray((n + 1) * 2)
        for (k in 0..n) { out[2 * k] = x0 + (x1 - x0) * k / n; out[2 * k + 1] = y0 + (y1 - y0) * k / n }
        return out
    }

    @Before
    fun setUp() {
        view = InkCanvasView(ApplicationProvider.getApplicationContext())
        doc = Document()
        view.doc = doc
        view.measure(View.MeasureSpec.makeMeasureSpec(1000, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1600, View.MeasureSpec.EXACTLY))
        view.layout(0, 0, 1000, 1600)
        view.onUndoGesture = { undoCount++ }
        view.onRedoGesture = { redoCount++ }
        t0 = SystemClock.uptimeMillis()
    }

    private fun render() {
        val bmp = Bitmap.createBitmap(1000, 1600, Bitmap.Config.ARGB_8888)
        view.draw(Canvas(bmp))
    }

    @Test
    fun penDrawsStroke() {
        penStroke(*line(100f, 100f, 400f, 300f))
        assertEquals(1, doc.elements.size)
        assertTrue(doc.elements[0] is Stroke)
        assertTrue((doc.elements[0] as Stroke).count > 5)
        render()
    }

    @Test
    fun fingerPansInsteadOfDrawing() {
        val tx0 = view.tx
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 500f, 500f, MotionEvent.TOOL_TYPE_FINGER))))
        for (k in 1..10) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, 500f + k * 20, 500f, MotionEvent.TOOL_TYPE_FINGER))))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, 700f, 500f, MotionEvent.TOOL_TYPE_FINGER))))
        assertEquals(0, doc.elements.size)
        assertEquals(tx0 + 200f, view.tx, 1f)
    }

    @Test
    fun fingerDrawsWhenEnabled() {
        view.fingerDraws = true
        penStroke(*line(100f, 100f, 300f, 100f), tool = MotionEvent.TOOL_TYPE_FINGER)
        assertEquals(1, doc.elements.size)
    }

    @Test
    fun pinchZooms() {
        val f = MotionEvent.TOOL_TYPE_FINGER
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 400f, 800f, f))))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(0, 400f, 800f, f), P(1, 600f, 800f, f)), actionIndex = 1))
        for (k in 1..10) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, 400f - k * 10, 800f, f), P(1, 600f + k * 10, 800f, f))))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(0, 300f, 800f, f), P(1, 700f, 800f, f)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, 300f, 800f, f))))
        assertEquals(2f, view.scale, 0.05f)
    }

    @Test
    fun twoFingerTapUndoThreeFingerTapRedo() {
        val f = MotionEvent.TOOL_TYPE_FINGER
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 400f, 800f, f))))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(0, 400f, 800f, f), P(1, 600f, 800f, f)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(0, 400f, 800f, f), P(1, 600f, 800f, f)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, 400f, 800f, f))))
        assertEquals(1, undoCount)

        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 300f, 800f, f))))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(0, 300f, 800f, f), P(1, 500f, 800f, f)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(0, 300f, 800f, f), P(1, 500f, 800f, f), P(2, 700f, 800f, f)), actionIndex = 2))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(0, 300f, 800f, f), P(1, 500f, 800f, f), P(2, 700f, 800f, f)), actionIndex = 2))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(0, 300f, 800f, f), P(1, 500f, 800f, f)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, 300f, 800f, f))))
        assertEquals(1, redoCount)
    }

    @Test
    fun eraserRemovesStroke() {
        penStroke(*line(100f, 400f, 500f, 400f))
        assertEquals(1, doc.elements.size)
        view.tool = Tool.ERASER
        penStroke(*line(300f, 300f, 300f, 500f))
        assertEquals(0, doc.elements.size)
        doc.undo()
        assertEquals(1, doc.elements.size)
    }

    @Test
    fun lassoSelectThenDragMoves() {
        penStroke(*line(200f, 200f, 300f, 250f))
        val before = (doc.elements[0] as Stroke).pts[0]
        view.tool = Tool.SELECT
        // Lasso around the stroke
        penStroke(150f, 150f, 350f, 150f, 350f, 300f, 150f, 300f, 150f, 150f)
        assertEquals(1, view.selection.size)
        // Drag from inside the selection
        penStroke(*line(250f, 225f, 650f, 625f))
        val after = (doc.elements[0] as Stroke).pts[0]
        val dxWorld = 400f / view.scale
        assertEquals(before + dxWorld, after, 2f)
        render()
    }

    @Test
    fun fingerDragsSelection() {
        doc.add(StickyEl(doc.nextId(), 0f, 0f, 100f, 100f, "", 0xFFFFF59D.toInt(), 16f))
        view.onDocMutated()
        view.select(listOf(doc.elements[0].id))
        val r = view.selectionScreenRect()!!
        val f = MotionEvent.TOOL_TYPE_FINGER
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, r.centerX(), r.centerY(), f))))
        for (k in 1..10) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, r.centerX() + k * 10, r.centerY(), f))))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, r.centerX() + 100, r.centerY(), f))))
        assertEquals(100f / view.scale, (doc.elements[0] as StickyEl).x, 1f)
    }

    @Test
    fun cornerHandleScalesSelection() {
        doc.add(StickyEl(doc.nextId(), 0f, 0f, 100f, 100f, "", 0xFFFFF59D.toInt(), 16f))
        view.onDocMutated()
        view.tool = Tool.SELECT
        view.select(listOf(doc.elements[0].id))
        val r = view.selectionScreenRect()!!
        val d = 6f * view.resources.displayMetrics.density
        // Drag bottom-right handle outward by 100 px diagonally.
        penStroke(*line(r.right + d, r.bottom + d, r.right + d + 100, r.bottom + d + 100))
        val s = doc.elements[0] as StickyEl
        assertTrue("scaled up: ${s.w}", s.w > 150f)
    }

    @Test
    fun fingerHoldMakesStraightLine() {
        val f = MotionEvent.TOOL_TYPE_FINGER
        val st = MotionEvent.TOOL_TYPE_STYLUS
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(5, 900f, 1500f, f))))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(5, 900f, 1500f, f), P(0, 100f, 100f, st)), actionIndex = 1))
        for (k in 1..20) send(ev(MotionEvent.ACTION_MOVE, listOf(P(5, 900f, 1500f, f), P(0, 100f + k * 20, 100f + k * 3 + (k % 3) * 4, st))))
        send(ev(MotionEvent.ACTION_POINTER_UP, listOf(P(5, 900f, 1500f, f), P(0, 500f, 160f, st)), actionIndex = 1))
        send(ev(MotionEvent.ACTION_UP, listOf(P(5, 900f, 1500f, f))))
        assertEquals(1, doc.elements.size)
        val sh = doc.elements[0] as ShapeEl
        assertEquals(ShapeKind.LINE, sh.kind)
        assertEquals(0, undoCount) // the held finger must not count as an undo tap
    }

    @Test
    fun penSideButtonPans() {
        val tx0 = view.tx
        val st = MotionEvent.TOOL_TYPE_STYLUS
        val b = MotionEvent.BUTTON_STYLUS_PRIMARY
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 100f, 100f, st)), buttons = b))
        for (k in 1..5) send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, 100f + k * 20, 100f, st)), buttons = b))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, 200f, 100f, st)), buttons = b))
        assertEquals(0, doc.elements.size)
        assertNotEquals(tx0, view.tx)
    }

    @Test
    fun tapSelectsSticky() {
        doc.add(StickyEl(doc.nextId(), 0f, 0f, 100f, 100f, "", 0xFFFFF59D.toInt(), 16f))
        view.onDocMutated()
        val cx = view.tx + 50f * view.scale
        val cy = view.ty + 50f * view.scale
        val f = MotionEvent.TOOL_TYPE_FINGER
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, cx, cy, f))))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, cx, cy, f))))
        assertEquals(1, view.selection.size)
        shadowOf(Looper.getMainLooper()).idle()
    }

    @Test
    fun zoomToContentFitsEverything() {
        penStroke(*line(100f, 100f, 900f, 1500f))
        view.zoomToContent()
        val b = doc.contentBounds()
        val vb = view.viewBox()
        assertTrue(vb.l <= b.l && vb.r >= b.r && vb.t <= b.t && vb.b >= b.b)
    }

    @Test
    fun minimapTapJumpsToContent() {
        doc.add(StickyEl(doc.nextId(), 8000f, 8000f, 200f, 200f, "", 0xFFFFF59D.toInt(), 16f))
        view.onDocMutated()
        render() // lays out the minimap
        val map = view.minimapRect()
        assertTrue(map.width() > 0)
        val before = view.viewBox()
        val f = MotionEvent.TOOL_TYPE_FINGER
        // Content is bottom-right of the current view, so tap the minimap's bottom-right area.
        val x = map.right - map.width() * 0.12f; val y = map.bottom - map.height() * 0.12f
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, x, y, f))))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, x, y, f))))
        val after = view.viewBox()
        val d0 = Math.hypot((before.cx - 8100f).toDouble(), (before.cy - 8100f).toDouble())
        val d1 = Math.hypot((after.cx - 8100f).toDouble(), (after.cy - 8100f).toDouble())
        assertTrue("moved closer: $d0 -> $d1", d1 < d0 * 0.3)
        assertEquals(0, view.selection.size) // tapping the map must not select or draw
        assertEquals(1, doc.elements.size)
    }

    @Test
    fun rulerStepsAreNice() {
        val nav = NavOverlay(1f)
        assertEquals(100f, nav.niceStep(1f, 80f), 1e-3f)
        assertEquals(50f, nav.niceStep(2f, 80f), 1e-3f)
        assertEquals(0.5f, nav.niceStep(200f, 80f), 1e-4f)
        assertEquals("0", nav.fmt(0.00001f, 1f))
        assertEquals("-250", nav.fmt(-250f, 50f))
    }

    @Test
    fun focusOnShowsBox() {
        val b = com.inkboard.model.Box(10000f, -3000f, 10400f, -2800f)
        view.focusOn(b)
        val vb = view.viewBox()
        assertTrue(vb.l <= b.l && vb.r >= b.r && vb.t <= b.t && vb.b >= b.b)
    }
}
