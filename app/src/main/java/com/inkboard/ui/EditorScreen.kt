package com.inkboard.ui

import android.graphics.RectF
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.List
import androidx.compose.material.icons.automirrored.outlined.Redo
import androidx.compose.material.icons.automirrored.outlined.Undo
import androidx.compose.material.icons.outlined.AccountTree
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.outlined.AutoAwesome
import androidx.compose.material.icons.outlined.ContentCopy
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.FlipToBack
import androidx.compose.material.icons.outlined.FlipToFront
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material.icons.outlined.Palette
import androidx.compose.material.icons.outlined.TextFields
import androidx.compose.material.icons.outlined.ZoomOutMap
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import com.inkboard.model.PressureCurve
import androidx.compose.ui.graphics.Brush
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Switch
import androidx.compose.material3.Slider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.inkboard.canvas.InkCanvasView
import com.inkboard.canvas.Tool
import com.inkboard.data.DocMeta
import com.inkboard.data.DocStore
import com.inkboard.export.ExportFormat
import com.inkboard.export.Exporter
import com.inkboard.model.Background
import com.inkboard.model.Document
import com.inkboard.model.Element
import com.inkboard.model.Node
import com.inkboard.model.NodeKind
import com.inkboard.model.PenType
import com.inkboard.model.ShapeEl
import com.inkboard.model.StickyEl
import com.inkboard.model.Stroke
import com.inkboard.model.TableEl
import com.inkboard.model.TextEl
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlin.math.roundToInt

/** Outlives screens so saves finish after the editor closes. */
val AppScope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

private val PEN_COLORS = listOf(
    0xFF202124, 0xFF5F6368, 0xFFD93025, 0xFFF29900, 0xFF188038, 0xFF1A73E8, 0xFF9334E6, 0xFFE52592,
).map { it.toInt() }
private val NODE_FILLS = listOf(0xFFBBDEFB, 0xFFC8E6C9, 0xFFFFE0B2, 0xFFF8BBD0, 0xFFD1C4E9, 0xFFFFF9C4, 0xFFFFFFFF, 0xFFCFD8DC).map { it.toInt() }
private val WIDTHS = listOf(1.2f, 2.5f, 5f, 10f)

