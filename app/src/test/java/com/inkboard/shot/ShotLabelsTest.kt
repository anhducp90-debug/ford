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
class ShotLabelsTest {
    @Test
    fun shot() {
        val out = System.getenv("SHOT_OUT2") ?: return
        val v = InkCanvasView(ApplicationProvider.getApplicationContext())
        val d = Document()
        val blue = 0xFF1A73E8.toInt()
        d.add(ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(40f, 60f, 380f, 60f, 380f, 220f, 40f, 220f), blue, 3f, label = "Kế hoạch quý 4"))
        d.add(ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(620f, 140f, 160f, 90f, 0f), 0xFF188038.toInt(), 3f, label = "Ý tưởng chính cần trình bày với cả nhóm"))
        d.add(ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(210f, 300f, 380f, 560f, 40f, 560f), 0xFFD93025.toInt(), 3f, label = "Rủi ro"))
        d.add(ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(620f, 300f, 800f, 430f, 620f, 560f, 440f, 430f), 0xFFF29900.toInt(), 3f, label = "Duyệt?"))
        d.add(TableEl(d.nextId(), 40f, 640f, 740f, 180f, 3, 3, 0xFF455A64.toInt(), 2f, mapOf(0 to "Việc", 1 to "Người", 2 to "Hạn", 3 to "Thiết kế", 4 to "An", 5 to "15/12", 7 to "Bình")))
        val target = ShapeEl(d.nextId(), ShapeKind.POLYGON, floatArrayOf(40f, 900f, 420f, 900f, 420f, 1080f, 40f, 1080f), blue, 3f)
        val txt = TextEl(d.nextId(), 600f, 960f, "Thả tôi vào hình", 26f, 0xFF202124.toInt())
        d.add(target, txt)
        d.camScale = 1f; d.camX = 140f; d.camY = 60f
        v.doc = d
        v.showMinimap = false
        v.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1300, View.MeasureSpec.EXACTLY))
        v.layout(0, 0, 1080, 1300)
        // Start dragging the text over the empty rectangle (no release) to show the drop hint.
        v.tool = Tool.SELECT
        v.select(listOf(txt.id))
        val r = v.selectionScreenRect()!!
        fun e(a: Int, x: Float, y: Float): MotionEvent {
            val p = arrayOf(MotionEvent.PointerProperties().apply { id = 0; toolType = MotionEvent.TOOL_TYPE_STYLUS })
            val c = arrayOf(MotionEvent.PointerCoords().apply { this.x = x; this.y = y; pressure = 0.6f })
            return MotionEvent.obtain(0, 0, a, 1, p, c, 0, 0, 1f, 1f, 0, 0, 0, 0)
        }
        v.dispatchTouchEvent(e(MotionEvent.ACTION_DOWN, r.centerX(), r.centerY()))
        for (k in 1..10) v.dispatchTouchEvent(e(MotionEvent.ACTION_MOVE, r.centerX() - k * 50f, r.centerY() + k * 2f))
        val bmp = Bitmap.createBitmap(1080, 1300, Bitmap.Config.ARGB_8888)
        v.draw(Canvas(bmp))
        File(out).outputStream().use { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }
    }
}
