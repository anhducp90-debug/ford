package com.inkboard.model

import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/**
 * Built-in shape library (generated geometry, no assets). Each entry builds
 * its elements centred on (cx, cy) with nominal radius r.
 */
object ShapeLibrary {
    enum class Category(val label: String) {
        BASIC("Cơ bản"), ARROWS("Mũi tên"), FLOW("Lưu đồ"), DIAGRAM("Sơ đồ & bảng"), SYMBOLS("Biểu tượng")
    }

    class Entry(
        val name: String,
        val category: Category,
        val keywords: String,
        val build: (doc: Document, cx: Float, cy: Float, r: Float, color: Int, stroke: Float) -> List<Element>,
    ) {
        fun matches(q: String): Boolean {
            if (q.isBlank()) return true
            val t = fold("$name $keywords ${category.label}")
            return fold(q).split(' ').filter { it.isNotBlank() }.all { it in t }
        }
    }

    /** Vietnamese-insensitive matching: lower-case, strip accents. */
    fun fold(s: String): String = java.text.Normalizer.normalize(s.lowercase(), java.text.Normalizer.Form.NFD)
        .replace(Regex("\\p{M}+"), "").replace('đ', 'd')

    private fun poly(doc: Document, pts: FloatArray, color: Int, w: Float) = listOf<Element>(ShapeEl(doc.nextId(), ShapeKind.POLYGON, pts, color, w))

    private fun regular(cx: Float, cy: Float, r: Float, n: Int, rot: Double = -PI / 2): FloatArray =
        FloatArray(n * 2) { k -> val i = k / 2; val a = rot + 2 * PI * i / n; if (k % 2 == 0) cx + r * cos(a).toFloat() else cy + r * sin(a).toFloat() }

    private fun star(cx: Float, cy: Float, r: Float, n: Int, inner: Float): FloatArray =
        FloatArray(n * 4) { k ->
            val i = k / 2; val a = -PI / 2 + PI * i / n; val rr = if (i % 2 == 0) r else r * inner
            if (k % 2 == 0) cx + rr * cos(a).toFloat() else cy + rr * sin(a).toFloat()
        }

    /** Normalised outline (unit square -1..1) scaled to (cx, cy, rx, ry). */
    private fun norm(cx: Float, cy: Float, rx: Float, ry: Float, vararg p: Float) = FloatArray(p.size) { i -> if (i % 2 == 0) cx + p[i] * rx else cy + p[i] * ry }

    private fun curve(n: Int, f: (Double) -> Pair<Double, Double>): FloatArray = FloatArray(n * 2) { k ->
        val (x, y) = f(2 * PI * (k / 2) / n); if (k % 2 == 0) x.toFloat() else y.toFloat()
    }

    private fun node(doc: Document, kind: NodeKind, text: String, fill: Int, cx: Float, cy: Float, r: Float): List<Element> {
        val n = Node(doc.nextId(), cx, cy, 10f, 10f, "", kind, fill, r * 0.26f).fitText(text)
        return listOf(n.copy(x = cx - n.w / 2, y = cy - n.h / 2))
    }

