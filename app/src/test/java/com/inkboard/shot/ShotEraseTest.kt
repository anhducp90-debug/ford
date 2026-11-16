package com.inkboard.shot

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.MotionEvent
import android.view.View
import androidx.test.core.app.ApplicationProvider
import com.inkboard.canvas.InkCanvasView
import com.inkboard.canvas.Tool
import com.inkboard.model.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.GraphicsMode
import java.io.File

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class ShotEraseTest {
    @Test
    fun shot() {
        val out = System.getenv("SHOT_ERASE") ?: return
        val v = InkCanvasView(ApplicationProvider.getApplicationContext())
        val d = Document().also { it.background = Background.PLAIN }
        d.add(ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(-250f, 0f, 180f, 180f, 0f), 0xFF1A73E8.toInt(), 6f))
        d.add(ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(80f, -180f, 420f, -180f, 420f, 180f, 80f, 180f), 0xFFD93025.toInt(), 6f))
        v.doc = d; v.showMinimap = false; v.showRulers = false
        v.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(700, View.MeasureSpec.EXACTLY))
        v.layout(0, 0, 1080, 700)
        v.centerOn(0f, 0f)
        fun pen(pts: List<Pair<Float, Float>>) {
            fun e(a: Int, x: Float, y: Float) = MotionEvent.obtain(0, 0, a, 1,
                arrayOf(MotionEvent.PointerProperties().apply { id = 0; toolType = MotionEvent.TOOL_TYPE_STYLUS }),
                arrayOf(MotionEvent.PointerCoords().apply { this.x = x; this.y = y; pressure = 0.6f }), 0, 0, 1f, 1f, 0, 0, 0, 0)
            v.dispatchTouchEvent(e(MotionEvent.ACTION_DOWN, pts[0].first, pts[0].second))
            for (p in pts.drop(1)) v.dispatchTouchEvent(e(MotionEvent.ACTION_MOVE, p.first, p.second))
            v.dispatchTouchEvent(e(MotionEvent.ACTION_UP, pts.last().first, pts.last().second))
        }
        fun sx(x: Float) = x * v.scale + v.tx
        fun sy(y: Float) = y * v.scale + v.ty
        v.tool = Tool.ERASER; v.eraserMode = InkCanvasView.EraserMode.PARTIAL; v.eraserSizeDp = 14f
        pen((0..20).map { i -> sx(-120f + i * 4f) to sy(-150f + i * 10f) })   // nick the circle
        pen((0..30).map { i -> sx(250f) to sy(-250f + i * 17f) })             // cut the rectangle top & bottom
        val bmp = Bitmap.createBitmap(1080, 700, Bitmap.Config.ARGB_8888)
        v.draw(Canvas(bmp))
        File(out).outputStream().use { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }
    }
}
