package com.inkboard.canvas

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RecordingCanvas
import android.graphics.RectF
import android.graphics.RenderNode
import android.os.SystemClock
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import com.inkboard.core.InkCore
import com.inkboard.core.RecognizedShape
import com.inkboard.model.Affine
import com.inkboard.model.Box
import com.inkboard.model.Document
import com.inkboard.model.DropRules
import com.inkboard.model.Edge
import com.inkboard.model.Element
import com.inkboard.model.Geo
import com.inkboard.model.Node
import com.inkboard.model.PenType
import com.inkboard.model.ShapeEl
import com.inkboard.model.ShapeKind
import com.inkboard.model.Stroke
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

enum class Tool { PEN, ERASER, SELECT }

/**
 * Infinite canvas. Coordinates: screen = world * scale + (tx, ty).
 *
 * Input model: the pen works (draw / erase / lasso), fingers navigate.
 * - 1 finger drag: pan (or drag the selection when started on it)
 * - 2 fingers: pinch zoom + pan; 2-finger tap = undo, 3-finger tap = redo
 * - finger held still while the pen draws = modifier (straight line / perfect shape)
 * - 2 fingers moving while the pen draws = navigate simultaneously
 * - pen held still ~0.5 s at the end of a stroke = snap to shape
 * - pen side button held = pan with the pen
 */
class InkCanvasView(context: Context) : View(context) {

    interface Listener {
        fun onSelectionChanged(screenRect: RectF?, ids: Set<Long>)
        fun onDocChanged()
        fun onCameraChanged(scale: Float)
        fun onEditRequest(el: Element, wx: Float = Float.NaN, wy: Float = Float.NaN)
        /** World position under the pen / last touch (for the coordinate bar). */
        fun onPointer(wx: Float, wy: Float) {}
    }

    var listener: Listener? = null
    var doc = Document()
        set(v) {
            field = v
            scale = v.camScale; tx = v.camX; ty = v.camY
            clearSelection()
            invalidateContent()
        }

    // Tool state
    var tool = Tool.PEN
        set(v) { field = v; if (v != Tool.SELECT) clearSelection(); invalidate() }
    var penColor = Color.BLACK
    var penWidthDp = 2.5f
    var penType = PenType.PEN
    var fingerDraws = false
    var autoShape = true
    var dark = false
    var showRulers = true
        set(v) { field = v; invalidate() }
    var showMinimap = true
        set(v) { field = v; invalidate() }
    private val nav = NavOverlay(resources.displayMetrics.density)
    private var pointerSX = -1f
    private var pointerSY = -1f
    private var mapNavigating = false

    // Camera
    var scale = 1f; private set
    var tx = 0f; private set
    var ty = 0f; private set
    private val density = resources.displayMetrics.density

    private val renderer = Renderer()

    // Selection
    val selection = LinkedHashSet<Long>()
    var primaryId = 0L; private set
    private var selPreview = Affine.IDENTITY

    // Render caches (RenderNode chunks of committed content, recorded in world space)
    private val chunkSize = 1500
    private val chunks = ArrayList<RenderNode>()
    private val chunkKeys = ArrayList<Array<Any?>>()
    private var contentDirty = true
    private val hidden = HashSet<Long>()

    // ------------------------------------------------------------------ input
    private enum class PenMode { NONE, DRAW, LINE, ERASE, LASSO, DRAG, SCALE, PAN }

    private var penPointer = -1
    private var penMode = PenMode.NONE
    private var livePts = FloatArray(3 * 512)
    private var liveN = 0
    private var liveWidth = 0f
    private var snapped: ShapeEl? = null
    private var holdX = 0f
    private var holdY = 0f
    private val lasso = ArrayList<Float>()
    private var dragStartWX = 0f
    private var dragStartWY = 0f
    private var anchorWX = 0f
    private var anchorWY = 0f
    private var scaleStartDist = 1f
    private var lastPenSX = 0f
    private var lastPenSY = 0f
    private var lastHoverTime = Long.MIN_VALUE / 2
    private var hoverX = -1f
    private var hoverY = -1f
    private var penPanX = 0f
    private var penPanY = 0f

    private class Finger(val id: Int, var sx: Float, var sy: Float) {
        var x = sx; var y = sy; var moved = false
    }
    private val fingers = LinkedHashMap<Int, Finger>()
    private var fingerGesture = 0 // 0 none, 1 pan, 2 pinch, 3 drag selection
    private var maxFingers = 0
    /** True once the pen was used while fingers were down (finger = modifier, not a tap). */
    private var penUsedWithFingers = false
    private var fingerDownTime = 0L
    private var gestureCX = 0f
    private var gestureCY = 0f
    private var gestureSpan = 0f
    private var lastTapTime = 0L
    private var lastTapId = 0L
    private var fingerDragWX = 0f
    private var fingerDragWY = 0f

    private val touchSlop = 8f * density
    private val handleR = 16f * density
    private val eraserR get() = 12f * density / scale