@Composable
fun EditorScreen(store: DocStore, prefs: Prefs, meta: DocMeta, dark: Boolean, onClose: () -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val loaded by produceState<Result<Document>?>(null, meta.id) {
        value = withContext(Dispatchers.IO) { runCatching { store.load(meta.id) } }
    }
    val result = loaded
    if (result == null) {
        Box(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background))
        return
    }
    val document = result.getOrElse {
        LaunchedEffect(Unit) { Toast.makeText(context, "Không mở được tài liệu: ${it.message}", Toast.LENGTH_LONG).show(); onClose() }
        return
    }

    var tick by remember { mutableIntStateOf(0) }
    var savedVersion by remember { mutableStateOf(document.version) }
    var tool by remember { mutableStateOf(Tool.PEN) }
    var penColor by remember { mutableIntStateOf(prefs.penColor) }
    var penWidth by remember { mutableFloatStateOf(prefs.penWidth) }
    var penType by remember { mutableStateOf(PenType.entries.getOrElse(prefs.penType) { PenType.PEN }) }
    var selRect by remember { mutableStateOf<RectF?>(null) }
    var selIds by remember { mutableStateOf(emptySet<Long>()) }
    var zoom by remember { mutableFloatStateOf(document.camScale) }
    var showPenOptions by remember { mutableStateOf(false) }
    var showInsert by remember { mutableStateOf(false) }
    var showMenu by remember { mutableStateOf(false) }
    var showColor by remember { mutableStateOf(false) }
    var editing by remember { mutableStateOf<Element?>(null) }
    var editPoint by remember { mutableStateOf(Float.NaN to Float.NaN) }
    var newText by remember { mutableStateOf(false) }
    var exportFormat by remember { mutableStateOf<ExportFormat?>(null) }
    var busy by remember { mutableStateOf("") }
    var askDownload by remember { mutableStateOf(false) }
    var rulers by remember { mutableStateOf(prefs.showRulers) }
    var minimap by remember { mutableStateOf(prefs.showMinimap) }
    var showComponents by remember { mutableStateOf(false) }
    var pointer by remember { mutableStateOf<Pair<Float, Float>?>(null) }
    var penOpacity by remember { mutableFloatStateOf(prefs.penOpacity) }
    var stabilizer by remember { mutableIntStateOf(prefs.stabilizer) }
    var curve by remember { mutableStateOf(PressureCurve.entries.getOrElse(prefs.pressureCurve) { PressureCurve.NORMAL }) }
    var taper by remember { mutableStateOf(prefs.taper) }
    var recent by remember { mutableStateOf(prefs.recentColors) }
    var showColorPicker by remember { mutableStateOf(false) }
    var showGuides by remember { mutableStateOf(false) }
    var viewRot by remember { mutableFloatStateOf(0f) }
    var viewFlip by remember { mutableStateOf(false) }
    var rotateOn by remember { mutableStateOf(prefs.rotateCanvas) }
    var guidesTick by remember { mutableIntStateOf(0) }
    val refFile = remember { java.io.File(context.filesDir, "reference.jpg") }
    var refVisible by remember { mutableStateOf(prefs.refImageVisible && refFile.exists()) }
    var refVersion by remember { mutableIntStateOf(0) }

    val view = remember {
        InkCanvasView(context).apply {
            doc = document
            this.dark = dark
            this.tool = Tool.PEN
            this.penColor = withAlpha(penColor, penOpacity); this.penWidthDp = penWidth; this.penType = penType
            stabilizerLevel = stabilizer; pressureCurve = curve; this.taper = taper
            rotationEnabled = prefs.rotateCanvas
            fingerDraws = prefs.fingerDraws
            autoShape = prefs.autoShape
            showRulers = prefs.showRulers
            showMinimap = prefs.showMinimap
        }
    }
    val actions = remember { EditorActions(view) }

    fun toast(s: String) = Toast.makeText(context, s, Toast.LENGTH_SHORT).show()

    fun saveNow(after: (() -> Unit)? = null) {
        val snap = document.snapshot()
        val ver = document.version
        AppScope.launch {
            runCatching {
                store.save(meta.id, snap)
                store.saveThumbnail(meta.id, snap)
            }.onFailure { withContext(Dispatchers.Main) { toast("Lỗi lưu: ${it.message}") } }
            withContext(Dispatchers.Main) {
                savedVersion = ver
                after?.invoke()
            }
        }
    }

    fun close() = saveNow { onClose() }

    fun undo() { if (document.undo() != null) { view.onDocMutated(); tick++ } }
    fun redo() { if (document.redo() != null) { view.onDocMutated(); tick++ } }

    DisposableEffect(view) {
        view.listener = object : InkCanvasView.Listener {
            override fun onSelectionChanged(screenRect: RectF?, ids: Set<Long>) { selRect = screenRect; selIds = ids }
            override fun onDocChanged() { tick++ }
            override fun onCameraChanged(scale: Float) { zoom = scale }
            override fun onEditRequest(el: Element, wx: Float, wy: Float) {
                if (isEditable(el)) { editPoint = wx to wy; editing = el }
            }
            override fun onPointer(wx: Float, wy: Float) { pointer = wx to wy }
            override fun onColorPicked(color: Int) {
                penColor = color; view.penColor = withAlpha(color, penOpacity); prefs.penColor = color
            }
            override fun onViewTransformChanged(rotation: Float, flipped: Boolean) { viewRot = rotation; viewFlip = flipped }
            override fun onGuidesChanged() { guidesTick++; tick++ }
        }
        view.onUndoGesture = { undo() }
        view.onRedoGesture = { redo() }
        onDispose { view.listener = null }
    }

    // Autosave 1.2 s after the last change.
    LaunchedEffect(tick) {
        if (document.version != savedVersion) { delay(1200); saveNow() }
    }

    // Save when the app goes to background.
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    DisposableEffect(lifecycle) {
        val obs = LifecycleEventObserver { _, e -> if (e == Lifecycle.Event.ON_STOP && document.version != savedVersion) saveNow() }
        lifecycle.addObserver(obs)
        onDispose { lifecycle.removeObserver(obs) }
    }
    BackHandler { if (selIds.isNotEmpty()) view.clearSelection() else close() }

    fun setTool(t: Tool) { tool = t; view.tool = t }
    fun setToolSelect() = setTool(Tool.SELECT)

    val pickReference = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) AppScope.launch {
            val f = runCatching { saveReferenceImage(context, uri) }.getOrNull()
            withContext(Dispatchers.Main) {
                if (f == null) toast("Không đọc được ảnh") else { refVersion++; refVisible = true; prefs.refImageVisible = true }
            }
        }
    }

    val pickImage = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) {
            busy = "Đang chèn ảnh…"
            AppScope.launch {
                val img = runCatching { EditorActions.prepareImage(context, uri) }.getOrNull()
                withContext(Dispatchers.Main) {
                    busy = ""
                    if (img == null) toast("Không đọc được ảnh") else { actions.insertPrepared(img); setToolSelect(); tick++ }
                }
            }
        }
    }


    fun runInkToText() {
        busy = "Đang nhận dạng chữ…"
        scope.launch {
            try {
                val text = actions.inkToText(prefs.recogLang)
                if (text.isBlank()) toast("Không nhận ra chữ") else tick++
            } catch (e: IllegalStateException) {
                if (e.message == "MODEL_MISSING") askDownload = true else toast("Lỗi: ${e.message}")
            } catch (e: Exception) {
                toast("Lỗi nhận dạng: ${e.message}")
            } finally { busy = "" }
        }
    }

    Box(Modifier.fillMaxSize()) {
        AndroidView(factory = { view }, modifier = Modifier.fillMaxSize())

        val rulerPad = if (rulers) 22.dp else 0.dp
        Box(Modifier.fillMaxSize().safeDrawingPadding().padding(start = rulerPad, top = rulerPad)) {
            // ----- Top-left: back + name
            Row(Modifier.align(Alignment.TopStart).padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Pill {
                    IconButton(onClick = { close() }) { Icon(Icons.AutoMirrored.Outlined.ArrowBack, "Quay lại") }
                    Text(meta.name, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.widthIn(max = 140.dp).padding(end = 12.dp),
                        style = MaterialTheme.typography.bodyMedium)
                }
            }

            // ----- Top-center: main toolbar (6 items)
            Box(Modifier.align(Alignment.TopCenter).padding(top = 8.dp)) {
                Pill {
                    ToolButton(InkIcons.Pen, "Bút", tool == Tool.PEN, tint = Color(penColor)) {
                        if (tool == Tool.PEN) showPenOptions = !showPenOptions else setTool(Tool.PEN)
                    }
                    ToolButton(InkIcons.Eraser, "Tẩy", tool == Tool.ERASER) { setTool(Tool.ERASER) }
                    ToolButton(InkIcons.Lasso, "Chọn", tool == Tool.SELECT) { setTool(Tool.SELECT) }
                    Box {
                        ToolButton(Icons.Outlined.Add, "Chèn", false) { showInsert = true }
                        InsertMenu(showInsert, onDismiss = { showInsert = false }) { item ->
                            showInsert = false
                            when (item) {
                                "text" -> newText = true
                                "sticky" -> actions.insertSticky()
                                "image" -> pickImage.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                                "mindmap" -> actions.insertMindmap()
                                "table" -> actions.insertTable()
                                "process" -> actions.insertNode(NodeKind.ROUND, "Bước")
                                "decision" -> actions.insertNode(NodeKind.DIAMOND, "Điều kiện?")
                                "terminal" -> actions.insertNode(NodeKind.ELLIPSE, "Bắt đầu")
                                else -> actions.insertShape(item, penColor)
                            }
                            if (item != "text" && item != "image") { setTool(Tool.SELECT); tick++ }
                        }
                    }
                    ToolButton(Icons.AutoMirrored.Outlined.Undo, "Hoàn tác", false, enabled = tick >= 0 && document.canUndo) { undo() }
                    ToolButton(Icons.AutoMirrored.Outlined.Redo, "Làm lại", false, enabled = tick >= 0 && document.canRedo) { redo() }
                    Box {
                        ToolButton(Icons.Outlined.MoreVert, "Menu", false) { showMenu = true }
                        MainMenu(showMenu, document.background, rulers, minimap, rotateOn, refVisible, onDismiss = { showMenu = false }) { item ->
                            showMenu = false
                            when (item) {
                                "png" -> exportFormat = ExportFormat.PNG
                                "pdf" -> exportFormat = ExportFormat.PDF
                                "bg_plain" -> { document.background = Background.PLAIN; document.markDirty(); view.invalidate(); tick++ }
                                "bg_dots" -> { document.background = Background.DOTS; document.markDirty(); view.invalidate(); tick++ }
                                "bg_grid" -> { document.background = Background.GRID; document.markDirty(); view.invalidate(); tick++ }
                                "bg_lines" -> { document.background = Background.LINES; document.markDirty(); view.invalidate(); tick++ }
                                "fit" -> view.zoomToContent()
                                "rulers" -> { rulers = !rulers; view.showRulers = rulers; prefs.showRulers = rulers }
                                "minimap" -> { minimap = !minimap; view.showMinimap = minimap; prefs.showMinimap = minimap }
                                "components" -> showComponents = true
                                "guides" -> showGuides = true
                                "flip" -> view.toggleFlip()
                                "rotate" -> { rotateOn = !rotateOn; view.rotationEnabled = rotateOn; prefs.rotateCanvas = rotateOn }
                                "reference" -> if (refFile.exists()) { refVisible = !refVisible; prefs.refImageVisible = refVisible } else pickReference.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                            }
                        }
                    }
                }
                if (showPenOptions && tool == Tool.PEN) {
                    PenOptions(
                        Modifier.align(Alignment.TopCenter).offset(y = 60.dp),
                        penColor, penWidth, penType, penOpacity, stabilizer, curve, taper, recent,
                        onColor = { penColor = it; view.penColor = withAlpha(it, penOpacity); prefs.penColor = it },
                        onCustomColor = { showColorPicker = true },
                        onWidth = { penWidth = it; view.penWidthDp = it; prefs.penWidth = it },
                        onType = { penType = it; view.penType = it; prefs.penType = it.ordinal },
                        onOpacity = { penOpacity = it; view.penColor = withAlpha(penColor, it); prefs.penOpacity = it },
                        onStabilizer = { stabilizer = it; view.stabilizerLevel = it; prefs.stabilizer = it },
                        onCurve = { curve = it; view.pressureCurve = it; prefs.pressureCurve = it.ordinal },
                        onTaper = { taper = it; view.taper = it; prefs.taper = it },
                        onDismiss = { showPenOptions = false }
                    )
                }
            }

            // ----- Bottom: zoom + fit
            Row(Modifier.align(Alignment.BottomStart).padding(12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Pill(Modifier.clickable { view.setZoom(1f) }) {
                    Text("${(zoom * 100).roundToInt()}%", modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp), style = MaterialTheme.typography.labelLarge)
                }
                if (viewRot != 0f || viewFlip) {
                    Pill(Modifier.clickable { view.resetViewTransform() }) {
                        Text(
                            listOfNotNull(if (viewRot != 0f) "↻ ${viewRot.roundToInt()}°" else null, if (viewFlip) "⇋ lật" else null).joinToString("  ") + "  ✕",
                            modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp), style = MaterialTheme.typography.labelLarge
                        )
                    }
                }
                if (rulers) {
                    val selBox = remember(selIds, tick) { if (selIds.isEmpty()) null else document.boundsOf(selIds).takeUnless { it.isEmpty } }
                    val label = if (selBox != null) {
                        "Chọn  X ${selBox.l.roundToInt()}  Y ${(-selBox.t).roundToInt()}  ·  ${selBox.w.roundToInt()} × ${selBox.h.roundToInt()}"
                    } else {
                        pointer?.let { (x, y) -> "X ${x.roundToInt()}   Y ${(-y).roundToInt()}" } ?: "X —   Y —"
                    }
                    Pill { Text(label, modifier = Modifier.padding(horizontal = 14.dp, vertical = 10.dp), style = MaterialTheme.typography.labelLarge) }
                }
            }
            Row(Modifier.align(Alignment.BottomEnd).padding(12.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Pill { IconButton(onClick = { showComponents = true }) { Icon(Icons.AutoMirrored.Outlined.List, "Thành phần") } }
                Pill { IconButton(onClick = { view.zoomToContent() }) { Icon(Icons.Outlined.ZoomOutMap, "Về nội dung") } }
            }

            if (busy.isNotEmpty()) {
                Pill(Modifier.align(Alignment.BottomCenter).padding(bottom = 16.dp)) {
                    Text(busy, modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp))
                }
            }
        }

        // ----- Context bar near the selection
        val rect = selRect
        if (rect != null && selIds.isNotEmpty()) {
            ContextBar(
                rect, tick,
                hasStrokes = remember(selIds, tick) { actions.selectedStrokes().isNotEmpty() },
                singleStroke = remember(selIds, tick) { actions.selected().let { it.size == 1 && it[0] is Stroke } },
                editable = remember(selIds, tick) { actions.selected().singleOrNull { isEditable(it) && it !is TableEl } },
                mindmapNode = remember(selIds, tick) { (document.byId(view.primaryId) as? Node)?.takeIf { it.mindmap } },
                twoNodes = remember(selIds, tick) { actions.selectedNodes().size == 2 && actions.selectedNodes().none { it.mindmap } },
                table = remember(selIds, tick) { actions.selected().singleOrNull() as? TableEl },
                onAction = { a ->
                    when (a) {
                        "text" -> runInkToText()
                        "shape" -> if (!actions.strokeToShape()) toast("Chưa nhận ra hình")
                        "color" -> showColor = true
                        "edit" -> { editPoint = Float.NaN to Float.NaN; editing = actions.selected().singleOrNull { isEditable(it) && it !is TableEl } }
                        "dup" -> actions.duplicate()
                        "front" -> actions.reorder(true)
                        "back" -> actions.reorder(false)
                        "delete" -> actions.delete()
                        "branch" -> actions.addBranch()
                        "tidy" -> actions.tidyMindmap()
                        "connect" -> actions.connect()
                        "row+" -> actions.tableResize(1, 0)
                        "row-" -> actions.tableResize(-1, 0)
                        "col+" -> actions.tableResize(0, 1)
                        "col-" -> actions.tableResize(0, -1)
                    }
                    tick++
                }
            )
        }
    }

    if (refVisible) {
        ReferenceWindow(refFile, refVersion, onClose = { refVisible = false; prefs.refImageVisible = false }) { c ->
            penColor = c; view.penColor = withAlpha(c, penOpacity); prefs.penColor = c
            prefs.pushRecent(c); recent = prefs.recentColors
            toast("Đã lấy màu")
        }
    }
    if (showColorPicker) {
        ColorPickerDialog(penColor, recent, onDismiss = { showColorPicker = false }) { c ->
            showColorPicker = false
            penColor = c; view.penColor = withAlpha(c, penOpacity); prefs.penColor = c
            prefs.pushRecent(c); recent = prefs.recentColors
        }
    }
    if (showGuides) {
        val g = remember(guidesTick, showGuides) { document.guides }
        GuidesDialog(
            g.symmetry, g.radialN, g.perspective, g.assist, refFile.exists(),
            onSymmetry = { m, n -> view.setSymmetry(m, n); guidesTick++ },
            onPerspective = { view.setPerspective(it); guidesTick++ },
            onAssist = { view.setPerspectiveAssist(it); guidesTick++ },
            onReference = { showGuides = false; pickReference.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly)) },
            onDismiss = { showGuides = false },
        )
    }
    if (showComponents) {
        ComponentsPanel(document, tick, onDismiss = { showComponents = false }) { c ->
            showComponents = false
            view.focusOn(c.box)
        }
    }

    // ----- Dialogs
    editing?.let { el ->
        val (px, py) = editPoint
        val initial = when (el) {
            is TextEl -> el.text; is StickyEl -> el.text; is Node -> el.text; is ShapeEl -> el.label
            is TableEl -> el.cells[if (px.isNaN()) 0 else el.cellAt(px, py)].orEmpty()
            else -> ""
        }
        val title = when (el) { is ShapeEl -> "Chữ trong hình"; is TableEl -> "Chữ trong ô"; else -> "Sửa chữ" }
        TextInputDialog(title, initial, singleLine = false, onDismiss = { editing = null }) { t ->
            editing = null
            document.byId(el.id)?.let { actions.setText(it, t, px, py) }
            tick++
        }
    }
    if (newText) {
        TextInputDialog("Thêm chữ", "", singleLine = false, onDismiss = { newText = false }) { t ->
            newText = false
            actions.insertText(t, penColor); setTool(Tool.SELECT); tick++
        }
    }
    if (showColor) {
        ColorDialog(onDismiss = { showColor = false }) { i ->
            showColor = false
            actions.recolor(PEN_COLORS[i], EditorActions.STICKY_COLORS[i % EditorActions.STICKY_COLORS.size], NODE_FILLS[i])
            tick++
        }
    }
    if (askDownload) {
        AlertDialog(
            onDismissRequest = { askDownload = false },
            title = { Text("Cần gói nhận dạng chữ") },
            text = { Text("Tải một lần gói nhận dạng (cần mạng). Sau đó dùng offline hoàn toàn.") },
            confirmButton = {
                TextButton(onClick = {
                    askDownload = false
                    busy = "Đang tải gói nhận dạng…"
                    scope.launch {
                        try {
                            com.inkboard.recog.InkToText.download(prefs.recogLang)
                            busy = ""
                            runInkToText()
                        } catch (e: Exception) { busy = ""; toast("Chưa tải được: ${e.message ?: "lỗi mạng"}") }
                    }
                }) { Text("Tải ngay") }
            },
            dismissButton = { TextButton(onClick = { askDownload = false }) { Text("Để sau") } }
        )
    }
    exportFormat?.let { fmt ->
        val region = if (selIds.isNotEmpty()) "vùng đã chọn" else "toàn bộ nội dung"
        AlertDialog(
            onDismissRequest = { exportFormat = null },
            title = { Text("Xuất ${fmt.name}") },
            text = { Text("Xuất $region thành ${fmt.name}.") },
            confirmButton = {
                Row {
                    for ((mode, label) in listOf("share" to "Chia sẻ", "save" to "Lưu vào Tải xuống")) {
                        TextButton(onClick = {
                            exportFormat = null
                            val snap = document.snapshot()
                            val box = if (selIds.isNotEmpty()) document.boundsOf(selIds) else document.contentBounds()
                            if (box.isEmpty) { toast("Chưa có nội dung"); return@TextButton }
                            busy = "Đang xuất…"
                            AppScope.launch {
                                val r = runCatching {
                                    val f = Exporter.export(context, snap, box, fmt, meta.name)
                                    if (mode == "save") Exporter.saveToDownloads(context, f, fmt)
                                    f
                                }
                                withContext(Dispatchers.Main) {
                                    busy = ""
                                    r.onSuccess { f ->
                                        if (mode == "share") Exporter.share(context, f, fmt) else toast("Đã lưu vào Tải xuống/InkBoard")
                                    }.onFailure { toast("Lỗi xuất: ${it.message}") }
                                }
                            }
                        }) { Text(label) }
                    }
                }
            },
            dismissButton = { TextButton(onClick = { exportFormat = null }) { Text("Huỷ") } }
        )
    }
}

