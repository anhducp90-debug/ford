package com.inkboard.canvas

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Matrix
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
import com.inkboard.model.Perspective
import com.inkboard.model.PressureCurve
import com.inkboard.model.Stabilizer
import com.inkboard.model.Symmetry
import com.inkboard.model.SymmetryMode
import com.inkboard.model.ImageEl
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
        /** Eyedropper result (long-press with a finger). */
        fun onColorPicked(color: Int) {}
        /** Canvas rotation (degrees) or mirroring changed. */
        fun onViewTransformChanged(rotation: Float, flipped: Boolean) {}
        /** Guides (symmetry centre, vanishing points) were moved. */
        fun onGuidesChanged() {}
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
    private val nav = NavOverlay(resources.displayMetrics.density).also { it.onThumbReady = { invalidate() } }

    /** What the S Pen side button does while held. */
    enum class PenButton { PAN, ERASE, SELECT }
    var penButton = PenButton.PAN
    /** Laser pointer: strokes glow and fade, nothing is saved. */
    var laser = false
    /** Scratch-out (zigzag over ink) deletes it; hand-drawn lines between blocks become connectors. */
    var smartInk = true
    /** Snap dragged items to nearby edges/centres. */
    var snapGuides = true
    private class Laser(val pts: FloatArray, val n: Int, val t: Long)
    private val laserTrails = ArrayList<Laser>()
    private var snapLines = ArrayList<FloatArray>()

    // Brush feel
    /** 0 = off … 10 = strong. */
    var stabilizerLevel = 0
    var pressureCurve = PressureCurve.NORMAL
    var taper = true
    private val stabilizer = Stabilizer(0f)
    private var lastRawWX = 0f
    private var lastRawWY = 0f
    private var lastRawP = 0.5f
    /** Straight perspective-assisted stroke being drawn (x0, y0, x1, y1). */
    private var perspLine: FloatArray? = null

    // View rotation / mirroring (applied around the view centre on top of the camera)
    var rotationEnabled = true
    var canvasRotation = 0f; private set
    var flipped = false; private set
    private val viewM = Matrix()
    private val viewInv = Matrix()
    private val viewIdentity get() = canvasRotation == 0f && !flipped
    private var rotTracking = false
    private var rotEngaged = false
    private var rotAngle0 = 0f
    private var rotBase = 0f

    // Eyedropper
    private var eyedropping = false
    private var eyeColor = 0
    private val eyeBmp by lazy { Bitmap.createBitmap(1, 1, Bitmap.Config.ARGB_8888) }
    private val eyedropRunnable = Runnable { startEyedrop() }

    // Guide handles: 1 = symmetry centre, 2 = VP1, 3 = VP2
    private var handleDrag = 0
    private val guidePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private var pointerSX = -1f
    private var pointerSY = -1f
    private var mapNavigating = false

    // Camera
    var scale = 1f; private set
    var tx = 0f; private set
    var ty = 0f; private set
    private val density = resources.displayMetrics.density

    /** Pictures decode in background at the resolution they are shown. */
    private val imageCache = ImageCache { invalidate() }
    private val renderer = Renderer(imageCache)

    // Selection
    val selection = LinkedHashSet<Long>()
    var primaryId = 0L; private set
    private var selPreview = Affine.IDENTITY

    // Render caches (RenderNode chunks of committed content, recorded in world space)
    private val chunkSize = 1500
    private var contentDirty = true
    private val hidden = HashSet<Long>()

    // ------------------------------------------------------------------ input
    private enum class PenMode { NONE, DRAW, LINE, ERASE, LASSO, DRAG, SCALE, PAN, HANDLE }

    private var penPointer = -1
    /** The current "pen" is a finger (finger-drawing mode). */
    private var penIsFinger = false
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
    /** Eraser radius on screen (dp) and whether it removes only the touched part of strokes. */
    var eraserSizeDp = 12f
    var eraserPartial = false
    private val eraserR get() = eraserSizeDp * density / scale
    /** Partial erase in progress: original stroke id → remaining pieces (x, y, p triples). */
    private val eraseWork = LinkedHashMap<Long, List<FloatArray>>()

    /** Main-thread timer (works even before the view is attached). */
    private val timer = android.os.Handler(android.os.Looper.getMainLooper())
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

    fun viewBox(): Box {
        val r = visibleRegion()
        return Box(wx(r.left), wy(r.top), wx(r.right), wy(r.bottom))
    }

    /** Region of the unrotated view plane that is visible on screen. */
    private fun visibleRegion(): RectF {
        val r = RectF(0f, 0f, width.toFloat(), height.toFloat())
        if (!viewIdentity) viewInv.mapRect(r)
        return r
    }

    // ------------------------------------------------------- rotation / flip

    private fun updateViewMatrix() {
        viewM.reset()
        val cx = width / 2f; val cy = height / 2f
        viewM.postTranslate(-cx, -cy)
        if (flipped) viewM.postScale(-1f, 1f)
        viewM.postRotate(canvasRotation)
        viewM.postTranslate(cx, cy)
        viewM.invert(viewInv)
    }

    fun setViewRotation(deg: Float) {
        var r = ((deg % 360f) + 360f) % 360f
        if (r > 180f) r -= 360f
        canvasRotation = r
        updateViewMatrix()
        listener?.onViewTransformChanged(canvasRotation, flipped)
        notifySelection(); invalidate()
    }

    fun toggleFlip() {
        flipped = !flipped
        updateViewMatrix()
        listener?.onViewTransformChanged(canvasRotation, flipped)
        notifySelection(); invalidate()
    }

    fun resetViewTransform() {
        canvasRotation = 0f; flipped = false
        updateViewMatrix()
        listener?.onViewTransformChanged(canvasRotation, flipped)
        notifySelection(); invalidate()
    }

    /** Two-finger rotation from raw (screen) coordinates; engages after 12°, snaps to 90° steps. */
    private fun trackRotation(e: MotionEvent) {
        val fingerIdx = (0 until e.pointerCount).filter { e.getToolType(it) == MotionEvent.TOOL_TYPE_FINGER }
        val ending = e.actionMasked == MotionEvent.ACTION_UP || e.actionMasked == MotionEvent.ACTION_CANCEL ||
            e.actionMasked == MotionEvent.ACTION_POINTER_UP || e.actionMasked == MotionEvent.ACTION_POINTER_DOWN
        if (!rotationEnabled || penPointer >= 0 || fingerIdx.size < 2 || ending || eyedropping) {
            rotTracking = false; rotEngaged = false; return
        }
        if (e.actionMasked != MotionEvent.ACTION_MOVE) return
        val a = fingerIdx[0]; val b = fingerIdx[1]
        val ang = Math.toDegrees(atan2((e.getY(b) - e.getY(a)).toDouble(), (e.getX(b) - e.getX(a)).toDouble())).toFloat()
        if (!rotTracking) { rotTracking = true; rotEngaged = false; rotAngle0 = ang; return }
        fun norm(d: Float): Float { var x = d % 360f; if (x > 180f) x -= 360f; if (x < -180f) x += 360f; return x }
        val diff = norm(ang - rotAngle0)
        if (!rotEngaged) {
            if (abs(diff) < 12f) return
            rotEngaged = true; rotAngle0 = ang; rotBase = canvasRotation
            return
        }
        var r = rotBase + norm(ang - rotAngle0) * if (flipped) -1f else 1f
        val snap = Math.round(r / 90f) * 90f
        if (abs(r - snap) < 5f) r = snap
        if (r != canvasRotation) setViewRotation(r)
    }

    /** Map a raw screen point into the unrotated view plane. */
    private fun toPlane(x: Float, y: Float): Pair<Float, Float> {
        if (viewIdentity) return x to y
        val p = floatArrayOf(x, y); viewInv.mapPoints(p); return p[0] to p[1]
    }

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

    private fun insets(): androidx.core.graphics.Insets? =
        androidx.core.view.ViewCompat.getRootWindowInsets(this)?.getInsets(
            androidx.core.view.WindowInsetsCompat.Type.systemBars() or androidx.core.view.WindowInsetsCompat.Type.displayCutout()
        )

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
        for (e in doc.connections()) if (e is Edge && e.from in selection && e.to in selection) selection += e.id
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

    /** Selection bounds on screen (after rotation), for UI placement. */
    fun selectionScreenRect(): RectF? {
        val r = planeSelRect() ?: return null
        if (!viewIdentity) viewM.mapRect(r)
        return r
    }

    /** Selection bounds in the unrotated view plane (used for hit testing). */
    private fun planeSelRect(): RectF? {
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
        updateViewMatrix()
        if (oldw == 0 && doc.camScale == 1f && doc.camX == 0f && doc.camY == 0f) {
            if (doc.elements.isEmpty()) { tx = w / 2f; ty = h / 2f; saveCamera() } else fitContent(1f)
        }
        notifySelection()
    }

    override fun onDraw(c: Canvas) {
        c.drawColor(if (dark) 0xFF1E1F22.toInt() else 0xFFFBFBF9.toInt())
        c.save()
        if (!viewIdentity) c.concat(viewM)
        drawWorld(c)
        drawInnerOverlays(c)
        c.restore()
        drawOuterOverlays(c)
    }

    /** Background, content and live input, in the unrotated view plane. */
    private fun drawWorld(c: Canvas) {
        val vr = visibleRegion()
        renderer.drawBackground(c, doc.background, vr.left, vr.top, vr.right, vr.bottom, scale, tx, ty, dark)
        if (showRulers) nav.drawOrigin(c, scale, tx, ty, dark) // under the content
        val previewing = !selPreview.isIdentity
        c.save()
        c.translate(tx, ty)
        c.scale(scale, scale)

        // Connections (edges + mind-map branches) follow moving nodes live.
        renderer.drawConnections(c, doc) { id ->
            val n = doc.byId(id)
            if (n != null && previewing && id in selection) n.transformed(selPreview) else n
        }

        if (c.isHardwareAccelerated) {
            drawChunks(c, previewing)
        } else {
            val vb = viewBox().inset(-50f / scale)
            val folded = doc.hiddenIds()
            renderer.viewScale = scale
            for (e in doc.query(vb)) {
                if (e.id in hidden || e.id in folded || (previewing && e.id in selection)) continue
                renderer.draw(c, doc, e)
            }
        }

        if (previewing) {
            c.save()
            c.translate(selPreview.tx, selPreview.ty)
            c.scale(selPreview.s, selPreview.s)
            if (c.isHardwareAccelerated) {
                // Record the moving selection once, then only transform it each frame.
                val sig = doc.version * 31 + selection.hashCode()
                if (sig != previewSig) {
                    previewNode.setPosition(-1_000_000, -1_000_000, 1_000_000, 1_000_000)
                    previewNode.setClipToBounds(false)
                    val rc = previewNode.beginRecording()
                    rc.translate(1_000_000f, 1_000_000f)
                    for (id in selection) doc.byId(id)?.let { if (it !is Edge) renderer.draw(rc, doc, it) }
                    previewNode.endRecording()
                    previewSig = sig
                }
                c.drawRenderNode(previewNode)
            } else {
                for (id in selection) doc.byId(id)?.let { renderer.draw(c, doc, it) }
            }
            c.restore()
        } else if (previewSig != Long.MIN_VALUE) {
            previewNode.discardDisplayList(); previewSig = Long.MIN_VALUE
        }

        // Live input (+ symmetric copies)
        val pl = perspLine
        when {
            pl != null -> {
                val pts = lineStroke(pl)
                renderer.drawLiveStroke(c, pts, pts.size / 3, penColor, liveWidth, penType, taper)
                for (cp in Symmetry.copies(pts, doc.guides)) renderer.drawLiveStroke(c, cp, cp.size / 3, penColor, liveWidth, penType, taper)
            }
            snapped != null -> {
                renderer.drawShape(c, snapped!!)
                for (cp in symmetricShapes(snapped!!)) renderer.drawShape(c, cp)
            }
            penMode == PenMode.DRAW && liveN > 0 && !laser -> {
                // Draw a predicted point ahead of the pen (not committed).
                val pe = if (stabilizer.radius == 0f) runCatching { predictor?.predict() }.getOrNull() else null
                if (pe != null && penPointer >= 0) {
                    val pi = pe.findPointerIndex(penPointer)
                    if (pi >= 0) {
                        val ext = livePts.copyOf((liveN + 1) * 3)
                        ext[liveN * 3] = wx(pe.getX(pi)); ext[liveN * 3 + 1] = wy(pe.getY(pi)); ext[liveN * 3 + 2] = livePts[liveN * 3 - 1]
                        renderer.drawLiveStroke(c, ext, liveN + 1, penColor, liveWidth, penType, taper)
                    } else renderer.drawLiveStroke(c, livePts, liveN, penColor, liveWidth, penType, taper)
                    pe.recycle()
                } else renderer.drawLiveStroke(c, livePts, liveN, penColor, liveWidth, penType, taper)
                if (doc.guides.symmetry != SymmetryMode.OFF) {
                    for (cp in Symmetry.copies(livePts.copyOf(liveN * 3), doc.guides)) renderer.drawLiveStroke(c, cp, liveN, penColor, liveWidth, penType, taper)
                }
            }
        }
        if (eraseWork.isNotEmpty()) for ((id, parts) in eraseWork) {
            val o = doc.byId(id) as? Stroke ?: continue
            for (p in parts) renderer.drawLiveStroke(c, p, p.size / 3, o.color, o.width, o.pen, o.taper)
        }
        if (laserTrails.isNotEmpty() || (laser && penMode == PenMode.DRAW && liveN > 0)) drawLasers(c)
        if (!selPreview.isIdentity && snapLines.isNotEmpty()) {
            guidePaint.color = 0xFFE52592.toInt(); guidePaint.strokeWidth = 1.2f * density / scale; guidePaint.pathEffect = null
            for (l in snapLines) c.drawLine(l[0], l[1], l[2], l[3], guidePaint)
        }
        drawGuides(c)
        c.restore()
    }

    private fun drawLasers(c: Canvas) {
        val now = SystemClock.uptimeMillis()
        val w = 4f * density / scale
        fun one(pts: FloatArray, n: Int, alpha: Float) {
            if (n < 1 || alpha <= 0f) return
            val a = (alpha * 255).toInt()
            renderer.drawLiveStroke(c, pts, n, (a * 0.35f).toInt().shl(24) or 0xFF3B30, w * 3.5f, PenType.HIGHLIGHTER)
            renderer.drawLiveStroke(c, pts, n, a.shl(24) or 0xFF3B30, w, PenType.PEN)
        }
        for (l in laserTrails) one(l.pts, l.n, 1f - (now - l.t).toFloat() / LASER_MS)
        if (laser && penMode == PenMode.DRAW) one(livePts, liveN, 1f)
    }

    /** Symmetry axes and perspective lines (world space, inside the camera transform). */
    private fun drawGuides(c: Canvas) {
        val g = doc.guides
        if (g.symmetry == SymmetryMode.OFF && g.perspective == 0) return
        val vb = viewBox()
        val big = max(vb.w, vb.h) * 2f
        guidePaint.strokeWidth = 1.2f * density / scale
        guidePaint.pathEffect = null
        if (g.symmetry != SymmetryMode.OFF) {
            guidePaint.color = 0x99E52592.toInt()
            val cx = g.symX; val cy = g.symY
            when (g.symmetry) {
                SymmetryMode.VERTICAL -> c.drawLine(cx, cy - big, cx, cy + big, guidePaint)
                SymmetryMode.HORIZONTAL -> c.drawLine(cx - big, cy, cx + big, cy, guidePaint)
                SymmetryMode.QUAD -> { c.drawLine(cx, cy - big, cx, cy + big, guidePaint); c.drawLine(cx - big, cy, cx + big, cy, guidePaint) }
                SymmetryMode.RADIAL -> {
                    val n = g.radialN.coerceIn(2, 24)
                    for (k in 0 until n) {
                        val a = -Math.PI / 2 + 2 * Math.PI * k / n
                        c.drawLine(cx, cy, cx + (big * cos(a)).toFloat(), cy + (big * sin(a)).toFloat(), guidePaint)
                    }
                }
                SymmetryMode.OFF -> Unit
            }
        }
        if (g.perspective > 0) {
            guidePaint.color = 0x661A73E8
            c.drawLine(vb.l - big, g.vp1y, vb.r + big, g.vp1y, guidePaint) // horizon
            val vps = if (g.perspective >= 2) listOf(g.vp1x to g.vp1y, g.vp2x to g.vp2y) else listOf(g.vp1x to g.vp1y)
            guidePaint.color = 0x331A73E8
            for ((vx, vy) in vps) for (k in 0 until 24) {
                val a = 2 * Math.PI * k / 24
                c.drawLine(vx, vy, vx + (big * cos(a)).toFloat(), vy + (big * sin(a)).toFloat(), guidePaint)
            }
        }
    }

    /** Draggable handles of the active guides, as (id, world x, world y). */
    private fun guideHandles(): List<Triple<Int, Float, Float>> {
        val g = doc.guides
        val out = ArrayList<Triple<Int, Float, Float>>()
        if (g.symmetry != SymmetryMode.OFF) out += Triple(1, g.symX, g.symY)
        if (g.perspective >= 1) out += Triple(2, g.vp1x, g.vp1y)
        if (g.perspective >= 2) out += Triple(3, g.vp2x, g.vp2y)
        return out
    }

    private fun hitGuideHandle(px: Float, py: Float): Int =
        guideHandles().firstOrNull { (_, x, y) -> hypot(sx(x) - px, sy(y) - py) <= 22f * density }?.first ?: 0

    private fun moveGuideHandle(h: Int, px: Float, py: Float) {
        val x = wx(px); val y = wy(py)
        doc.guides = when (h) {
            1 -> doc.guides.copy(symX = x, symY = y)
            2 -> doc.guides.copy(vp1x = x, vp1y = y, vp2y = if (doc.guides.perspective >= 2) y else doc.guides.vp2y)
            3 -> doc.guides.copy(vp2x = x, vp2y = y, vp1y = y)
            else -> doc.guides
        }
        invalidate()
    }

    /** Turn guides on with sensible defaults around the current view. */
    fun setSymmetry(mode: SymmetryMode, n: Int = doc.guides.radialN) {
        val (cx, cy) = centerWorld()
        val g = doc.guides
        doc.guides = if (g.symmetry == SymmetryMode.OFF && mode != SymmetryMode.OFF) g.copy(symmetry = mode, radialN = n, symX = cx, symY = cy)
        else g.copy(symmetry = mode, radialN = n)
        doc.markDirty(); listener?.onGuidesChanged(); invalidate()
    }

    fun setPerspective(points: Int) {
        val (cx, cy) = centerWorld()
        val g = doc.guides
        val span = width / scale * 0.35f
        doc.guides = if (g.perspective == 0 && points > 0) g.copy(perspective = points, vp1x = if (points == 2) cx - span else cx, vp1y = cy - height / scale * 0.15f, vp2x = cx + span, vp2y = cy - height / scale * 0.15f)
        else if (points == 2 && g.perspective == 1) g.copy(perspective = 2, vp1x = g.vp1x - span, vp2x = g.vp1x + span, vp2y = g.vp1y)
        else g.copy(perspective = points)
        doc.markDirty(); listener?.onGuidesChanged(); invalidate()
    }

    fun setPerspectiveAssist(on: Boolean) {
        doc.guides = doc.guides.copy(assist = on); doc.markDirty(); listener?.onGuidesChanged(); invalidate()
    }

    /** Resample a straight segment into a pressure-carrying stroke. */
    private fun lineStroke(l: FloatArray): FloatArray {
        val n = 24
        val p = if (liveN > 0) (0 until liveN).map { livePts[3 * it + 2] }.average().toFloat() else 0.5f
        return FloatArray(n * 3) { k ->
            val i = k / 3; val t = i / (n - 1f)
            when (k % 3) { 0 -> l[0] + (l[2] - l[0]) * t; 1 -> l[1] + (l[3] - l[1]) * t; else -> p }
        }
    }

    /** Symmetric copies of a shape (lines, polygons, ellipses). */
    private fun symmetricShapes(s: ShapeEl): List<ShapeEl> {
        val g = doc.guides
        if (g.symmetry == SymmetryMode.OFF) return emptyList()
        return if (s.kind == ShapeKind.ELLIPSE) {
            val cx = s.geo[0]; val cy = s.geo[1]; val a = s.geo[4]
            val probe = floatArrayOf(cx, cy, 0f, cx + cos(a), cy + sin(a), 0f)
            Symmetry.copies(probe, g).map { q ->
                val na = atan2(q[4] - q[1], q[3] - q[0])
                ShapeEl(0, s.kind, floatArrayOf(q[0], q[1], s.geo[2], s.geo[3], na), s.color, s.width, s.fill)
            }
        } else {
            val tri = FloatArray(s.geo.size / 2 * 3) { k -> if (k % 3 == 2) 0f else s.geo[(k / 3) * 2 + k % 3] }
            Symmetry.copies(tri, g).map { q ->
                ShapeEl(0, s.kind, FloatArray(s.geo.size) { k -> q[(k / 2) * 3 + k % 2] }, s.color, s.width, s.fill)
            }
        }
    }

    // ------------------------------------------------------------- eyedropper

    private fun startEyedrop() {
        val f = fingers.values.singleOrNull() ?: return
        if (f.moved || penPointer >= 0 || fingerGesture != 0) return
        // Holding on the selection (to drag it) or on a guide handle is not an eyedropper.
        if (planeSelRect()?.apply { inset(-6f * density, -6f * density) }?.contains(f.sx, f.sy) == true) return
        if (hitGuideHandle(f.sx, f.sy) != 0) return
        // Only over drawn content: holding on empty paper never changes the pen colour.
        if (doc.hitTop(wx(f.x), wy(f.y), 4f * density / scale, includeLocked = true) == null) return
        eyedropping = true
        fingerGesture = 5
        sampleAt(f.x, f.y)
        performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
    }

    /** True while the loupe is over drawn content (colour will be applied on release). */
    private var eyeValid = false

    /** Read the rendered colour at a point of the view plane (preview only; applied on release). */
    fun sampleAt(px: Float, py: Float): Int {
        val bc = Canvas(eyeBmp)
        bc.drawColor(0, android.graphics.PorterDuff.Mode.CLEAR)
        bc.translate(-px, -py)
        drawWorld(bc)
        val over = doc.hitTop(wx(px), wy(py), 4f * density / scale, includeLocked = true) != null
        if (over) { eyeColor = eyeBmp.getPixel(0, 0) or 0xFF000000.toInt(); eyeValid = true }
        invalidate()
        return eyeColor
    }

    private fun finishEyedrop() {
        if (eyeValid) listener?.onColorPicked(eyeColor)
        eyedropping = false; eyeValid = false; fingerGesture = 0
        invalidate()
    }

    /** Committed content is recorded into world-space RenderNodes and replayed. */
    /** A slice of the z-ordered element list recorded into one RenderNode. */
    private class Chunk(val node: RenderNode) {
        var key: Array<Any?> = emptyArray()
        var bounds: Box = Box.EMPTY
        var dirty = true
        var hasImages = false
        /** Zoom bucket and picture-cache version the chunk was recorded with. */
        var recBucket = Int.MIN_VALUE
        var recImgVer = -1
    }
    private val chunkList = ArrayList<Chunk>()
    private val previewNode = RenderNode("preview")
    private var previewSig = Long.MIN_VALUE
    private var chunkSignature = Long.MIN_VALUE

    /**
     * Committed content is recorded per chunk (world space) and replayed. Chunks
     * are only re-keyed when the document/selection state changes, only
     * re-recorded when their content changed, and skipped when off screen.
     */
    private fun drawChunks(c: Canvas, previewing: Boolean) {
        val els = doc.elements
        val need = (els.size + chunkSize - 1) / chunkSize
        while (chunkList.size < need) chunkList += Chunk(RenderNode("ink${chunkList.size}"))
        val folded = doc.hiddenIds()
        val sig = doc.version * 31 + hidden.size * 7 + (if (previewing) 1 else 0) + selection.size * 131L
        if (contentDirty || sig != chunkSignature) {
            for (k in 0 until need) {
                val from = k * chunkSize
                val to = min(els.size, from + chunkSize)
                val key = arrayOfNulls<Any?>(to - from)
                for (i in from until to) {
                    val e = els[i]
                    key[i - from] = if (e is Edge || e.id in hidden || e.id in folded || (previewing && e.id in selection)) null else e
                }
                val ch = chunkList[k]
                if (contentDirty || !key.contentEquals(ch.key)) {
                    ch.key = key
                    var b = Box.EMPTY
                    for (e in key) if (e != null) b = b.union((e as Element).bounds)
                    ch.bounds = b
                    ch.hasImages = key.any { it is ImageEl }
                    ch.dirty = true
                }
            }
            chunkSignature = sig
            contentDirty = false
        }
        val vb = viewBox().inset(-64f / scale)
        val bucket = Math.floor(Math.log(scale.toDouble()) / Math.log(2.0)).toInt()
        renderer.viewScale = scale
        for (k in 0 until need) {
            val ch = chunkList[k]
            if (!ch.bounds.intersects(vb)) {
                // Off-screen chunks with pictures drop their display list so the bitmaps can be freed.
                if (ch.hasImages && !ch.dirty) { ch.node.discardDisplayList(); ch.dirty = true }
                continue
            }
            if (ch.hasImages && (ch.recBucket != bucket || ch.recImgVer != imageCache.version)) ch.dirty = true
            if (ch.dirty) {
                ch.recBucket = bucket; ch.recImgVer = imageCache.version
                ch.node.setPosition(-1_000_000, -1_000_000, 1_000_000, 1_000_000)
                ch.node.setClipToBounds(false)
                val rc: RecordingCanvas = ch.node.beginRecording()
                rc.translate(1_000_000f, 1_000_000f)
                for (e in ch.key) if (e != null) renderer.draw(rc, doc, e as Element)
                ch.node.endRecording()
                ch.dirty = false
            }
            c.drawRenderNode(ch.node)
        }
    }

    private fun drawInnerOverlays(c: Canvas) {
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
        planeSelRect()?.let { r ->
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
            c.drawCircle(x, y, eraserSizeDp * density, overlay)
        } else if (hoverX >= 0 && tool == Tool.PEN && penPointer < 0) {
            overlayFill.color = penColor
            c.drawCircle(hoverX, hoverY, max(1.5f * density, penWidthDp * density / 2), overlayFill)
        }
        handlesAndLoupe(c)
    }

    private fun handlesAndLoupe(c: Canvas) {
        for ((_, x, y) in guideHandles()) {
            overlayFill.color = Color.WHITE
            c.drawCircle(sx(x), sy(y), 9f * density, overlayFill)
            overlay.color = 0xFFE52592.toInt()
            c.drawCircle(sx(x), sy(y), 9f * density, overlay)
            c.drawCircle(sx(x), sy(y), 2.5f * density, overlay)
        }
        if (eyedropping) {
            val f = fingers.values.firstOrNull() ?: return
            val ly = f.y - 70f * density
            overlayFill.color = eyeColor
            c.drawCircle(f.x, ly, 30f * density, overlayFill)
            overlay.color = Color.WHITE; overlay.strokeWidth = 4f * density
            c.drawCircle(f.x, ly, 30f * density, overlay)
            overlay.color = 0x55000000; overlay.strokeWidth = 1.5f * density
            c.drawCircle(f.x, ly, 32f * density, overlay)
        }
    }

    /** Screen-space aids that do not rotate with the canvas. */
    private fun drawOuterOverlays(c: Canvas) {
        val ins = insets()
        if (showRulers && viewIdentity) {
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
            if (e.actionMasked != MotionEvent.ACTION_HOVER_EXIT) excludeEdgesAround(e.y)
            val (hx, hy) = toPlane(e.x, e.y)
            if (e.actionMasked == MotionEvent.ACTION_HOVER_EXIT) { hoverX = -1f; hoverY = -1f } else { hoverX = hx; hoverY = hy; reportPointer(hx, hy) }
            invalidate()
            return true
        }
        return super.onHoverEvent(e)
    }

    private fun penNear() = penPointer >= 0 || SystemClock.uptimeMillis() - lastHoverTime < 400

    /**
     * While the pen is near the screen edge, opt that stretch of the edge out of
     * the system back gesture so writing close to the edge is not taken as "back".
     */
    private var lastExclusionY = Float.NaN

    private fun excludeEdgesAround(rawY: Float) {
        // Only refresh when the pen moved noticeably (hover events arrive at ~120 Hz).
        if (!lastExclusionY.isNaN() && abs(rawY - lastExclusionY) < 40f * density) return
        lastExclusionY = rawY
        val band = (200f * density).toInt()
        val edgeW = (40f * density).toInt()
        val top = (rawY - band / 2).toInt().coerceIn(0, max(0, height - band))
        systemGestureExclusionRects = listOf(
            android.graphics.Rect(0, top, edgeW, top + band),
            android.graphics.Rect(width - edgeW, top, width, top + band),
        )
    }

    // ------------------------------------------------------------------ touch

    private fun isPenTool(e: MotionEvent, idx: Int): Boolean {
        val t = e.getToolType(idx)
        return t == MotionEvent.TOOL_TYPE_STYLUS || t == MotionEvent.TOOL_TYPE_ERASER || t == MotionEvent.TOOL_TYPE_MOUSE
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (handleMinimapTouch(e)) return true
        if (e.getToolType(e.actionIndex) == MotionEvent.TOOL_TYPE_STYLUS && e.actionMasked == MotionEvent.ACTION_DOWN) excludeEdgesAround(e.y)
        trackRotation(e)
        if (viewIdentity) return handleTouch(e)
        val ve = MotionEvent.obtain(e)
        ve.transform(viewInv)
        try { return handleTouch(ve) } finally { ve.recycle() }
    }

    /** Predicts where the pen is going so the live stroke runs ahead of input latency. */
    private val predictor by lazy { runCatching { androidx.input.motionprediction.MotionEventPredictor.newInstance(this) }.getOrNull() }

    private fun handleTouch(e: MotionEvent): Boolean {
        if (penPointer >= 0 || e.actionMasked == MotionEvent.ACTION_DOWN) runCatching { predictor?.record(e) }
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val idx = e.actionIndex
                val pid = e.getPointerId(idx)
                val asPen = isPenTool(e, idx) || (fingerDraws && fingers.isEmpty() && penPointer < 0 && e.pointerCount == 1)
                if (asPen && penPointer < 0) {
                    requestUnbufferedDispatch(e)
                    penPointer = pid
                    penIsFinger = !isPenTool(e, idx)
                    penDown(e, idx)
                } else if (!isPenTool(e, idx)) {
                    if (penPointer >= 0 && penIsFinger) {
                        // Finger-drawing mode: a second finger means navigation, not ink.
                        val fi = e.findPointerIndex(penPointer)
                        val first = penPointer
                        penCancel(); penPointer = -1; penIsFinger = false
                        if (fi >= 0) fingerDown(e, fi, first)
                    }
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
                timer.removeCallbacks(eyedropRunnable); eyedropping = false
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
        timer.removeCallbacks(eyedropRunnable)
        if (eyedropping) { eyedropping = false; fingerGesture = 0 }
        lastPenSX = x; lastPenSY = y
        hoverX = -1f
        val button = (e.buttonState and (MotionEvent.BUTTON_STYLUS_PRIMARY or MotionEvent.BUTTON_SECONDARY)) != 0
        val eraserTip = e.getToolType(idx) == MotionEvent.TOOL_TYPE_ERASER
        handleDrag = if (!button) hitGuideHandle(x, y) else 0
        penMode = when {
            handleDrag != 0 -> PenMode.HANDLE
            button && penButton == PenButton.PAN -> PenMode.PAN
            button && penButton == PenButton.ERASE -> PenMode.ERASE
            button && penButton == PenButton.SELECT -> selectModeAt(x, y)
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
                snapped = null; perspLine = null
                stabilizer.radius = stabilizerLevel * 1.6f * density / scale
                stabilizer.reset(wx(x), wy(y))
                lastRawWX = wx(x); lastRawWY = wy(y); lastRawP = pressure(e, idx, -1)
                addLivePoint(wx(x), wy(y), lastRawP)
                holdX = x; holdY = y
                if (modifierHeld() || perspectiveAssist()) penMode = PenMode.LINE
                scheduleHold()
            }
            PenMode.ERASE -> {
                hidden.clear(); eraseWork.clear()
                eraseEdges = if (doc.hasEdges) doc.elements.filterIsInstance<Edge>() else emptyList()
                eraseAt(wx(x), wy(y))
            }
            PenMode.LASSO -> { lasso.clear(); lasso += wx(x); lasso += wy(y) }
            PenMode.DRAG, PenMode.SCALE -> Unit
            else -> Unit
        }
        invalidate()
    }

    /** SELECT tool: decide between scaling, dragging and a new lasso. */
    private fun selectModeAt(x: Float, y: Float): PenMode {
        val r = planeSelRect()
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

    private fun perspectiveAssist() = doc.guides.perspective > 0 && doc.guides.assist

    /** Pressure through the user's curve; for the pencil, tilt adds width (shading). */
    private fun pressure(e: MotionEvent, idx: Int, h: Int): Float {
        if (e.getToolType(idx) == MotionEvent.TOOL_TYPE_FINGER) return if (penType == PenType.PENCIL) 0.3f else 0.6f
        val raw = if (h < 0) e.getPressure(idx) else e.getHistoricalPressure(idx, h)
        var p = pressureCurve.map(raw)
        if (penType == PenType.PENCIL) {
            val tilt = if (h < 0) e.getAxisValue(MotionEvent.AXIS_TILT, idx) else e.getHistoricalAxisValue(MotionEvent.AXIS_TILT, idx, h)
            val tf = sin(tilt.toDouble()).toFloat().coerceIn(0f, 1f)
            p = if (tf > 0.05f) p * 0.4f + tf * 0.6f else p * 0.5f
        }
        return p.coerceIn(0.03f, 1f)
    }

    /** Add a pen point through the stabilizer. */
    private fun addStabilized(x: Float, y: Float, p: Float) {
        lastRawWX = x; lastRawWY = y; lastRawP = p
        val q = stabilizer.feed(x, y)
        if (q == null) {
            if (liveN > 0) livePts[3 * (liveN - 1) + 2] = max(livePts[3 * (liveN - 1) + 2], p)
            return
        }
        addLivePoint(q.first, q.second, p)
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
                    addStabilized(wx(x), wy(y), pressure(e, idx, if (h < hs) h else -1))
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
                PenMode.HANDLE -> moveGuideHandle(handleDrag, x, y)
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

    /** Snap a translation preview so the moving bounds align with nearby elements. */
    private fun snapTranslate(dx: Float, dy: Float): Affine {
        snapLines = ArrayList()
        if (!snapGuides || selection.isEmpty()) return Affine.translate(dx, dy)
        val mb = doc.boundsOf(selection)
        if (mb.isEmpty) return Affine.translate(dx, dy)
        val m = Box(mb.l + dx, mb.t + dy, mb.r + dx, mb.b + dy)
        val tol = 6f * density / scale
        val area = Box(m.l - m.w * 3, m.t - m.h * 3, m.r + m.w * 3, m.b + m.h * 3).inset(-200f / scale)
        val excl = selection + doc.hiddenIds()
        val others = doc.query(area).asSequence().filter { it.id !in excl && it !is Stroke }.take(300).map { doc.boundsOf(it) }.toList()
        var bestX = tol + 1; var sx = 0f; var gx = Float.NaN
        var bestY = tol + 1; var sy = 0f; var gy = Float.NaN
        val mxs = floatArrayOf(m.l, m.cx, m.r); val mys = floatArrayOf(m.t, m.cy, m.b)
        for (o in others) {
            for (a in mxs) for (b in floatArrayOf(o.l, o.cx, o.r)) { val d = b - a; if (abs(d) < bestX) { bestX = abs(d); sx = d; gx = b } }
            for (a in mys) for (b in floatArrayOf(o.t, o.cy, o.b)) { val d = b - a; if (abs(d) < bestY) { bestY = abs(d); sy = d; gy = b } }
        }
        val fx = if (bestX <= tol) sx else 0f
        val fy = if (bestY <= tol) sy else 0f
        if (bestX <= tol) snapLines.add(floatArrayOf(gx, min(m.t, m.t + fy) - 40f / scale, gx, max(m.b, m.b + fy) + 40f / scale))
        if (bestY <= tol) snapLines.add(floatArrayOf(min(m.l, m.l + fx) - 40f / scale, gy, max(m.r, m.r + fx) + 40f / scale, gy))
        return Affine.translate(dx + fx, dy + fy)
    }

    private fun updateDrag(x: Float, y: Float) {
        selPreview = snapTranslate(wx(x) - dragStartWX, wy(y) - dragStartWY)
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
        if (perspectiveAssist() && !modifierHeld()) {
            perspLine = Perspective.snap(x0, y0, x1, y1, doc.guides)
            snapped = null
            return
        }
        val len = hypot(x1 - x0, y1 - y0)
        val ang = Geo.snapAngle(atan2((y1 - y0).toDouble(), (x1 - x0).toDouble()), Math.PI / 12)
        val ex = x0 + (len * cos(ang)).toFloat(); val ey = y0 + (len * sin(ang)).toFloat()
        snapped = ShapeEl(0, ShapeKind.LINE, floatArrayOf(x0, y0, ex, ey), penColor, strokeWidthForShapes())
    }

    private fun strokeWidthForShapes() = max(liveWidth, 1.5f * density / scale) * if (penType == PenType.HIGHLIGHTER) 0.25f else 1f

    private fun scheduleHold() {
        timer.removeCallbacks(holdRunnable)
        if (autoShape && penMode == PenMode.DRAW) timer.postDelayed(holdRunnable, HOLD_MS)
    }

    /** Pen held still: try to snap the live stroke to a clean shape. */
    private fun onPenHold() {
        if (penMode != PenMode.DRAW || snapped != null || liveN < 4) return
        val xy = FloatArray(liveN * 2) { k -> livePts[(k / 2) * 3 + (k % 2)] }
        // Small marks (letters, dots) are handwriting, never shapes.
        val bb = Box.of(xy)
        if (maxOf(bb.w, bb.h) * scale < MIN_SHAPE_DP * density) return
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

    private var eraseEdges: List<Edge> = emptyList()

    private fun eraseAt(x: Float, y: Float) {
        val r = eraserR
        var changed = false
        for (el in doc.query(Box(x - r, y - r, x + r, y + r))) {
            if (el.id in doc.locked || el.id in doc.hiddenIds()) continue
            if (eraserPartial && el is Stroke) {
                if (splitStroke(el, x, y, r)) changed = true
                continue
            }
            if (el.id in hidden) continue
            if (el.hit(x, y, r, solid = false)) { hidden += el.id; changed = true }
        }
        for (el in eraseEdges) {
            if (el.id in hidden) continue
            val hit = doc.edgeEnds(el)?.let { Geo.distToSegment(x, y, it[0], it[1], it[2], it[3]) <= r + el.width } ?: false
            if (hit) { hidden += el.id; changed = true }
        }
        if (changed) invalidate()
    }

    /** Remove the points of [s] within [r] of (x, y); keeps the remaining runs as pieces. */
    private fun splitStroke(s: Stroke, x: Float, y: Float, r: Float): Boolean {
        val pieces = eraseWork[s.id] ?: listOf(s.pts)
        val rr = r + s.width / 2
        var touched = false
        val out = ArrayList<FloatArray>()
        for (p in pieces) {
            var run = ArrayList<Float>()
            val n = p.size / 3
            for (i in 0 until n) {
                val inside = hypot(p[3 * i] - x, p[3 * i + 1] - y) <= rr
                if (inside) {
                    touched = true
                    if (run.size >= 6) out += run.toFloatArray()
                    run = ArrayList()
                } else { run += p[3 * i]; run += p[3 * i + 1]; run += p[3 * i + 2] }
            }
            if (run.size >= 6) out += run.toFloatArray()
        }
        if (!touched) return false
        eraseWork[s.id] = out
        hidden += s.id
        return true
    }

    private fun penUp() {
        timer.removeCallbacks(holdRunnable)
        stopAutoScroll()
        when (penMode) {
            PenMode.DRAW, PenMode.LINE -> {
                val shape = snapped
                val pl = perspLine
                val useTaper = taper && penType != PenType.HIGHLIGHTER
                if (shape == null && pl == null && liveN > 0 && stabilizer.radius > 0f) addLivePoint(lastRawWX, lastRawWY, lastRawP)
                val els = ArrayList<Element>()
                when {
                    pl != null -> {
                        val pts = lineStroke(pl)
                        els += Stroke(doc.nextId(), pts, penColor, liveWidth, penType, useTaper)
                        for (cp in Symmetry.copies(pts, doc.guides)) els += Stroke(doc.nextId(), cp, penColor, liveWidth, penType, useTaper)
                    }
                    shape != null && smartInk && !laser && (shape.kind == ShapeKind.LINE || shape.kind == ShapeKind.ARROW) &&
                        doc.guides.symmetry == SymmetryMode.OFF && tryConnector(shape.geo) -> Unit
                    shape != null -> {
                        els += ShapeEl(doc.nextId(), shape.kind, shape.geo, shape.color, shape.width, shape.fill)
                        for (cp in symmetricShapes(shape)) els += ShapeEl(doc.nextId(), cp.kind, cp.geo, cp.color, cp.width, cp.fill)
                    }
                    liveN > 0 && laser -> {
                        laserTrails += Laser(livePts.copyOf(liveN * 3), liveN, SystemClock.uptimeMillis())
                        postOnAnimation { fadeLasers() }
                    }
                    liveN > 0 && smartInk && smartInkAction() -> Unit
                    liveN > 0 -> {
                        val pts = livePts.copyOf(liveN * 3)
                        els += Stroke(doc.nextId(), pts, penColor, liveWidth, penType, useTaper)
                        for (cp in Symmetry.copies(pts, doc.guides)) els += Stroke(doc.nextId(), cp, penColor, liveWidth, penType, useTaper)
                    }
                }
                if (els.isNotEmpty()) doc.batch {
                    doc.add(els)
                    // Ink written fully inside a shape / block / note / table belongs to it.
                    for (el in els) DropRules.enclosingContainer(doc, el.bounds, el.id)?.let { doc.setContainer(el.id, it.id) }
                }
                liveN = 0; snapped = null; perspLine = null
                changed()
            }
            PenMode.ERASE -> {
                if (hidden.isNotEmpty()) {
                    val ids = hidden.toList()
                    val work = LinkedHashMap(eraseWork)
                    hidden.clear(); eraseWork.clear()
                    doc.batch {
                        val pieces = ArrayList<Pair<Long, Stroke>>()
                        for ((id, parts) in work) {
                            val orig = doc.byId(id) as? Stroke ?: continue
                            for (pp in parts) pieces += id to Stroke(doc.nextId(), pp, orig.color, orig.width, orig.pen, orig.taper)
                        }
                        val containers = work.keys.associateWith { doc.containerOf(it) }
                        doc.remove(ids)
                        if (pieces.isNotEmpty()) {
                            doc.add(pieces.map { it.second })
                            for ((orig, piece) in pieces) containers[orig]?.takeIf { it != 0L && doc.byId(it) != null }?.let { doc.setContainer(piece.id, it) }
                        }
                    }
                    changed()
                }
            }
            PenMode.LASSO -> finishLasso()
            PenMode.DRAG -> commitPreview(lastPenSX, lastPenSY)
            PenMode.SCALE -> commitPreview()
            PenMode.PAN -> Unit
            PenMode.HANDLE -> { handleDrag = 0; doc.markDirty(); listener?.onGuidesChanged() }
            PenMode.NONE -> Unit
        }
        penMode = PenMode.NONE
        invalidate()
    }

    /** Scratch-out or hand-drawn connector. Returns true when the stroke was consumed. */
    private fun smartInkAction(): Boolean {
        if (doc.guides.symmetry != SymmetryMode.OFF || penType == PenType.HIGHLIGHTER) return false
        val xy = FloatArray(liveN * 2) { k -> livePts[(k / 2) * 3 + (k % 2)] }
        val tol = 8f * density / scale
        if (com.inkboard.model.InkGestures.isScratch(xy)) {
            val targets = com.inkboard.model.InkGestures.scratchTargets(doc, xy, tol) + com.inkboard.model.InkGestures.scratchEdges(doc, xy, tol)
            if (targets.isNotEmpty()) {
                doc.remove(targets)
                performHapticFeedback(HapticFeedbackConstants.CONFIRM)
                return true
            }
        }
        return tryConnector(xy)
    }

    /** If [xy] runs from one element to another, add a connector instead of ink. */
    private fun tryConnector(xy: FloatArray): Boolean {
        val tol = 10f * density / scale
        val pair = com.inkboard.model.InkGestures.connectorFor(doc, xy, tol) ?: return false
        doc.add(Edge(doc.nextId(), pair.first.id, pair.second.id, penColor or 0xFF000000.toInt(), max(liveWidth, 1.5f * density / scale)))
        performHapticFeedback(HapticFeedbackConstants.CONFIRM)
        return true
    }

    private fun fadeLasers() {
        val now = SystemClock.uptimeMillis()
        laserTrails.removeAll { now - it.t > LASER_MS }
        invalidate()
        if (laserTrails.isNotEmpty()) postOnAnimation { fadeLasers() }
    }

    private fun penCancel() {
        timer.removeCallbacks(holdRunnable)
        stopAutoScroll()
        liveN = 0; snapped = null; perspLine = null; hidden.clear(); eraseWork.clear(); lasso.clear()
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
        val bb = Box.of(pts)
        val folded = doc.hiddenIds()
        val ids = doc.query(bb).filter { it.id !in doc.locked && it.id !in folded && it.inLasso(pts) }.map { it.id }
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
            if (penPointer < 0 && !penNear()) timer.postDelayed(eyedropRunnable, EYEDROP_MS)
        } else {
            timer.removeCallbacks(eyedropRunnable)
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
        if (eyedropping) {
            fingers.values.firstOrNull()?.let { sampleAt(it.x, it.y) }
            return
        }
        val anyMoved = fingers.values.any { it.moved }
        if (!anyMoved) return
        timer.removeCallbacks(eyedropRunnable)

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
            val r = planeSelRect()?.apply { inset(-6f * density, -6f * density) }
            handleDrag = hitGuideHandle(f.sx, f.sy)
            if (handleDrag != 0) {
                fingerGesture = 4
            } else if (r != null && r.contains(f.sx, f.sy)) {
                fingerGesture = 3
                fingerDragWX = wx(f.sx); fingerDragWY = wy(f.sy)
            } else {
                fingerGesture = 1
                gestureCX = f.sx; gestureCY = f.sy
            }
        }
        when (fingerGesture) {
            4 -> moveGuideHandle(handleDrag, f.x, f.y)
            1 -> {
                tx += f.x - gestureCX; ty += f.y - gestureCY
                gestureCX = f.x; gestureCY = f.y
                saveCamera(); notifySelection(); invalidate()
            }
            3 -> {
                selPreview = snapTranslate(wx(f.x) - fingerDragWX, wy(f.y) - fingerDragWY)
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
        timer.removeCallbacks(eyedropRunnable)
        if (eyedropping && fingers.isEmpty()) {
            if (canceled) { eyedropping = false; eyeValid = false; fingerGesture = 0; invalidate() } else finishEyedrop()
            return
        }
        if (fingerGesture == 4 && fingers.isEmpty()) {
            fingerGesture = 0; handleDrag = 0; doc.markDirty(); listener?.onGuidesChanged(); return
        }
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
        timer.removeCallbacks(holdRunnable); stopAutoScroll()
        chunkList.forEach { it.node.discardDisplayList() }
    }

    companion object {
        const val MIN_SCALE = 0.001f
        const val MAX_SCALE = 100f
        const val HOLD_MS = 550L
        const val TAP_MS = 280L
        const val EYEDROP_MS = 600L
        /** Strokes smaller than this on screen are never snapped to shapes. */
        const val MIN_SHAPE_DP = 48f
        const val LASER_MS = 1200L
    }
}
