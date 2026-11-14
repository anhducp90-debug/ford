package com.inkboard.shot

import android.graphics.Bitmap
import android.graphics.Canvas
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
class ShotTest {
    @Test
    fun shot() {
        val out = System.getenv("SHOT_OUT") ?: return
        val v = InkCanvasView(ApplicationProvider.getApplicationContext())
        val d = Document()
        val root = Node(d.nextId(), -100f, -30f, 200f, 60f, "", NodeKind.ROUND, 0xFFFFC857.toInt(), 22f, 0L, true).fitText("Ý tưởng trung tâm")
        d.add(root); d.addMindmapChild(root, "Nhánh A"); d.addMindmapChild(root, "Nhánh B"); d.layoutMindmap(root.id)
        val pts = FloatArray(60 * 3) { i -> when (i % 3) { 0 -> -300f + (i / 3) * 6f; 1 -> 250f + 30f * kotlin.math.sin(i / 9f); else -> 0.7f } }
        d.add(Stroke(d.nextId(), pts, 0xFF1A73E8.toInt(), 4f, PenType.PEN))
        d.add(StickyEl(d.nextId(), 900f, 600f, 160f, 160f, "Ghi chú xa", 0xFFFFF59D.toInt(), 16f))
        v.doc = d
        v.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1400, View.MeasureSpec.EXACTLY))
        v.layout(0, 0, 1080, 1400)
        val bmp = Bitmap.createBitmap(1080, 1400, Bitmap.Config.ARGB_8888)
        v.draw(Canvas(bmp))
        File(out).outputStream().use { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }
    }
}