    val entries: List<Entry> = buildList {
        val B = Category.BASIC; val A = Category.ARROWS; val F = Category.FLOW; val D = Category.DIAGRAM; val S = Category.SYMBOLS
        fun add(name: String, cat: Category, kw: String, b: (Document, Float, Float, Float, Int, Float) -> List<Element>) = add(Entry(name, cat, kw, b))

        // ---- Basic
        add("Hình chữ nhật", B, "rectangle box khung") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.8f, -1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Hình vuông", B, "square") { d, x, y, r, c, w -> poly(d, norm(x, y, r, r, -1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Hình tròn", B, "circle") { d, x, y, r, c, w -> listOf(ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x, y, r, r, 0f), c, w)) }
        add("Hình elip", B, "ellipse oval bau duc") { d, x, y, r, c, w -> listOf(ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x, y, r * 1.4f, r * 0.8f, 0f), c, w)) }
        add("Tam giác", B, "triangle") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.15f, r, 0f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Tam giác vuông", B, "right triangle") { d, x, y, r, c, w -> poly(d, norm(x, y, r, r, -1f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Hình thoi", B, "diamond rhombus") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r, 0f, -1f, 1f, 0f, 0f, 1f, -1f, 0f), c, w) }
        add("Ngũ giác", B, "pentagon 5") { d, x, y, r, c, w -> poly(d, regular(x, y, r, 5), c, w) }
        add("Lục giác", B, "hexagon 6") { d, x, y, r, c, w -> poly(d, regular(x, y, r, 6, 0.0), c, w) }
        add("Bát giác", B, "octagon 8") { d, x, y, r, c, w -> poly(d, regular(x, y, r, 8, PI / 8), c, w) }
        add("Hình bình hành", B, "parallelogram") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.75f, -0.7f, -1f, 1f, -1f, 0.7f, 1f, -1f, 1f), c, w) }
        add("Hình thang", B, "trapezoid") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.75f, -0.6f, -1f, 0.6f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Ngôi sao 5 cánh", B, "star") { d, x, y, r, c, w -> poly(d, star(x, y, r, 5, 0.45f), c, w) }
        add("Ngôi sao 6 cánh", B, "star david") { d, x, y, r, c, w -> poly(d, star(x, y, r, 6, 0.55f), c, w) }
        add("Ngôi sao 8 cánh", B, "star burst") { d, x, y, r, c, w -> poly(d, star(x, y, r, 8, 0.6f), c, w) }
        add("Chữ thập", B, "cross plus cong") { d, x, y, r, c, w -> poly(d, norm(x, y, r, r, -0.33f, -1f, 0.33f, -1f, 0.33f, -0.33f, 1f, -0.33f, 1f, 0.33f, 0.33f, 0.33f, 0.33f, 1f, -0.33f, 1f, -0.33f, 0.33f, -1f, 0.33f, -1f, -0.33f, -0.33f, -0.33f), c, w) }
        add("Trái tim", B, "heart love") { d, x, y, r, c, w ->
            poly(d, curve(48) { t -> val s = sin(t); (x + r * 0.06 * 16 * s * s * s) to (y - r * 0.06 * (13 * cos(t) - 5 * cos(2 * t) - 2 * cos(3 * t) - cos(4 * t))) }, c, w)
        }
        add("Đám mây", B, "cloud") { d, x, y, r, c, w ->
            poly(d, curve(72) { t -> val k = 1 + 0.12 * kotlin.math.abs(sin(4 * t)); (x + r * 1.35 * k * cos(t)) to (y + r * 0.8 * k * sin(t)) }, c, w)
        }
        add("Hình bán nguyệt", B, "semicircle half") { d, x, y, r, c, w -> poly(d, FloatArray(34) { k -> val i = k / 2; val a = PI + PI * i / 16; if (k % 2 == 0) x + r * cos(a).toFloat() else y + r * 0.5f + r * sin(a).toFloat() }, c, w) }

        // ---- Arrows
        add("Mũi tên", A, "arrow line connector") { d, x, y, r, c, w -> listOf(ShapeEl(d.nextId(), ShapeKind.ARROW, floatArrayOf(x - r * 1.5f, y, x + r * 1.5f, y), c, w)) }
        add("Đường thẳng", A, "line") { d, x, y, r, c, w -> listOf(ShapeEl(d.nextId(), ShapeKind.LINE, floatArrayOf(x - r * 1.5f, y, x + r * 1.5f, y), c, w)) }
        val right = floatArrayOf(-1f, -0.35f, 0.3f, -0.35f, 0.3f, -0.8f, 1f, 0f, 0.3f, 0.8f, 0.3f, 0.35f, -1f, 0.35f)
        add("Mũi tên khối phải", A, "block arrow right") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.4f, r * 0.8f, *right), c, w) }
        add("Mũi tên khối trái", A, "block arrow left") { d, x, y, r, c, w -> poly(d, norm(x, y, -r * 1.4f, r * 0.8f, *right), c, w) }
        add("Mũi tên khối lên", A, "block arrow up") { d, x, y, r, c, w -> poly(d, FloatArray(right.size) { i -> if (i % 2 == 0) x + right[i + 1] * r * 0.8f else y - right[i - 1] * r * 1.4f }, c, w) }
        add("Mũi tên khối xuống", A, "block arrow down") { d, x, y, r, c, w -> poly(d, FloatArray(right.size) { i -> if (i % 2 == 0) x + right[i + 1] * r * 0.8f else y + right[i - 1] * r * 1.4f }, c, w) }
        add("Mũi tên hai chiều", A, "double arrow both") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.5f, r * 0.8f, -1f, 0f, -0.55f, -0.8f, -0.55f, -0.35f, 0.55f, -0.35f, 0.55f, -0.8f, 1f, 0f, 0.55f, 0.8f, 0.55f, 0.35f, -0.55f, 0.35f, -0.55f, 0.8f), c, w) }
        add("Chevron", A, "chevron next tiep") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.8f, -1f, -1f, 0.4f, -1f, 1f, 0f, 0.4f, 1f, -1f, 1f, -0.4f, 0f), c, w) }
        add("Mũi tên ngũ giác", A, "pentagon arrow tag nhan") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.4f, r * 0.7f, -1f, -1f, 0.5f, -1f, 1f, 0f, 0.5f, 1f, -1f, 1f), c, w) }

        // ---- Flowchart
        add("Quy trình", F, "process step buoc") { d, x, y, r, _, _ -> node(d, NodeKind.ROUND, "Bước", 0xFFBBDEFB.toInt(), x, y, r) }
        add("Quy trình (vuông)", F, "process rectangle") { d, x, y, r, _, _ -> node(d, NodeKind.RECT, "Bước", 0xFFBBDEFB.toInt(), x, y, r) }
        add("Quyết định", F, "decision if dieu kien") { d, x, y, r, _, _ -> node(d, NodeKind.DIAMOND, "Điều kiện?", 0xFFFFE0B2.toInt(), x, y, r) }
        add("Bắt đầu / Kết thúc", F, "terminal start end") { d, x, y, r, _, _ -> node(d, NodeKind.ELLIPSE, "Bắt đầu", 0xFFC8E6C9.toInt(), x, y, r) }
        add("Dữ liệu vào/ra", F, "data input output io") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.6f, -0.7f, -1f, 1f, -1f, 0.7f, 1f, -1f, 1f), c, w) }
        add("Chuẩn bị", F, "preparation hexagon") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.6f, -0.75f, -1f, 0.75f, -1f, 1f, 0f, 0.75f, 1f, -0.75f, 1f, -1f, 0f), c, w) }
        add("Tài liệu", F, "document paper") { d, x, y, r, c, w ->
            val top = norm(x, y, r * 1.3f, r * 0.7f, -1f, 0.75f, -1f, -1f, 1f, -1f, 1f, 0.75f)
            val wave = FloatArray(20 * 2) { k -> val i = k / 2; val t = i / 19.0; if (k % 2 == 0) x + r * 1.3f * (1 - 2 * t).toFloat() else y + r * 0.7f * (0.75f + 0.18f * sin(2 * PI * t).toFloat()) }
            poly(d, top + wave, c, w)
        }
        add("Nhập thủ công", F, "manual input") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.6f, -1f, -0.4f, 1f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Thao tác thủ công", F, "manual operation") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.3f, r * 0.6f, -1f, -1f, 1f, -1f, 0.7f, 1f, -0.7f, 1f), c, w) }
        add("Đầu nối", F, "connector circle on page") { d, x, y, r, c, w -> listOf(ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x, y, r * 0.4f, r * 0.4f, 0f), c, w)) }
        add("Nối trang", F, "off page connector") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 0.6f, r * 0.7f, -1f, -1f, 1f, -1f, 1f, 0.4f, 0f, 1f, -1f, 0.4f), c, w) }
        add("Trễ", F, "delay") { d, x, y, r, c, w ->
            val arc = FloatArray(17 * 2) { k -> val i = k / 2; val a = -PI / 2 + PI * i / 16; if (k % 2 == 0) x + r * 0.4f + r * 0.6f * cos(a).toFloat() else y + r * 0.6f * sin(a).toFloat() }
            poly(d, floatArrayOf(x - r, y - r * 0.6f) + arc + floatArrayOf(x - r, y + r * 0.6f), c, w)
        }

        // ---- Diagrams & tables
        add("Mindmap", D, "mind map y tuong") { d, x, y, r, _, _ ->
            val n = Node(d.nextId(), x, y, 10f, 10f, "", NodeKind.ROUND, 0xFFFFC857.toInt(), r * 0.3f, 0L, true).fitText("Ý tưởng trung tâm")
            listOf(n.copy(x = x - n.w / 2, y = y - n.h / 2))
        }
        add("Bảng 3×3", D, "table grid") { d, x, y, r, c, w -> listOf(TableEl(d.nextId(), x - r * 1.5f, y - r * 0.7f, r * 3f, r * 1.4f, 3, 3, c, w * 0.6f)) }
        add("Bảng 4×4", D, "table grid") { d, x, y, r, c, w -> listOf(TableEl(d.nextId(), x - r * 1.8f, y - r, r * 3.6f, r * 2f, 4, 4, c, w * 0.6f)) }
        add("Lớp UML", D, "uml class") { d, x, y, r, c, w ->
            listOf(TableEl(d.nextId(), x - r, y - r * 1.2f, r * 2f, r * 2.4f, 3, 1, c, w * 0.6f, mapOf(0 to "TênLớp", 1 to "+ thuộcTính", 2 to "+ phươngThức()")))
        }
        add("Làn bơi (swimlane)", D, "swimlane lane process") { d, x, y, r, c, w ->
            listOf(TableEl(d.nextId(), x - r * 3f, y - r * 1.5f, r * 6f, r * 3f, 3, 1, c, w * 0.6f, mapOf(0 to "Phòng A", 1 to "Phòng B", 2 to "Phòng C")))
        }
        add("Khung", D, "frame container group") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 3f, r * 2f, -1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f), c, w) }
        add("Ghi chú dán", D, "sticky note") { d, x, y, r, _, _ -> listOf(StickyEl(d.nextId(), x - r, y - r, r * 2f, r * 2f, "", 0xFFFFF59D.toInt(), r * 0.18f)) }
        add("Bóng thoại", D, "speech bubble callout chu thich") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 1.4f, r * 0.9f, -1f, -1f, 1f, -1f, 1f, 0.5f, -0.2f, 0.5f, -0.6f, 1f, -0.5f, 0.5f, -1f, 0.5f), c, w) }
        add("Trục thời gian", D, "timeline") { d, x, y, r, c, w ->
            val out = ArrayList<Element>()
            out += ShapeEl(d.nextId(), ShapeKind.ARROW, floatArrayOf(x - r * 3f, y, x + r * 3f, y), c, w)
            for (i in 0..4) out += ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x - r * 2.4f + i * r * 1.2f, y, r * 0.12f, r * 0.12f, 0f), c, w, c)
            out
        }

        // ---- Symbols
        add("Dấu tích", S, "check tick ok") { d, x, y, r, c, w -> poly(d, norm(x, y, r, r, -1f, 0f, -0.6f, -0.4f, -0.2f, 0.1f, 0.7f, -0.9f, 1f, -0.5f, -0.2f, 0.9f), c, w) }
        add("Dấu X", S, "x cross close sai") { d, x, y, r, c, w -> poly(d, norm(x, y, r, r, -1f, -0.7f, -0.7f, -1f, 0f, -0.3f, 0.7f, -1f, 1f, -0.7f, 0.3f, 0f, 1f, 0.7f, 0.7f, 1f, 0f, 0.3f, -0.7f, 1f, -1f, 0.7f, -0.3f, 0f), c, w) }
        add("Tia sét", S, "lightning bolt energy") { d, x, y, r, c, w -> poly(d, norm(x, y, r * 0.7f, r, 0.2f, -1f, -0.8f, 0.15f, -0.05f, 0.15f, -0.3f, 1f, 0.8f, -0.25f, 0.05f, -0.25f), c, w) }
        add("Ngôi nhà", S, "house home") { d, x, y, r, c, w -> poly(d, norm(x, y, r, r, 0f, -1f, 1f, -0.2f, 0.75f, -0.2f, 0.75f, 1f, -0.75f, 1f, -0.75f, -0.2f, -1f, -0.2f), c, w) }
        add("Ghim vị trí", S, "pin location map marker") { d, x, y, r, c, w ->
            val arc = FloatArray(25 * 2) { k -> val i = k / 2; val a = PI * 0.75 + 1.5 * PI * i / 24; if (k % 2 == 0) x + r * 0.6f * cos(a).toFloat() else y - r * 0.3f + r * 0.6f * sin(a).toFloat() }
            poly(d, arc + floatArrayOf(x, y + r), c, w)
        }
        add("Người", S, "person user actor nguoi dung") { d, x, y, r, c, w ->
            listOf(
                ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x, y - r * 0.65f, r * 0.3f, r * 0.3f, 0f), c, w),
                ShapeEl(d.nextId(), ShapeKind.POLYGON, norm(x, y, r * 0.6f, r, -1f, 1f, -1f, 0.2f, -0.5f, -0.2f, 0.5f, -0.2f, 1f, 0.2f, 1f, 1f), c, w),
            )
        }
        add("Mặt cười", S, "smile happy emoji") { d, x, y, r, c, w ->
            val smile = FloatArray(13 * 2) { k -> val i = k / 2; val a = PI * 0.2 + PI * 0.6 * i / 12; if (k % 2 == 0) x + r * 0.55f * cos(a).toFloat() else y + r * 0.55f * sin(a).toFloat() }
            listOf(
                ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x, y, r, r, 0f), c, w),
                ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x - r * 0.35f, y - r * 0.3f, r * 0.08f, r * 0.08f, 0f), c, w, c),
                ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x + r * 0.35f, y - r * 0.3f, r * 0.08f, r * 0.08f, 0f), c, w, c),
                Stroke(d.nextId(), FloatArray(13 * 3) { k -> if (k % 3 == 2) 0.6f else smile[(k / 3) * 2 + k % 3] }, c, w * 1.5f, PenType.PEN),
            )
        }
        add("Bóng đèn (ý tưởng)", S, "idea light bulb") { d, x, y, r, c, w ->
            listOf(
                ShapeEl(d.nextId(), ShapeKind.ELLIPSE, floatArrayOf(x, y - r * 0.25f, r * 0.6f, r * 0.6f, 0f), c, w),
                ShapeEl(d.nextId(), ShapeKind.POLYGON, norm(x, y, r * 0.3f, r, -1f, 0.35f, 1f, 0.35f, 1f, 0.75f, -1f, 0.75f), c, w),
            )
        }
        add("Cờ", S, "flag milestone moc") { d, x, y, r, c, w ->
            listOf(
                ShapeEl(d.nextId(), ShapeKind.LINE, floatArrayOf(x - r * 0.6f, y - r, x - r * 0.6f, y + r), c, w),
                ShapeEl(d.nextId(), ShapeKind.POLYGON, norm(x, y, r * 0.6f, r, -1f, -1f, 1f, -0.7f, -1f, -0.3f), c, w),
            )
        }
    }
}