// ------------------------------------------------------------------ pieces

private fun isEditable(el: Element) =
    el is TextEl || el is StickyEl || el is Node || el is TableEl || (el is ShapeEl && el.closed)

@Composable
private fun Pill(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    Surface(
        modifier = modifier.shadow(6.dp, RoundedCornerShape(28.dp)),
        shape = RoundedCornerShape(28.dp),
        color = MaterialTheme.colorScheme.surface,
        tonalElevation = 2.dp,
    ) { Row(verticalAlignment = Alignment.CenterVertically) { content() } }
}

@Composable
private fun ToolButton(icon: ImageVector, label: String, active: Boolean, tint: Color? = null, enabled: Boolean = true, onClick: () -> Unit) {
    val bg = if (active) MaterialTheme.colorScheme.primary.copy(alpha = 0.14f) else Color.Transparent
    IconButton(onClick = onClick, enabled = enabled, modifier = Modifier.padding(2.dp).clip(CircleShape).background(bg)) {
        Icon(icon, label, tint = if (!enabled) MaterialTheme.colorScheme.onSurface.copy(alpha = 0.3f) else tint ?: MaterialTheme.colorScheme.onSurface)
    }
}

@Composable
private fun PenOptions(
    modifier: Modifier, color: Int, width: Float, type: PenType, opacity: Float, stabilizer: Int,
    curve: PressureCurve, taper: Boolean, recent: List<Int>,
    onColor: (Int) -> Unit, onCustomColor: () -> Unit, onWidth: (Float) -> Unit, onType: (PenType) -> Unit,
    onOpacity: (Float) -> Unit, onStabilizer: (Int) -> Unit, onCurve: (PressureCurve) -> Unit, onTaper: (Boolean) -> Unit,
    onDismiss: () -> Unit,
) {
    Surface(modifier.shadow(8.dp, RoundedCornerShape(20.dp)).widthIn(max = 420.dp), shape = RoundedCornerShape(20.dp), color = MaterialTheme.colorScheme.surface) {
        Column(Modifier.padding(12.dp).verticalScroll(rememberScrollState())) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                for ((t, label) in listOf(PenType.PEN to "Bút", PenType.HIGHLIGHTER to "Bút nhấn", PenType.PENCIL to "Bút chì")) {
                    val sel = t == type
                    Text(label, modifier = Modifier.clip(RoundedCornerShape(12.dp))
                        .background(if (sel) MaterialTheme.colorScheme.primary.copy(alpha = 0.15f) else Color.Transparent)
                        .clickable { onType(t) }.padding(horizontal = 12.dp, vertical = 6.dp),
                        fontWeight = if (sel) FontWeight.Bold else FontWeight.Normal)
                }
            }
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
                for (c in PEN_COLORS) Swatch(c, c == color) { onColor(c) }
                // Custom colour (HSV picker)
                Box(Modifier.size(30.dp).clip(CircleShape)
                    .background(Brush.sweepGradient(listOf(Color.Red, Color.Yellow, Color.Green, Color.Cyan, Color.Blue, Color.Magenta, Color.Red)))
                    .clickable(onClick = onCustomColor), contentAlignment = Alignment.Center) {
                    Text("+", color = Color.White, fontWeight = FontWeight.Bold)
                }
            }
            val extra = recent.filter { it !in PEN_COLORS }.take(9)
            if (extra.isNotEmpty() || color !in PEN_COLORS) {
                Spacer(Modifier.height(6.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    if (color !in PEN_COLORS && color !in extra) Swatch(color, true) {}
                    for (c in extra) Swatch(c, c == color) { onColor(c) }
                }
            }
            Text("Mẹo: giữ 1 ngón tay lên bản vẽ để hút màu.", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 6.dp))
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                for (w in WIDTHS) {
                    Box(Modifier.size(36.dp).clip(CircleShape)
                        .background(if (w == width) MaterialTheme.colorScheme.primary.copy(alpha = 0.15f) else Color.Transparent)
                        .clickable { onWidth(w) }, contentAlignment = Alignment.Center) {
                        Box(Modifier.size((w * 2.2f).coerceIn(3f, 24f).dp).clip(CircleShape).background(Color(withAlpha(color, opacity))))
                    }
                }
            }
            LabeledSlider("Độ đậm ${(opacity * 100).roundToInt()}%", opacity, 0.1f..1f) { onOpacity(it) }
            LabeledSlider("Chống rung nét ${if (stabilizer == 0) "tắt" else stabilizer.toString()}", stabilizer.toFloat(), 0f..10f, steps = 9) { onStabilizer(it.roundToInt()) }
            Text("Lực nhấn", style = MaterialTheme.typography.labelMedium, modifier = Modifier.padding(top = 4.dp))
            Row {
                for (c in PressureCurve.entries) {
                    FilterChip(selected = c == curve, onClick = { onCurve(c) }, label = { Text(c.label) }, modifier = Modifier.padding(end = 6.dp))
                }
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("Thon đầu & cuối nét", modifier = Modifier.weight(1f))
                Switch(checked = taper, onCheckedChange = onTaper)
            }
            if (type == PenType.PENCIL) Text("Bút chì: nghiêng bút để tô bóng (nếu bút hỗ trợ cảm biến nghiêng).",
                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row { Spacer(Modifier.weight(1f)); TextButton(onClick = onDismiss) { Text("Xong") } }
        }
    }
}

