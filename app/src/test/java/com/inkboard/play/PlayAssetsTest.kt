package com.inkboard.play

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Shader
import android.graphics.Typeface
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createEmptyComposeRule
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.core.content.ContextCompat
import androidx.test.core.app.ActivityScenario
import androidx.test.core.app.ApplicationProvider
import com.inkboard.MainActivity
import com.inkboard.R
import com.inkboard.canvas.Renderer
import com.inkboard.data.DocStore
import com.inkboard.model.*
import com.inkboard.ui.Prefs
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.io.File
import kotlin.math.cos
import kotlin.math.sin

/** Generates Google Play graphics (icon, feature graphic, screenshots). Runs only with PLAY_ASSETS=<dir>. */
@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(qualifiers = "w360dp-h800dp-xxhdpi")
class PlayAssetsTest {
    @get:Rule val compose = createEmptyComposeRule()
    private val out get() = System.getenv("PLAY_ASSETS")?.let { File(it).apply { mkdirs() } }

    private fun save(bmp: Bitmap, name: String) = File(out, name).outputStream().use { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }

    private fun drawIcon(c: Canvas, size: Int) {
        val ctx = ApplicationProvider.getApplicationContext<android.content.Context>()
        val bg = ContextCompat.getDrawable(ctx, R.drawable.ic_launcher_bg)!!
        val fg = ContextCompat.getDrawable(ctx, R.drawable.ic_launcher_fg)!!
        bg.setBounds(0, 0, size, size); bg.draw(c)
        // Adaptive icon foreground is 108dp with a 72dp safe zone: scale it up so the mark fills the icon.
        val inset = -(size * 18 / 72)
        fg.setBounds(inset, inset, size - inset, size - inset); fg.draw(c)
    }

    @Test
    fun icon() {
        out ?: return
        val bmp = Bitmap.createBitmap(512, 512, Bitmap.Config.ARGB_8888)
        drawIcon(Canvas(bmp), 512)
        save(bmp, "icon-512.png")
    }

    /** A recognisable demo board: mind map, flowchart, notes, sketch. */
    private fun demo(d: Document) {
        TextMetrics.measure.let { }
        val root = Node(d.nextId(), 0f, 0f, 10f, 10f, "", NodeKind.ROUND, 0xFFFFC857.toInt(), 26f, 0L, true).fitText("Ra mắt InkBoard")
        d.add(root)
        for (t in listOf("Thiết kế giao diện", "Kiểm thử S Pen", "Trang Google Play", "Truyền thông")) d.addMindmapChild(d.byId(root.id) as Node, t)
        val kids = d.children(root.id)
        d.addMindmapChild(kids[1], "Độ trễ nét")
        d.addMindmapChild(kids[1], "Chống chạm tay")
        d.layoutMindmap(root.id)
        // Flowchart below
        val y0 = 520f
        val a = Node(d.nextId(), -60f, y0, 10f, 10f, "", NodeKind.ELLIPSE, 0xFFC8E6C9.toInt(), 20f).fitText("Ý tưởng")
        val b = Node(d.nextId(), -60f, y0 + 140f, 10f, 10f, "", NodeKind.DIAMOND, 0xFFFFE0B2.toInt(), 20f).fitText("Khả thi?")
        val c = Node(d.nextId(), 260f, y0 + 140f, 10f, 10f, "", NodeKind.ROUND, 0xFFBBDEFB.toInt(), 20f).fitText("Phác thảo")
        d.add(a, b, c)
        d.add(Edge(d.nextId(), a.id, b.id, 0xFF455A64.toInt(), 2.5f), Edge(d.nextId(), b.id, c.id, 0xFF455A64.toInt(), 2.5f, "Có"))
        d.add(StickyEl(d.nextId(), 520f, y0 - 20f, 220f, 200f, "Nhớ: thử bản trên S25 Ultra trước khi phát hành", 0xFFFFF59D.toInt(), 22f))
        // Hand-drawn light bulb
        fun stroke(color: Int, w: Float, f: (Double) -> Pair<Double, Double>, n: Int = 80) {
            val pts = FloatArray(n * 3) { k -> val i = k / 3; val t = i / (n - 1.0); val (x, y) = f(t); when (k % 3) { 0 -> x.toFloat(); 1 -> y.toFloat(); else -> (0.45 + 0.4 * sin(t * Math.PI)).toFloat() } }
            d.add(Stroke(d.nextId(), pts, color, w, PenType.PEN, taper = true))
        }
        val bx = -330.0; val by = 620.0
        stroke(0xFF1A73E8.toInt(), 5f, { t -> val a2 = Math.PI * 0.75 + t * Math.PI * 1.5; (bx + 70 * cos(a2)) to (by - 40 + 70 * sin(a2)) })
        stroke(0xFF1A73E8.toInt(), 5f, { t -> (bx - 30 + 60 * t) to (by + 60) }, 20)
        stroke(0xFF1A73E8.toInt(), 5f, { t -> (bx - 25 + 50 * t) to (by + 80) }, 20)
        for (k in 0 until 5) { val ang = -Math.PI / 2 + (k - 2) * 0.55; stroke(0xFFF29900.toInt(), 4f, { t -> (bx + (95 + 30 * t) * cos(ang)) to (by - 40 + (95 + 30 * t) * sin(ang)) }, 12) }
        d.add(TextEl(d.nextId(), bx.toFloat() - 70f, by.toFloat() + 110f, "Ý tưởng!", 30f, 0xFF1A73E8.toInt()))
    }