    private val holdRunnable = Runnable { onPenHold() }
    private var dropAction: DropRules.Action? = null
    private var dropSX = 0f
    private var dropSY = 0f
    private val hintPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { textSize = 14f * resources.displayMetrics.density; color = Color.WHITE }
    private val autoScroll = object : Runnable {
        override fun run() { stepAutoScroll() }
    }
    private var autoScrolling = false

    // Paints for overlays
    private val overlay = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 1.5f * density }
    private val overlayFill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val lassoPath = Path()

    init {
        isFocusable = true
        isClickable = true
    }

    // ----------------------------------------------------------- coordinates
    private fun wx(sx: Float) = (sx - tx) / scale
    private fun wy(sy: Float) = (sy - ty) / scale
    private fun sx(wx: Float) = wx * scale + tx
    private fun sy(wy: Float) = wy * scale + ty

    fun viewBox(): Box = Box(wx(0f), wy(0f), wx(width.toFloat()), wy(height.toFloat()))

    private fun saveCamera() {
        doc.camScale = scale; doc.camX = tx; doc.camY = ty
        listener?.onCameraChanged(scale)
    }

    private fun zoomAround(factor: Float, cx: Float, cy: Float) {
        val ns = (scale * factor).coerceIn(MIN_SCALE, MAX_SCALE)
        val f = ns / scale
        tx = cx - (cx - tx) * f
        ty = cy - (cy - ty) * f
        scale = ns
    }

    fun panBy(dx: Float, dy: Float) {
        tx += dx; ty += dy
        saveCamera(); notifySelection(); invalidate()
    }

    /** Fit all content on screen (or reset when empty). */
    fun zoomToContent() = fitContent(2f)

    private fun fitContent(maxScale: Float) {
        val b = doc.contentBounds()
        if (b.isEmpty || width == 0) {
            scale = 1f; tx = width / 2f; ty = height / 2f
        } else {
            val pad = 48f * density
            val s = min((width - 2 * pad) / max(b.w, 1f), (height - 2 * pad) / max(b.h, 1f)).coerceIn(MIN_SCALE, maxScale)
            scale = s
            tx = width / 2f - b.cx * s
            ty = height / 2f - b.cy * s
        }
        saveCamera(); notifySelection(); invalidate()
    }

    fun setZoom(s: Float) {
        zoomAround(s / scale, width / 2f, height / 2f)
        saveCamera(); notifySelection(); invalidate()
    }

    /** Bring a world box into view (used by the components panel). */
    fun focusOn(b: Box) {
        if (b.isEmpty || width == 0) return
        val pad = 64f * density
        val s = min((width - 2 * pad) / max(b.w, 1f), (height - 2 * pad) / max(b.h, 1f)).coerceIn(MIN_SCALE, 1.5f)
        scale = s
        tx = width / 2f - b.cx * s
        ty = height / 2f - b.cy * s
        saveCamera(); notifySelection(); invalidate()
    }

    /** Centre the view on a world point, keeping the zoom. */
    fun centerOn(x: Float, y: Float) {
        tx = width / 2f - x * scale
        ty = height / 2f - y * scale
        saveCamera(); notifySelection(); invalidate()
    }

    private fun insets(): android.graphics.Insets? =
        rootWindowInsets?.getInsets(android.view.WindowInsets.Type.systemBars() or android.view.WindowInsets.Type.displayCutout())

    /** Minimap rectangle on screen (valid after the first draw). */
    fun minimapRect(): RectF = RectF(nav.mapRect)

    /** Height reserved at the top by the ruler (px), 0 when hidden. */
    val rulerSizePx get() = nav.rulerSize

    private fun reportPointer(sx: Float, sy: Float) {
        pointerSX = sx; pointerSY = sy
        listener?.onPointer(wx(sx), wy(sy))
    }

    /** World point at the centre of the screen. */
    fun centerWorld(): Pair<Float, Float> = wx(width / 2f) to wy(height / 2f)

    // ----------------------------------------------------------- selection API

    fun select(ids: Collection<Long>, primary: Long = ids.firstOrNull() ?: 0L) {
        selection.clear()
        for (id in ids) {
            selection += id
            val n = doc.byId(id) as? Node
            if (n != null && n.mindmap) selection += doc.descendants(id)
        }
        // Items attached to a selected container travel with it.
        selection += doc.attachedTree(selection)
        // Edges whose both ends are selected travel with them.
        for (e in doc.elements) if (e is Edge && e.from in selection && e.to in selection) selection += e.id
        primaryId = if (primary in selection) primary else selection.firstOrNull() ?: 0L
        if (selection.isNotEmpty() && tool != Tool.SELECT) {
            // keep the current tool, selection is still usable via the context bar
        }
        notifySelection(); invalidate()
    }

    fun clearSelection() {
        if (selection.isEmpty()) return
        selection.clear(); primaryId = 0L
        notifySelection(); invalidate()
    }

    fun selectionScreenRect(): RectF? {
        if (selection.isEmpty()) return null
        val b = doc.boundsOf(selection)
        if (b.isEmpty) return null
        val bb = b.map(selPreview)
        return RectF(sx(bb.l), sy(bb.t), sx(bb.r), sy(bb.b))
    }

    private fun notifySelection() {
        listener?.onSelectionChanged(selectionScreenRect(), selection.toSet())
    }

    /** Call after any document change made outside the view. */
    fun onDocMutated() {
        selection.retainAll(doc.elements.map { it.id }.toSet())
        if (primaryId !in selection) primaryId = selection.firstOrNull() ?: 0L
        invalidateContent()
        notifySelection()
        listener?.onDocChanged()
    }

    fun invalidateContent() {
        contentDirty = true
        invalidate()
    }

    // ---------------------------------------------------------------- drawing

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        if (oldw == 0 && doc.camScale == 1f && doc.camX == 0f && doc.camY == 0f) {
            if (doc.elements.isEmpty()) { tx = w / 2f; ty = h / 2f; saveCamera() } else fitContent(1f)
        }
        notifySelection()
    }

    override fun onDraw(c: Canvas) {
        renderer.drawBackground(c, doc.background, width, height, scale, tx, ty, dark)
        if (showRulers) nav.drawOrigin(c, scale, tx, ty, dark) // under the content
        val previewing = !selPreview.isIdentity
        c.save()
        c.translate(tx, ty)
        c.scale(scale, scale)

        // Connections (edges + mind-map branches) follow moving nodes live.
        renderer.drawConnections(c, doc) { id ->
            val n = doc.byId(id) as? Node
            if (n != null && previewing && id in selection) n.transformed(selPreview) as Node else n
        }

        if (c.isHardwareAccelerated) {
            drawChunks(c, previewing)
        } else {
            val vb = viewBox().inset(-50f / scale)
            for (e in doc.elements) {
                if (e.id in hidden || (previewing && e.id in selection)) continue
                if (!doc.boundsOf(e).intersects(vb)) continue
                renderer.draw(c, doc, e)
            }
        }

        if (previewing) {
            c.save()
            c.translate(selPreview.tx, selPreview.ty)
            c.scale(selPreview.s, selPreview.s)
            for (id in selection) doc.byId(id)?.let { renderer.draw(c, doc, it) }
            c.restore()
        }

        // Live input
        when {
            snapped != null -> renderer.drawShape(c, snapped!!)
            penMode == PenMode.DRAW && liveN > 0 -> renderer.drawLiveStroke(c, livePts, liveN, penColor, liveWidth, penType)
        }
        c.restore()

        drawOverlays(c)
    }

    /** Committed content is recorded into world-space RenderNodes and replayed. */
    private fun drawChunks(c: Canvas, previewing: Boolean) {
        val els = doc.elements
        val need = (els.size + chunkSize - 1) / chunkSize
        while (chunks.size < need) { chunks.add(RenderNode("ink${chunks.size}")); chunkKeys.add(arrayOf()) }
        for (k in 0 until need) {
            val from = k * chunkSize
            val to = min(els.size, from + chunkSize)
            // Key: element identities + hidden/preview flags. Re-record only on change.
            val key = arrayOfNulls<Any?>(to - from)
            for (i in from until to) {
                val e = els[i]
                key[i - from] = if (e.id in hidden || (previewing && e.id in selection)) null else e
            }
            val node = chunks[k]
            if (contentDirty || !key.contentEquals(chunkKeys[k])) {
                node.setPosition(-1_000_000, -1_000_000, 1_000_000, 1_000_000)
                node.setClipToBounds(false)
                val rc: RecordingCanvas = node.beginRecording()
                rc.translate(1_000_000f, 1_000_000f)
                for (e in key) if (e != null && e !is Edge) renderer.draw(rc, doc, e as Element)
                node.endRecording()
                chunkKeys[k] = key
            }
            c.drawRenderNode(node)
        }
        contentDirty = false
    }

    private fun drawOverlays(c: Canvas) {
        dropAction?.let { act ->
            if (selPreview.isIdentity) return@let
            doc.byId(act.targetId)?.let { t ->
                val b = doc.boundsOf(t)
                val r = RectF(sx(b.l), sy(b.t), sx(b.r), sy(b.b))
                r.inset(-4f * density, -4f * density)
                overlayFill.color = 0x262F80ED
                c.drawRoundRect(r, 8f * density, 8f * density, overlayFill)
                overlay.color = 0xFF2F80ED.toInt(); overlay.strokeWidth = 3f * density
                c.drawRoundRect(r, 8f * density, 8f * density, overlay)
                overlay.strokeWidth = 1.5f * density
            }
            // Hint bubble near the pointer
            val tw = hintPaint.measureText(act.hint)
            val pad = 10f * density
            val bx = (dropSX - tw / 2 - pad).coerceIn(4f, width - tw - 2 * pad - 4f)
            val by = (dropSY - 64f * density).coerceAtLeast(4f)
            overlayFill.color = 0xE61F3A5F.toInt()
            c.drawRoundRect(bx, by, bx + tw + 2 * pad, by + 30f * density, 15f * density, 15f * density, overlayFill)
            c.drawText(act.hint, bx + pad, by + 20f * density, hintPaint)
        }
        // Selection frame + corner handles
        selectionScreenRect()?.let { r ->
            r.inset(-6f * density, -6f * density)
            overlay.color = 0xFF2F80ED.toInt(); overlay.pathEffect = Renderer.dash
            c.drawRect(r, overlay)
            overlay.pathEffect = null
            overlayFill.color = Color.WHITE
            for ((hx, hy) in corners(r)) {
                c.drawCircle(hx, hy, 7f * density, overlayFill)
                c.drawCircle(hx, hy, 7f * density, overlay)
            }
        }
        if (penMode == PenMode.LASSO && lasso.size >= 4) {
            lassoPath.reset()
            lassoPath.moveTo(sx(lasso[0]), sy(lasso[1]))
            var i = 2
            while (i + 1 < lasso.size) { lassoPath.lineTo(sx(lasso[i]), sy(lasso[i + 1])); i += 2 }
            overlayFill.color = 0x182F80ED
            c.drawPath(lassoPath, overlayFill)
            overlay.color = 0xFF2F80ED.toInt(); overlay.pathEffect = Renderer.dash
            c.drawPath(lassoPath, overlay)
            overlay.pathEffect = null
        }
        if (penMode == PenMode.ERASE || (hoverX >= 0 && tool == Tool.ERASER && penPointer < 0)) {
            val x = if (penMode == PenMode.ERASE) lastPenSX else hoverX
            val y = if (penMode == PenMode.ERASE) lastPenSY else hoverY
            overlay.color = 0x99000000.toInt()
            c.drawCircle(x, y, 12f * density, overlay)
        } else if (hoverX >= 0 && tool == Tool.PEN && penPointer < 0) {
            overlayFill.color = penColor
            c.drawCircle(hoverX, hoverY, max(1.5f * density, penWidthDp * density / 2), overlayFill)
        }
        val ins = insets()
        if (showRulers) {
            nav.drawRulers(c, width, height, (ins?.left ?: 0).toFloat(), (ins?.top ?: 0).toFloat(), scale, tx, ty, pointerSX, pointerSY, dark)
        }
        if (showMinimap) {
            nav.layoutMap(width, height, (ins?.right ?: 0).toFloat(), (ins?.bottom ?: 0).toFloat())
            nav.drawMinimap(c, doc, viewBox(), renderer, dark, SystemClock.uptimeMillis())
        }
    }

    private fun corners(r: RectF) = listOf(r.left to r.top, r.right to r.top, r.right to r.bottom, r.left to r.bottom)

    // ------------------------------------------------------------------ hover

    override fun onHoverEvent(e: MotionEvent): Boolean {
        if (e.getToolType(0) == MotionEvent.TOOL_TYPE_STYLUS || e.getToolType(0) == MotionEvent.TOOL_TYPE_ERASER) {
            lastHoverTime = SystemClock.uptimeMillis()
            if (e.actionMasked == MotionEvent.ACTION_HOVER_EXIT) { hoverX = -1f; hoverY = -1f } else { hoverX = e.x; hoverY = e.y; reportPointer(e.x, e.y) }
            invalidate()
            return true
        }
        return super.onHoverEvent(e)
    }

    private fun penNear() = penPointer >= 0 || SystemClock.uptimeMillis() - lastHoverTime < 400

    // ------------------------------------------------------------------ touch

    private fun isPenTool(e: MotionEvent, idx: Int): Boolean {
        val t = e.getToolType(idx)
        return t == MotionEvent.TOOL_TYPE_STYLUS || t == MotionEvent.TOOL_TYPE_ERASER || t == MotionEvent.TOOL_TYPE_MOUSE
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (handleMinimapTouch(e)) return true
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val idx = e.actionIndex
                val pid = e.getPointerId(idx)
                val asPen = isPenTool(e, idx) || (fingerDraws && fingers.isEmpty() && penPointer < 0 && e.pointerCount == 1)
                if (asPen && penPointer < 0) {
                    requestUnbufferedDispatch(e)
                    penPointer = pid
                    penDown(e, idx)
                } else if (!isPenTool(e, idx)) {
                    fingerDown(e, idx, pid)
                }
            }
            MotionEvent.ACTION_MOVE -> {
                val pIdx = if (penPointer >= 0) e.findPointerIndex(penPointer) else -1
                if (pIdx >= 0) penMove(e, pIdx)
                if (fingers.isNotEmpty()) fingerMove(e)
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> {
                val idx = e.actionIndex
                val pid = e.getPointerId(idx)
                val canceled = (e.flags and MotionEvent.FLAG_CANCELED) != 0
                if (pid == penPointer) {
                    penMove(e, idx)
                    if (canceled) penCancel() else penUp()
                    penPointer = -1
                } else if (fingers.containsKey(pid)) {
                    fingerUp(pid, canceled)
                }
            }
            MotionEvent.ACTION_CANCEL -> {
                if (penPointer >= 0) { penCancel(); penPointer = -1 }
                fingers.clear(); fingerGesture = 0
                selPreview = Affine.IDENTITY
                stopAutoScroll()
                invalidate()
            }
        }
        return true
    }

    /** Tap or drag inside the minimap to move the view there. */
    private fun handleMinimapTouch(e: MotionEvent): Boolean {
        if (e.actionMasked == MotionEvent.ACTION_DOWN && showMinimap && nav.mapRect.contains(e.x, e.y) && penPointer < 0 && fingers.isEmpty()) {
            mapNavigating = true
        }
        if (!mapNavigating) return false
        val (x, y) = nav.mapToWorld(e.x.coerceIn(nav.mapRect.left, nav.mapRect.right), e.y.coerceIn(nav.mapRect.top, nav.mapRect.bottom))
        centerOn(x, y)
        if (e.actionMasked == MotionEvent.ACTION_UP || e.actionMasked == MotionEvent.ACTION_CANCEL) mapNavigating = false
        return true
    }

    // --------------------------------------------------------------- pen side

    private fun modifierHeld() = fingers.values.any { !it.moved }

    private fun penDown(e: MotionEvent, idx: Int) {
        val x = e.getX(idx); val y = e.getY(idx)
        if (fingers.isNotEmpty()) penUsedWithFingers = true
        lastPenSX = x; lastPenSY = y
        hoverX = -1f
        val button = (e.buttonState and (MotionEvent.BUTTON_STYLUS_PRIMARY or MotionEvent.BUTTON_SECONDARY)) != 0
        val eraserTip = e.getToolType(idx) == MotionEvent.TOOL_TYPE_ERASER
        penMode = when {
            button -> PenMode.PAN
            eraserTip || tool == Tool.ERASER -> PenMode.ERASE
            tool == Tool.SELECT -> selectModeAt(x, y)
            else -> PenMode.DRAW
        }
        when (penMode) {
            PenMode.PAN -> { penPanX = x; penPanY = y }
            PenMode.DRAW -> {
                clearSelection()
                liveN = 0
                liveWidth = penWidthDp * density / scale * if (penType == PenType.HIGHLIGHTER) 6f else 1f
                snapped = null
                addLivePoint(wx(x), wy(y), pressure(e, idx, -1))
                holdX = x; holdY = y
                if (modifierHeld()) penMode = PenMode.LINE
                scheduleHold()
            }
            PenMode.ERASE -> { hidden.clear(); eraseAt(wx(x), wy(y)) }
            PenMode.LASSO -> { lasso.clear(); lasso += wx(x); lasso += wy(y) }
            PenMode.DRAG, PenMode.SCALE -> Unit
            else -> Unit
        }
        invalidate()
    }

    /** SELECT tool: decide between scaling, dragging and a new lasso. */
    private fun selectModeAt(x: Float, y: Float): PenMode {
        val r = selectionScreenRect()
        if (r != null) {
            r.inset(-6f * density, -6f * density)
            val cs = corners(r)
            for ((i, p) in cs.withIndex()) {
                if (hypot(x - p.first, y - p.second) <= handleR) {
                    val opp = cs[(i + 2) % 4]
                    anchorWX = wx(opp.first); anchorWY = wy(opp.second)
                    scaleStartDist = max(1f, hypot(wx(x) - anchorWX, wy(y) - anchorWY))
                    return PenMode.SCALE
                }
            }
            if (r.contains(x, y)) {
                dragStartWX = wx(x); dragStartWY = wy(y)
                return PenMode.DRAG
            }
        }
        return PenMode.LASSO
    }

    private fun pressure(e: MotionEvent, idx: Int, h: Int): Float {
        val p = if (h < 0) e.getPressure(idx) else e.getHistoricalPressure(idx, h)
        return if (e.getToolType(idx) == MotionEvent.TOOL_TYPE_FINGER) 0.6f else p.coerceIn(0.05f, 1f)
    }

    private fun addLivePoint(x: Float, y: Float, p: Float) {
        if (liveN > 0) {
            val lx = livePts[3 * (liveN - 1)]; val ly = livePts[3 * (liveN - 1) + 1]
            if (hypot(x - lx, y - ly) < 0.8f / scale) {
                livePts[3 * (liveN - 1) + 2] = max(livePts[3 * (liveN - 1) + 2], p)
                return
            }
        }
        if (3 * (liveN + 1) > livePts.size) livePts = livePts.copyOf(livePts.size * 2)
        livePts[3 * liveN] = x; livePts[3 * liveN + 1] = y; livePts[3 * liveN + 2] = p
        liveN++
    }

    private fun penMove(e: MotionEvent, idx: Int) {
        if (penMode == PenMode.NONE) return
        val hs = e.historySize
        for (h in 0..hs) {
            val x = if (h < hs) e.getHistoricalX(idx, h) else e.getX(idx)
            val y = if (h < hs) e.getHistoricalY(idx, h) else e.getY(idx)
            when (penMode) {
                PenMode.DRAW -> {
                    if (snapped != null) continue
                    addLivePoint(wx(x), wy(y), pressure(e, idx, if (h < hs) h else -1))
                    if (hypot(x - holdX, y - holdY) > 6f * density) { holdX = x; holdY = y; scheduleHold() }
                    if (modifierHeld()) penMode = PenMode.LINE
                }
                PenMode.LINE -> {
                    addLivePoint(wx(x), wy(y), pressure(e, idx, if (h < hs) h else -1))
                }
                PenMode.ERASE -> {
                    val px = wx(lastPenSX); val py = wy(lastPenSY); val qx = wx(x); val qy = wy(y)
                    val d = hypot(qx - px, qy - py)
                    val steps = max(1, (d / (eraserR * 0.5f)).toInt())
                    for (s in 1..steps) eraseAt(px + (qx - px) * s / steps, py + (qy - py) * s / steps)
                }
                PenMode.LASSO -> {
                    val wxp = wx(x); val wyp = wy(y)
                    val n = lasso.size
                    if (hypot(sx(wxp) - sx(lasso[n - 2]), sy(wyp) - sy(lasso[n - 1])) > 3f) { lasso += wxp; lasso += wyp }
                }
                PenMode.DRAG -> updateDrag(x, y)
                PenMode.SCALE -> {
                    val f = (hypot(wx(x) - anchorWX, wy(y) - anchorWY) / scaleStartDist).coerceIn(0.05f, 50f)
                    selPreview = Affine.scaleAround(f, anchorWX, anchorWY)
                }
                PenMode.PAN -> {
                    tx += x - penPanX; ty += y - penPanY
                    penPanX = x; penPanY = y
                    saveCamera()
                }
                PenMode.NONE -> Unit
            }
            lastPenSX = x; lastPenSY = y
        }
        reportPointer(lastPenSX, lastPenSY)
        if (penMode == PenMode.LINE) updateLinePreview()
        if (penMode == PenMode.DRAG || penMode == PenMode.SCALE) notifySelection()
        invalidate()
    }

    private fun updateDrag(x: Float, y: Float) {
        selPreview = Affine.translate(wx(x) - dragStartWX, wy(y) - dragStartWY)
        updateDropHint(x, y)
        val edge = 48f * density
        val nearEdge = x < edge || y < edge || x > width - edge || y > height - edge
        if (nearEdge && !autoScrolling) { autoScrolling = true; postOnAnimation(autoScroll) }
    }

    private fun stepAutoScroll() {
        val dragging = penMode == PenMode.DRAG || fingerGesture == 3
        if (!dragging) { autoScrolling = false; return }
        val (x, y) = if (penMode == PenMode.DRAG) lastPenSX to lastPenSY else fingers.values.first().let { it.x to it.y }
        val edge = 48f * density
        val speed = 14f * density
        var dx = 0f; var dy = 0f
        if (x < edge) dx = speed * (1 - x / edge)
        if (x > width - edge) dx = -speed * (1 - (width - x) / edge)
        if (y < edge) dy = speed * (1 - y / edge)
        if (y > height - edge) dy = -speed * (1 - (height - y) / edge)
        if (dx == 0f && dy == 0f) { autoScrolling = false; return }
        tx += dx; ty += dy
        saveCamera()
        if (penMode == PenMode.DRAG) selPreview = Affine.translate(wx(x) - dragStartWX, wy(y) - dragStartWY)
        else selPreview = Affine.translate(wx(x) - fingerDragWX, wy(y) - fingerDragWY)
        updateDropHint(x, y)
        notifySelection()
        invalidate()
        postOnAnimation(autoScroll)
    }

    private fun stopAutoScroll() {
        removeCallbacks(autoScroll); autoScrolling = false
    }

    private fun updateLinePreview() {
        if (liveN == 0) return
        val x0 = livePts[0]; val y0 = livePts[1]
        val x1 = livePts[3 * (liveN - 1)]; val y1 = livePts[3 * (liveN - 1) + 1]
        val len = hypot(x1 - x0, y1 - y0)
        val ang = Geo.snapAngle(atan2((y1 - y0).toDouble(), (x1 - x0).toDouble()), Math.PI / 12)
        val ex = x0 + (len * cos(ang)).toFloat(); val ey = y0 + (len * sin(ang)).toFloat()
        snapped = ShapeEl(0, ShapeKind.LINE, floatArrayOf(x0, y0, ex, ey), penColor, strokeWidthForShapes())
    }

    private fun strokeWidthForShapes() = max(liveWidth, 1.5f * density / scale) * if (penType == PenType.HIGHLIGHTER) 0.25f else 1f

    private fun scheduleHold() {
        removeCallbacks(holdRunnable)
        if (autoShape && penMode == PenMode.DRAW) postDelayed(holdRunnable, HOLD_MS)
    }

    /** Pen held still: try to snap the live stroke to a clean shape. */
    private fun onPenHold() {
        if (penMode != PenMode.DRAW || snapped != null || liveN < 4) return
        val xy = FloatArray(liveN * 2) { k -> livePts[(k / 2) * 3 + (k % 2)] }
        val shape = InkCore.recognize(xy, modifierHeld()) ?: return
        val w = strokeWidthForShapes()
        snapped = when (shape) {
            is RecognizedShape.Line -> ShapeEl(0, ShapeKind.LINE, floatArrayOf(shape.x0, shape.y0, shape.x1, shape.y1), penColor, w)
            is RecognizedShape.Polygon -> ShapeEl(0, ShapeKind.POLYGON, shape.pts, penColor, w)
            is RecognizedShape.Ellipse -> ShapeEl(0, ShapeKind.ELLIPSE, floatArrayOf(shape.cx, shape.cy, shape.rx, shape.ry, shape.angle), penColor, w)
        }
        performHapticFeedback(HapticFeedbackConstants.CONFIRM)
        invalidate()
    }

    private fun eraseAt(x: Float, y: Float) {
        val r = eraserR
        var changed = false
        for (el in doc.elements) {
            if (el.id in hidden) continue
            val hit = if (el is Edge) {
                doc.edgeEnds(el)?.let { Geo.distToSegment(x, y, it[0], it[1], it[2], it[3]) <= r + el.width } ?: false
            } else {
                val b = el.bounds
                x >= b.l - r && x <= b.r + r && y >= b.t - r && y <= b.b + r && el.hit(x, y, r, solid = false)
            }
            if (hit) { hidden += el.id; changed = true }
        }
        if (changed) invalidate()
    }

    private fun penUp() {
        removeCallbacks(holdRunnable)
        stopAutoScroll()
        when (penMode) {
            PenMode.DRAW, PenMode.LINE -> {
                val shape = snapped
                val el: Element? = if (shape != null) ShapeEl(doc.nextId(), shape.kind, shape.geo, shape.color, shape.width, shape.fill)
                else if (liveN > 0) Stroke(doc.nextId(), livePts.copyOf(liveN * 3), penColor, liveWidth, penType) else null
                if (el != null) doc.batch {
                    doc.add(el)
                    // Ink written fully inside a shape / block / note / table belongs to it.
                    DropRules.enclosingContainer(doc, el.bounds, el.id)?.let { doc.setContainer(el.id, it.id) }
                }
                liveN = 0; snapped = null
                changed()
            }
            PenMode.ERASE -> {
                if (hidden.isNotEmpty()) {
                    val ids = hidden.toList()
                    hidden.clear()
                    doc.remove(ids)
                    changed()
                }
            }
            PenMode.LASSO -> finishLasso()
            PenMode.DRAG -> commitPreview(lastPenSX, lastPenSY)
            PenMode.SCALE -> commitPreview()
            PenMode.PAN -> Unit
            PenMode.NONE -> Unit
        }
        penMode = PenMode.NONE
        invalidate()
    }

    private fun penCancel() {
        removeCallbacks(holdRunnable)
        stopAutoScroll()
        liveN = 0; snapped = null; hidden.clear(); lasso.clear()
        selPreview = Affine.IDENTITY
        penMode = PenMode.NONE
        invalidateContent()
    }

    /** Commit a drag/scale preview; for drags, apply the drop rule at the pointer as the same undo step. */
    private fun commitPreview(dropSX: Float = Float.NaN, dropSY: Float = Float.NaN) {
        val a = selPreview
        selPreview = Affine.IDENTITY
        val action = dropAction
        dropAction = null
        if (!a.isIdentity) {
            var reselect: List<Long>? = null
            doc.batch {
                doc.transform(selection.filter { doc.byId(it) !is Edge }, a)
                if (action != null && !dropSX.isNaN()) reselect = DropRules.apply(doc, action)
            }
            changed()
            reselect?.let { ids -> if (ids.isNotEmpty()) select(ids, ids.first()) }
            if (action != null) performHapticFeedback(HapticFeedbackConstants.CONFIRM)
        }
        notifySelection()
    }

    /** Recompute the drop target while dragging (pointer in screen coords). */
    private fun updateDropHint(sx: Float, sy: Float) {
        dropSX = sx; dropSY = sy
        dropAction = if (selection.isEmpty()) null else DropRules.resolve(doc, selection, primaryId, wx(sx), wy(sy))
    }

    private fun finishLasso() {
        val pts = lasso.toFloatArray()
        lasso.clear()
        if (pts.size < 2) return
        var len = 0f
        var i = 2
        while (i + 1 < pts.size) { len += hypot(pts[i] - pts[i - 2], pts[i + 1] - pts[i - 1]) * scale; i += 2 }
        if (len < 12f * density) {
            tapSelect(pts[0], pts[1])
            return
        }
        val ids = doc.elements.filter { it.inLasso(pts) }.map { it.id }
        if (ids.isEmpty()) clearSelection() else select(ids)
    }

    private fun tapSelect(wx: Float, wy: Float) {
        val hit = doc.hitTop(wx, wy, 6f * density / scale)
        if (hit == null) { clearSelection(); return }
        val now = SystemClock.uptimeMillis()
        if (hit.id == lastTapId && now - lastTapTime < 400) {
            listener?.onEditRequest(hit, wx, wy)
            lastTapTime = 0
        } else {
            lastTapTime = now; lastTapId = hit.id
        }
        select(listOf(hit.id), hit.id)
    }

    private fun changed() {
        invalidateContent()
        notifySelection()
        listener?.onDocChanged()
    }

    // ------------------------------------------------------------ finger side

    private fun fingerDown(e: MotionEvent, idx: Int, pid: Int) {
        val f = Finger(pid, e.getX(idx), e.getY(idx))
        fingers[pid] = f
        if (fingers.size == 1) {
            maxFingers = 1
            penUsedWithFingers = penPointer >= 0
            fingerDownTime = SystemClock.uptimeMillis()
            fingerGesture = 0
        } else {
            maxFingers = max(maxFingers, fingers.size)
            // Rebase gesture origins so a pan that becomes a pinch does not jump.
            for (o in fingers.values) { o.sx = o.x; o.sy = o.y }
            if (fingerGesture == 3) { // second finger while dragging: commit drag, start pinch
                commitPreview()
            }
            if (fingerGesture != 2) fingerGesture = 0
        }
        resetPinchBaseline()
    }

    private fun resetPinchBaseline() {
        val fs = fingers.values.take(2)
        if (fs.size >= 2) {
            gestureCX = (fs[0].x + fs[1].x) / 2; gestureCY = (fs[0].y + fs[1].y) / 2
            gestureSpan = max(1f, hypot(fs[0].x - fs[1].x, fs[0].y - fs[1].y))
        } else if (fs.size == 1) {
            gestureCX = fs[0].x; gestureCY = fs[0].y
        }
    }

    private fun fingerMove(e: MotionEvent) {
        for (f in fingers.values) {
            val i = e.findPointerIndex(f.id)
            if (i < 0) continue
            f.x = e.getX(i); f.y = e.getY(i)
            if (!f.moved && hypot(f.x - f.sx, f.y - f.sy) > touchSlop) f.moved = true
        }
        val anyMoved = fingers.values.any { it.moved }
        if (!anyMoved) return

        if (fingers.size >= 2) {
            // Pinch / two-finger pan (also allowed while the pen draws).
            val fs = fingers.values.take(2)
            if (fingerGesture != 2) {
                // Start from the touch-down positions so no movement is lost to the slop.
                fingerGesture = 2
                gestureCX = (fs[0].sx + fs[1].sx) / 2; gestureCY = (fs[0].sy + fs[1].sy) / 2
                gestureSpan = max(1f, hypot(fs[0].sx - fs[1].sx, fs[0].sy - fs[1].sy))
            }
            val cx = (fs[0].x + fs[1].x) / 2; val cy = (fs[0].y + fs[1].y) / 2
            val span = max(1f, hypot(fs[0].x - fs[1].x, fs[0].y - fs[1].y))
            zoomAround(span / gestureSpan, gestureCX, gestureCY)
            tx += cx - gestureCX; ty += cy - gestureCY
            gestureCX = cx; gestureCY = cy; gestureSpan = span
            saveCamera(); notifySelection(); invalidate()
            return
        }

        // Single finger: ignored while the pen is near (palm safety).
        if (penNear()) return
        val f = fingers.values.first()
        if (fingerGesture == 0) {
            val r = selectionScreenRect()?.apply { inset(-6f * density, -6f * density) }
            if (r != null && r.contains(f.sx, f.sy)) {
                fingerGesture = 3
                fingerDragWX = wx(f.sx); fingerDragWY = wy(f.sy)
            } else {
                fingerGesture = 1
                gestureCX = f.sx; gestureCY = f.sy
            }
        }
        when (fingerGesture) {
            1 -> {
                tx += f.x - gestureCX; ty += f.y - gestureCY
                gestureCX = f.x; gestureCY = f.y
                saveCamera(); notifySelection(); invalidate()
            }
            3 -> {
                selPreview = Affine.translate(wx(f.x) - fingerDragWX, wy(f.y) - fingerDragWY)
                updateDropHint(f.x, f.y)
                val edge = 48f * density
                if ((f.x < edge || f.y < edge || f.x > width - edge || f.y > height - edge) && !autoScrolling) {
                    autoScrolling = true; postOnAnimation(autoScroll)
                }
                notifySelection(); invalidate()
            }
        }
    }

    private fun fingerUp(pid: Int, canceled: Boolean) {
        val f = fingers.remove(pid) ?: return
        if (fingers.isNotEmpty()) {
            resetPinchBaseline()
            if (fingerGesture == 2 && fingers.size == 1) {
                // Continue as a pan with the remaining finger.
                fingerGesture = 1
                fingers.values.first().let { gestureCX = it.x; gestureCY = it.y; it.moved = true }
            }
            return
        }
        // Last finger lifted.
        stopAutoScroll()
        if (fingerGesture == 3) commitPreview(f.x, f.y)
        val wasTap = !f.moved && fingerGesture == 0 && !canceled && !penUsedWithFingers &&
            SystemClock.uptimeMillis() - fingerDownTime < TAP_MS
        if (wasTap && penPointer < 0) {
            when (maxFingers) {
                2 -> performUndoRequest()
                3 -> performRedoRequest()
                1 -> if (!penNear()) tapSelect(wx(f.sx), wy(f.sy))
            }
        }
        fingerGesture = 0
    }

    var onUndoGesture: (() -> Unit)? = null
    var onRedoGesture: (() -> Unit)? = null
    private fun performUndoRequest() = onUndoGesture?.invoke()
    private fun performRedoRequest() = onRedoGesture?.invoke()

    override fun onDetachedFromWindow() {
        super.onDetachedFromWindow()
        removeCallbacks(holdRunnable); stopAutoScroll()
        chunks.forEach { it.discardDisplayList() }
    }

    companion object {
        const val MIN_SCALE = 0.001f
        const val MAX_SCALE = 100f
        const val HOLD_MS = 550L
        const val TAP_MS = 280L
    }
}
