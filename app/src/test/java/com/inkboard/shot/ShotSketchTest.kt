package com.inkboard.shot

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.MotionEvent
import android.view.View
import androidx.test.core.app.ApplicationProvider
import com.inkboard.canvas.InkCanvasView
import com.inkboard.model.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.GraphicsMode
import java.io.File

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class ShotSketchTest {
    private fun pen(v: View, pts: List<Pair<Float, Float>>, p: (Int) -> Float = { 0.7f }) {
        fun e(a: Int, x: Float, y: Float, pr: Float): MotionEvent {
            val pp = arrayOf(MotionEvent.PointerProperties().apply { id = 0; toolType = MotionEvent.TOOL_TYPE_STYLUS })
            val c = arrayOf(MotionEvent.PointerCoords().apply { this.x = x; this.y = y; pressure = pr })
            return MotionEvent.obtain(0, 0, a, 1, pp, c, 0, 0, 1f, 1f, 0, 0, 0, 0)
        }
        v.dispatchTouchEvent(e(MotionEvent.ACTION_DOWN, pts[0].first, pts[0].second, p(0)))
        for (i in 1 until pts.size) v.dispatchTouchEvent(e(MotionEvent.ACTION_MOVE, pts[i].first, pts[i].second, p(i)))
        v.dispatchTouchEvent(e(MotionEvent.ACTION_UP, pts.last().first, pts.last().second, p(pts.size - 1)))
    }

    @Test
    fun shot() {
        val out = System.getenv("SHOT_OUT3") ?: return
        val v = InkCanvasView(ApplicationProvider.getApplicationContext())
        v.doc = Document().also { it.background = Background.PLAIN }
        v.showMinimap = false; v.showRulers = false
        v.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1300, View.MeasureSpec.EXACTLY))
        v.layout(0, 0, 1080, 1300)
        // Top half: radial symmetry petals
        v.penWidthDp = 4f; v.penColor = 0xFFE52592.toInt()
        v.centerOn(0f, 330f)
        v.setSymmetry(SymmetryMode.RADIAL, 8)
        v.doc.guides = v.doc.guides.copy(symX = 0f, symY = 0f)
        val cx = v.tx; val cy = v.ty
        pen(v, (0..40).map { i -> val t = i / 40f; (cx + 40 + 200 * t) to (cy - 60 * kotlin.math.sin(t * Math.PI).toFloat()) }) { i -> 0.2f + 0.8f * kotlin.math.sin(i / 40f * Math.PI).toFloat() }
        v.penColor = 0xFF1A73E8.toInt(); v.penWidthDp = 2.5f
        pen(v, (0..30).map { i -> val t = i / 30f; (cx + 120 + 30 * kotlin.math.cos(t * 6.28f)) to (cy + 30 * kotlin.math.sin(t * 6.28f)) })
        v.setSymmetry(SymmetryMode.OFF)
        // Bottom half: 2-point perspective box drawn with assisted lines
        v.setPerspective(2)
        val g = v.doc.guides
        fun sx(x: Float) = x * v.scale + v.tx
        fun sy(y: Float) = y * v.scale + v.ty
        v.doc.guides = g.copy(vp1x = -700f, vp1y = 520f, vp2x = 700f, vp2y = 520f)
        v.penColor = 0xFF202124.toInt(); v.penWidthDp = 2f
        val bx = sx(0f); val by = sy(560f)
        val strokes = listOf(
            listOf(bx to by, bx to by + 260),                          // vertical edge
            listOf(bx to by, bx - 200 to by - 40),                     // toward VP1 (top)
            listOf(bx to by + 260, bx - 200 to by + 200),              // toward VP1 (bottom)
            listOf(bx to by, bx + 220 to by - 40),                     // toward VP2 (top)
            listOf(bx to by + 260, bx + 220 to by + 200),              // toward VP2 (bottom)
        )
        for (s in strokes) pen(v, (0..10).map { i -> val t = i / 10f; (s[0].first + (s[1].first - s[0].first) * t + (if (i % 2 == 0) 4 else -4)) to (s[0].second + (s[1].second - s[0].second) * t) })
        val bmp = Bitmap.createBitmap(1080, 1300, Bitmap.Config.ARGB_8888)
        v.draw(Canvas(bmp))
        File(out).outputStream().use { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }
    }
}