    @Test
    fun featureGraphic() {
        out ?: return
        val w = 1024; val h = 500
        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)
        val p = Paint(Paint.ANTI_ALIAS_FLAG)
        p.shader = LinearGradient(0f, 0f, w.toFloat(), h.toFloat(), 0xFF1F3A5F.toInt(), 0xFF2E5C8A.toInt(), Shader.TileMode.CLAMP)
        c.drawRect(0f, 0f, w.toFloat(), h.toFloat(), p)
        p.shader = null
        // Board card on the right
        val d = Document(); Renderer(); demo(d)
        val card = android.graphics.RectF(470f, 40f, 1000f, 460f)
        p.color = 0xFFFBFBF9.toInt(); c.drawRoundRect(card, 24f, 24f, p)
        c.save(); c.clipRect(card)
        val b = d.contentBounds()
        val s = minOf((card.width() - 40) / b.w, (card.height() - 40) / b.h)
        Renderer().drawAll(c, d, Affine(s, card.centerX() - b.cx * s, card.centerY() - b.cy * s))
        c.restore()
        // Icon + texts on the left
        c.save(); c.translate(50f, 70f); drawIcon(c, 110); c.restore()
        p.color = Color.WHITE; p.typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD); p.textSize = 64f
        c.drawText("InkBoard", 50f, 260f, p)
        p.typeface = Typeface.DEFAULT; p.textSize = 30f; p.color = 0xFFE3ECF7.toInt()
        c.drawText("Không gian vẽ vô hạn cho S Pen", 50f, 315f, p)
        c.drawText("Phác thảo · Sơ đồ · Mindmap", 50f, 360f, p)
        p.textSize = 24f; p.color = 0xFFFFC857.toInt()
        c.drawText("Offline · Miễn phí · Không tài khoản", 50f, 420f, p)
        save(bmp, "feature-graphic-1024x500.png")
    }

    private fun capture(name: String) {
        compose.waitForIdle()
        var bmp: Bitmap? = null
        scenario!!.onActivity { act ->
            val v = act.window.decorView
            bmp = Bitmap.createBitmap(v.width, v.height, Bitmap.Config.ARGB_8888).also { v.draw(Canvas(it)) }
        }
        save(bmp!!, name)
    }

    private var scenario: ActivityScenario<MainActivity>? = null

    @Test
    fun screenshots() {
        out ?: return
        val ctx = ApplicationProvider.getApplicationContext<android.content.Context>()
        Prefs(ctx).onboarded = true
        val store = DocStore(ctx)
        Renderer()
        val mm = Document()
        val r = Node(mm.nextId(), 0f, 0f, 10f, 10f, "", NodeKind.ROUND, 0xFFFFC857.toInt(), 26f, 0L, true).fitText("Ôn thi cuối kỳ"); mm.add(r)
        for (t in listOf("Toán", "Vật lý", "Tiếng Anh", "Lịch ôn")) mm.addMindmapChild(mm.byId(r.id) as Node, t)
        mm.layoutMindmap(r.id)
        store.saveThumbnail(store.create("Mindmap ôn thi", "Học tập", mm).id, mm)
        val sk = Document()
        sk.guides = Guides(SymmetryMode.RADIAL, 8)
        for (k in 0 until 8) {
            val a0 = k * Math.PI / 4
            val pts = FloatArray(40 * 3) { j -> val i = j / 3; val t = i / 39.0; val rr = 40 + 160 * t; val a = a0 + 0.6 * sin(t * Math.PI); when (j % 3) { 0 -> (rr * cos(a)).toFloat(); 1 -> (rr * sin(a)).toFloat(); else -> (0.3 + 0.6 * sin(t * Math.PI)).toFloat() } }
            sk.add(Stroke(sk.nextId(), pts, 0xFFE52592.toInt(), 6f, PenType.PEN, taper = true))
        }
        store.saveThumbnail(store.create("Hoạ tiết đối xứng", "Phác thảo", sk).id, sk)
        val fl = Document()
        val n1 = Node(fl.nextId(), 0f, 0f, 10f, 10f, "", NodeKind.ELLIPSE, 0xFFC8E6C9.toInt(), 20f).fitText("Bắt đầu")
        val n2 = Node(fl.nextId(), 0f, 140f, 10f, 10f, "", NodeKind.ROUND, 0xFFBBDEFB.toInt(), 20f).fitText("Nhận đơn")
        val n3 = Node(fl.nextId(), 0f, 280f, 10f, 10f, "", NodeKind.DIAMOND, 0xFFFFE0B2.toInt(), 20f).fitText("Còn hàng?")
        fl.add(n1, n2, n3); fl.add(Edge(fl.nextId(), n1.id, n2.id, 0xFF455A64.toInt(), 2f), Edge(fl.nextId(), n2.id, n3.id, 0xFF455A64.toInt(), 2f))
        store.saveThumbnail(store.create("Quy trình bán hàng", "Công việc", fl).id, fl)
        Thread.sleep(5)
        val main = Document().also { demo(it) }
        val m1 = store.create("Kế hoạch ra mắt", "Công việc", main); store.saveThumbnail(m1.id, main)

        scenario = ActivityScenario.launch(MainActivity::class.java)
        compose.waitUntil(8000) { compose.onAllNodesWithText("Kế hoạch ra mắt", useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty() }
        capture("screenshot-1-home.png")

        compose.onAllNodesWithText("Kế hoạch ra mắt", useUnmergedTree = true)[0].performClick()
        compose.waitUntil(8000) { compose.onAllNodesWithContentDescription("Bút").fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithContentDescription("Về nội dung").performClick()
        capture("screenshot-2-board.png")

        compose.onNodeWithContentDescription("Thành phần").performClick()
        compose.waitUntil(8000) { compose.onAllNodes(hasText("Thành phần (", substring = true)).fetchSemanticsNodes().isNotEmpty() }
        capture("screenshot-3-components.png")
        compose.onNodeWithContentDescription("Đóng").performClick()

        compose.onNodeWithContentDescription("Chèn").performClick()
        compose.onAllNodesWithText("Thư viện hình…", useUnmergedTree = true)[0].performClick()
        compose.waitUntil(8000) { compose.onAllNodesWithText("Hình có sẵn", useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty() }
        capture("screenshot-4-library.png")
        scenario?.close()
    }
}
