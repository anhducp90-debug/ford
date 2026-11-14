package com.inkboard.canvas

import android.os.Looper
import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import androidx.test.core.app.ApplicationProvider
import com.inkboard.model.Document
import com.inkboard.model.Guides
import com.inkboard.model.Stroke
import com.inkboard.model.SymmetryMode
import com.inkboard.model.StickyEl
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.GraphicsMode
import java.time.Duration
import kotlin.math.abs

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class SketchCanvasTest {
    private lateinit var view: InkCanvasView
    private lateinit var doc: Document
    private var t0 = 0L
    private var picked = 0
    private data class P(val id: Int, val x: Float, val y: Float, val tool: Int)

    private fun ev(action: Int, pts: List<P>, actionIndex: Int = 0): MotionEvent {
        val props = pts.map { p -> MotionEvent.PointerProperties().apply { id = p.id; toolType = p.tool } }.toTypedArray()
        val coords = pts.map { p -> MotionEvent.PointerCoords().apply { x = p.x; y = p.y; pressure = 0.7f } }.toTypedArray()
        val a = if (action == MotionEvent.ACTION_POINTER_DOWN || action == MotionEvent.ACTION_POINTER_UP)
            action or (actionIndex shl MotionEvent.ACTION_POINTER_INDEX_SHIFT) else action
        t0 += 16
        return MotionEvent.obtain(0, t0, a, pts.size, props, coords, 0, 0, 1f, 1f, 0, 0, 0, 0)
    }
    private fun send(e: MotionEvent) { view.dispatchTouchEvent(e); e.recycle() }
    private fun pen(vararg xy: Float) {
        val s = MotionEvent.TOOL_TYPE_STYLUS
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, xy[0], xy[1], s))))
        var i = 2
        while (i < xy.size) { send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, xy[i], xy[i + 1], s)))); i += 2 }
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, xy[xy.size - 2], xy[xy.size - 1], s))))
    }
    private fun line(x0: Float, y0: Float, x1: Float, y1: Float, n: Int = 20) =
        FloatArray((n + 1) * 2) { k -> val i = k / 2; if (k % 2 == 0) x0 + (x1 - x0) * i / n else y0 + (y1 - y0) * i / n }

    @Before
    fun setUp() {
        view = InkCanvasView(ApplicationProvider.getApplicationContext())
        doc = Document()
        view.doc = doc
        view.showMinimap = false
        view.measure(View.MeasureSpec.makeMeasureSpec(1000, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1600, View.MeasureSpec.EXACTLY))
        view.layout(0, 0, 1000, 1600)
        view.listener = object : InkCanvasView.Listener {
            override fun onSelectionChanged(screenRect: android.graphics.RectF?, ids: Set<Long>) {}
            override fun onDocChanged() {}
            override fun onCameraChanged(scale: Float) {}
            override fun onEditRequest(el: com.inkboard.model.Element, wx: Float, wy: Float) {}
            override fun onColorPicked(color: Int) { picked = color }
        }
        t0 = SystemClock.uptimeMillis()
    }

    @Test
    fun symmetryDrawsMirroredStroke() {
        view.setSymmetry(SymmetryMode.VERTICAL)
        val cx = doc.guides.symX
        pen(*line(600f, 300f, 700f, 500f))
        assertEquals(2, doc.elements.size)
        val a = doc.elements[0] as Stroke; val b = doc.elements[1] as Stroke
        assertEquals(cx - (a.pts[0] - cx), b.pts[0], 0.01f)
        doc.undo()
        assertEquals(0, doc.elements.size) // one undo step
    }

    @Test
    fun perspectiveAssistMakesStraightLineTowardVanishingPoint() {
        view.setPerspective(1)
        val g = doc.guides
        // Draw a wobbly stroke heading roughly to the VP
        val sx = 100f; val sy = 1200f
        val vx = g.vp1x * view.scale + view.tx; val vy = g.vp1y * view.scale + view.ty
        val pts = ArrayList<Float>()
        for (i in 0..20) { val t = i / 20f * 0.6f; pts += sx + (vx - sx) * t + (if (i % 2 == 0) 6f else -6f); pts += sy + (vy - sy) * t }
        pen(*pts.toFloatArray())
        val s = doc.elements.single() as Stroke
        val x0 = s.pts[0]; val y0 = s.pts[1]; val x1 = s.pts[s.pts.size - 3]; val y1 = s.pts[s.pts.size - 2]
        val cross = (x1 - x0) * (g.vp1y - y0) - (y1 - y0) * (g.vp1x - x0)
        assertEquals(0f, cross / 1000f, 0.5f) // collinear with the VP
    }

    @Test
    fun rotatedViewMapsPenToWorld() {
        view.setViewRotation(90f)
        pen(*line(500f, 700f, 500f, 900f)) // vertical on screen
        val s = doc.elements.single() as Stroke
        val dx = s.pts[s.pts.size - 3] - s.pts[0]; val dy = s.pts[s.pts.size - 2] - s.pts[1]
        assertTrue("screen-vertical becomes world-horizontal: dx=$dx dy=$dy", abs(dx) > 150f && abs(dy) < 5f)
        view.resetViewTransform()
        assertEquals(0f, view.canvasRotation, 0f)
    }

    @Test
    fun twoFingerTwistRotatesCanvas() {
        val f = MotionEvent.TOOL_TYPE_FINGER
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, 400f, 800f, f))))
        send(ev(MotionEvent.ACTION_POINTER_DOWN, listOf(P(0, 400f, 800f, f), P(1, 600f, 800f, f)), actionIndex = 1))
        for (k in 1..30) {
            val a = Math.toRadians(k * 1.5)
            val dx = (100 * Math.cos(a)).toFloat(); val dy = (100 * Math.sin(a)).toFloat()
            send(ev(MotionEvent.ACTION_MOVE, listOf(P(0, 500f - dx, 800f - dy, f), P(1, 500f + dx, 800f + dy, f))))
        }
        assertTrue("rotation ${view.canvasRotation}", view.canvasRotation > 20f)
    }

    @Test
    fun flipMirrorsInput() {
        view.toggleFlip()
        pen(*line(300f, 800f, 400f, 800f)) // left→right on screen
        val s = doc.elements.single() as Stroke
        assertTrue(s.pts[s.pts.size - 3] < s.pts[0]) // right→left in world
    }

    @Test
    fun longPressPicksColor() {
        doc.add(StickyEl(doc.nextId(), -100f, -100f, 200f, 200f, "", 0xFF90CAF9.toInt(), 16f))
        view.onDocMutated()
        val f = MotionEvent.TOOL_TYPE_FINGER
        val cx = view.tx; val cy = view.ty // world (0,0) on screen
        send(ev(MotionEvent.ACTION_DOWN, listOf(P(0, cx, cy, f))))
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(600))
        send(ev(MotionEvent.ACTION_UP, listOf(P(0, cx, cy, f))))
        assertEquals(0xFF90CAF9.toInt(), picked)
        assertEquals(0, view.selection.size) // eyedropper does not select
    }

    @Test
    fun guideHandleDragsVanishingPoint() {
        view.setPerspective(1)
        val g0 = doc.guides
        val hx = g0.vp1x * view.scale + view.tx; val hy = g0.vp1y * view.scale + view.ty
        pen(*line(hx, hy, hx + 200f, hy + 50f))
        assertEquals(0, doc.elements.size) // moving a handle draws nothing
        assertEquals(g0.vp1x + 200f / view.scale, doc.guides.vp1x, 1f)
    }

    @Test
    fun stabilizerAndTaperProduceStroke() {
        view.stabilizerLevel = 8
        pen(*line(100f, 100f, 600f, 120f, 60))
        val s = doc.elements.single() as Stroke
        assertTrue(s.taper)
        // Ends at the real pen end despite the trailing string.
        assertEquals((600f - view.tx) / view.scale, s.pts[s.pts.size - 3], 0.5f)
        val bmp = android.graphics.Bitmap.createBitmap(1000, 1600, android.graphics.Bitmap.Config.ARGB_8888)
        view.draw(android.graphics.Canvas(bmp))
    }
}