@Composable
private fun Swatch(c: Int, selected: Boolean, onClick: () -> Unit) {
    Box(Modifier.size(30.dp).clip(CircleShape).background(Color(c))
        .border(if (selected) 3.dp else 0.dp, MaterialTheme.colorScheme.primary.copy(alpha = 0.6f), CircleShape)
        .clickable(onClick = onClick))
}

@Composable
private fun LabeledSlider(label: String, value: Float, range: ClosedFloatingPointRange<Float>, steps: Int = 0, onChange: (Float) -> Unit) {
    Column(Modifier.padding(top = 4.dp)) {
        Text(label, style = MaterialTheme.typography.labelMedium)
        Slider(value = value, onValueChange = onChange, valueRange = range, steps = steps)
    }
}

fun withAlpha(color: Int, opacity: Float): Int = (color and 0x00FFFFFF) or ((opacity.coerceIn(0f, 1f) * 255).roundToInt() shl 24)

@Composable
private fun InsertMenu(expanded: Boolean, onDismiss: () -> Unit, onPick: (String) -> Unit) {
    DropdownMenu(expanded = expanded, onDismissRequest = onDismiss) {
        MenuItem("Chữ", Icons.Outlined.TextFields) { onPick("text") }
        MenuItem("Ghi chú dán", Icons.Outlined.Edit) { onPick("sticky") }
        MenuItem("Ảnh", Icons.Outlined.Image) { onPick("image") }
        HorizontalDivider()
        MenuItem("Mindmap", Icons.Outlined.AccountTree) { onPick("mindmap") }
        MenuItem("Khối quy trình", null) { onPick("process") }
        MenuItem("Khối quyết định ◇", null) { onPick("decision") }
        MenuItem("Bắt đầu / Kết thúc ⬭", null) { onPick("terminal") }
        MenuItem("Bảng 3×3", null) { onPick("table") }
        HorizontalDivider()
        MenuItem("Hình chữ nhật ▭", null) { onPick("rect") }
        MenuItem("Hình tròn ○", null) { onPick("ellipse") }
        MenuItem("Tam giác △", null) { onPick("triangle") }
        MenuItem("Hình thoi ◇", null) { onPick("diamond") }
        MenuItem("Đường thẳng ─", null) { onPick("line") }
        MenuItem("Mũi tên →", null) { onPick("arrow") }
    }
}

@Composable
private fun MainMenu(expanded: Boolean, bg: Background, rulers: Boolean, minimap: Boolean, rotateOn: Boolean, refOn: Boolean, onDismiss: () -> Unit, onPick: (String) -> Unit) {
    DropdownMenu(expanded = expanded, onDismissRequest = onDismiss) {
        MenuItem("Trợ vẽ: đối xứng, phối cảnh…", null) { onPick("guides") }
        MenuItem("Ảnh tham khảo" + if (refOn) "  ✓" else "", null) { onPick("reference") }
        MenuItem("Lật canvas ngang", null) { onPick("flip") }
        MenuItem("Xoay canvas bằng 2 ngón" + if (rotateOn) "  ✓" else "", null) { onPick("rotate") }
        HorizontalDivider()
        MenuItem("Thành phần trên bảng vẽ", null) { onPick("components") }
        MenuItem("Thước toạ độ" + if (rulers) "  ✓" else "", null) { onPick("rulers") }
        MenuItem("Bản đồ thu nhỏ" + if (minimap) "  ✓" else "", null) { onPick("minimap") }
        HorizontalDivider()
        MenuItem("Xuất PNG", null) { onPick("png") }
        MenuItem("Xuất PDF", null) { onPick("pdf") }
        HorizontalDivider()
        MenuItem("Về toàn bộ nội dung", null) { onPick("fit") }
        HorizontalDivider()
        for ((b, label) in listOf(Background.PLAIN to "Nền trơn", Background.DOTS to "Nền chấm", Background.GRID to "Nền lưới", Background.LINES to "Nền dòng kẻ")) {
            MenuItem(label + if (b == bg) "  ✓" else "", null) { onPick("bg_" + b.name.lowercase()) }
        }
    }
}

@Composable
private fun MenuItem(label: String, icon: ImageVector?, onClick: () -> Unit) {
    DropdownMenuItem(text = { Text(label) }, onClick = onClick, leadingIcon = icon?.let { { Icon(it, null) } })
}

@Composable
private fun ContextBar(
    rect: RectF, tick: Int,
    hasStrokes: Boolean, singleStroke: Boolean, editable: Element?, mindmapNode: Node?, twoNodes: Boolean, table: TableEl?,
    onAction: (String) -> Unit,
) {
    val density = LocalDensity.current
    BoxWithConstraints(Modifier.fillMaxSize()) {
        val barH = with(density) { 56.dp.toPx() }
        val maxW = with(density) { maxWidth.toPx() }
        val above = rect.top - barH - with(density) { 16.dp.toPx() }
        val y = if (above > with(density) { 80.dp.toPx() }) above else rect.bottom + with(density) { 16.dp.toPx() }
        val x = rect.centerX().coerceIn(with(density) { 24.dp.toPx() }, maxW - with(density) { 24.dp.toPx() })
        Box(Modifier.offset { IntOffset((x - with(density) { 170.dp.toPx() }).roundToInt().coerceAtLeast(8), y.roundToInt().coerceAtLeast(8)) }) {
            Surface(Modifier.shadow(8.dp, RoundedCornerShape(16.dp)).widthIn(max = 520.dp), shape = RoundedCornerShape(16.dp), color = MaterialTheme.colorScheme.surface) {
                Row(Modifier.horizontalScroll(rememberScrollState()).padding(horizontal = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    if (hasStrokes) CtxButton("Chữ", Icons.Outlined.TextFields) { onAction("text") }
                    if (singleStroke) CtxButton("Hình", Icons.Outlined.AutoAwesome) { onAction("shape") }
                    if (editable != null) CtxButton("Sửa", Icons.Outlined.Edit) { onAction("edit") }
                    if (mindmapNode != null) {
                        CtxButton("Nhánh", Icons.Outlined.Add) { onAction("branch") }
                        CtxButton("Sắp xếp", Icons.Outlined.AccountTree) { onAction("tidy") }
                    }
                    if (twoNodes) CtxButton("Nối", null, "→") { onAction("connect") }
                    if (table != null) {
                        CtxButton("Hàng", null, "+") { onAction("row+") }
                        CtxButton("Hàng", null, "−") { onAction("row-") }
                        CtxButton("Cột", null, "+") { onAction("col+") }
                        CtxButton("Cột", null, "−") { onAction("col-") }
                    }
                    CtxButton("Màu", Icons.Outlined.Palette) { onAction("color") }
                    CtxButton("Nhân đôi", Icons.Outlined.ContentCopy) { onAction("dup") }
                    CtxButton("Lên trên", Icons.Outlined.FlipToFront) { onAction("front") }
                    CtxButton("Xuống dưới", Icons.Outlined.FlipToBack) { onAction("back") }
                    CtxButton("Xoá", Icons.Outlined.Delete) { onAction("delete") }
                }
            }
        }
    }
}

@Composable
private fun CtxButton(label: String, icon: ImageVector?, glyph: String = "", onClick: () -> Unit) {
    Column(
        Modifier.clip(RoundedCornerShape(12.dp)).clickable(onClick = onClick).padding(horizontal = 8.dp, vertical = 6.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        if (icon != null) Icon(icon, label, Modifier.size(20.dp))
        else Text(glyph, fontWeight = FontWeight.Bold, modifier = Modifier.height(20.dp))
        Text(label, style = MaterialTheme.typography.labelSmall)
    }
}

@Composable
private fun ColorDialog(onDismiss: () -> Unit, onPick: (Int) -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Đổi màu") },
        text = {
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                PEN_COLORS.forEachIndexed { i, c ->
                    Box(Modifier.size(32.dp).clip(CircleShape).background(Color(c)).clickable { onPick(i) })
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Đóng") } }
    )
}
