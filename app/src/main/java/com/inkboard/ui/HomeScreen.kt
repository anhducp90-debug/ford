package com.inkboard.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.outlined.AccountTree
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.DocumentScanner
import androidx.compose.material.icons.outlined.Draw
import androidx.compose.material.icons.outlined.Hub
import androidx.compose.material.icons.outlined.PictureAsPdf
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.inkboard.data.DocMeta
import com.inkboard.data.DocStore
import com.inkboard.model.Document
import com.inkboard.model.Node
import com.inkboard.model.NodeKind
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private const val TRASH = "\u0000trash"

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun HomeScreen(store: DocStore, prefs: Prefs, onOpen: (DocMeta) -> Unit) {
    var refresh by remember { mutableIntStateOf(0) }
    var query by remember { mutableStateOf("") }
    var folder by remember { mutableStateOf("") }
    var showNew by remember { mutableStateOf(false) }
    var showSettings by remember { mutableStateOf(false) }
    var showOnboarding by remember { mutableStateOf(!prefs.onboarded) }
    var renameTarget by remember { mutableStateOf<DocMeta?>(null) }
    var moveTarget by remember { mutableStateOf<DocMeta?>(null) }
    var deleteTarget by remember { mutableStateOf<DocMeta?>(null) }
    val scope = rememberCoroutineScope()

    val all by produceState(initialValue = emptyList<DocMeta>(), refresh) {
        value = withContext(Dispatchers.IO) { store.list() }
    }
    val folders = remember(all) { all.filter { it.trashedAt == 0L && it.folder.isNotEmpty() }.map { it.folder }.distinct().sorted() }
    val shown = all.filter {
        val inScope = when (folder) {
            TRASH -> it.trashedAt != 0L
            "" -> it.trashedAt == 0L
            else -> it.trashedAt == 0L && it.folder == folder
        }
        inScope && (query.isBlank() || it.name.contains(query.trim(), ignoreCase = true) || it.folder.contains(query.trim(), ignoreCase = true))
    }.sortedByDescending { it.modified }

    fun create(name: String, template: (Document) -> Unit) {
        scope.launch {
            val meta = withContext(Dispatchers.IO) {
                val d = Document().also(template)
                store.create(name, if (folder == TRASH) "" else folder, d)
            }
            onOpen(meta)
        }
    }

    Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Box(Modifier.fillMaxSize().safeDrawingPadding()) {
            Column(Modifier.fillMaxSize().padding(horizontal = 16.dp)) {
                Row(Modifier.fillMaxWidth().padding(top = 12.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("InkBoard", style = MaterialTheme.typography.headlineMedium, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                    IconButton(onClick = { showSettings = true }) { Icon(Icons.Outlined.Settings, "Cài đặt") }
                }
                OutlinedTextField(
                    value = query, onValueChange = { query = it },
                    leadingIcon = { Icon(Icons.Outlined.Search, null) },
                    placeholder = { Text("Tìm tài liệu, thư mục…") },
                    singleLine = true, shape = RoundedCornerShape(24.dp),
                    modifier = Modifier.fillMaxWidth().padding(vertical = 8.dp)
                )
                LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    item { FilterChip(selected = folder == "", onClick = { folder = "" }, label = { Text("Tất cả") }) }
                    items(folders) { f -> FilterChip(selected = folder == f, onClick = { folder = f }, label = { Text(f) }) }
                    item { FilterChip(selected = folder == TRASH, onClick = { folder = TRASH }, label = { Text("Thùng rác") }, leadingIcon = { Icon(Icons.Outlined.Delete, null, Modifier.size(16.dp)) }) }
                }
                Spacer(Modifier.height(8.dp))
                if (shown.isEmpty()) {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text(
                            if (folder == TRASH) "Thùng rác trống" else if (query.isNotBlank()) "Không tìm thấy" else "Chạm + để bắt đầu vẽ",
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                } else {
                    LazyVerticalGrid(
                        columns = GridCells.Adaptive(170.dp),
                        contentPadding = PaddingValues(bottom = 96.dp),
                        horizontalArrangement = Arrangement.spacedBy(12.dp),
                        verticalArrangement = Arrangement.spacedBy(12.dp),
                    ) {
                        items(shown, key = { it.id }) { meta ->
                            DocCard(store, meta, inTrash = folder == TRASH, onOpen = { if (folder != TRASH) onOpen(meta) },
                                onAction = { action ->
                                    scope.launch {
                                        withContext(Dispatchers.IO) {
                                            when (action) {
                                                "rename" -> Unit
                                                "move" -> Unit
                                                "duplicate" -> store.duplicate(meta.id)
                                                "trash" -> store.trash(meta.id)
                                                "restore" -> store.restore(meta.id)
                                                "delete" -> Unit
                                            }
                                        }
                                        if (action == "rename") renameTarget = meta
                                        if (action == "delete") deleteTarget = meta
                                        if (action == "move") moveTarget = meta
                                        refresh++
                                    }
                                })
                        }
                    }
                }
            }
            if (folder != TRASH) {
                ExtendedFloatingActionButton(
                    onClick = { showNew = true },
                    icon = { Icon(Icons.Filled.Add, null) }, text = { Text("Mới") },
                    modifier = Modifier.align(Alignment.BottomEnd).padding(20.dp)
                )
            }
        }
    }

    if (showNew) {
        AlertDialog(
            onDismissRequest = { showNew = false },
            title = { Text("Bắt đầu với") },
            text = {
                Column {
                    StartItem(Icons.Outlined.Draw, "Trống", "Không gian vẽ vô hạn") { showNew = false; create("Bản vẽ ${stamp()}") {} }
                    StartItem(Icons.Outlined.Hub, "Mindmap", "Ý tưởng trung tâm, thêm nhánh bằng một chạm") {
                        showNew = false
                        create("Mindmap ${stamp()}") { d ->
                            d.add(Node(d.nextId(), -110f, -32f, 220f, 64f, "", NodeKind.ROUND, 0xFFFFC857.toInt(), 24f, 0L, true).fitText("Ý tưởng trung tâm"))
                            d.clearHistory()
                        }
                    }
                    StartItem(Icons.Outlined.AccountTree, "Sơ đồ", "Flowchart với khối và mũi tên") {
                        showNew = false
                        create("Sơ đồ ${stamp()}") { d ->
                            d.add(Node(d.nextId(), -80f, -30f, 160f, 60f, "", NodeKind.ELLIPSE, 0xFFC8E6C9.toInt(), 20f).fitText("Bắt đầu"))
                            d.clearHistory()
                        }
                    }
                    StartItem(Icons.Outlined.PictureAsPdf, "Chú thích PDF", "Có trong bản cập nhật tới", enabled = false) {}
                    StartItem(Icons.Outlined.DocumentScanner, "Quét tài liệu", "Có trong bản cập nhật tới", enabled = false) {}
                }
            },
            confirmButton = { TextButton(onClick = { showNew = false }) { Text("Đóng") } }
        )
    }

    renameTarget?.let { m ->
        TextInputDialog("Đổi tên", m.name, onDismiss = { renameTarget = null }) { name ->
            renameTarget = null
            scope.launch { withContext(Dispatchers.IO) { store.rename(m.id, name.ifBlank { m.name }) }; refresh++ }
        }
    }
    moveTarget?.let { m ->
        MoveDialog(folders, m.folder, onDismiss = { moveTarget = null }) { f ->
            moveTarget = null
            scope.launch { withContext(Dispatchers.IO) { store.move(m.id, f) }; refresh++ }
        }
    }
    deleteTarget?.let { m ->
        AlertDialog(
            onDismissRequest = { deleteTarget = null },
            title = { Text("Xoá vĩnh viễn?") },
            text = { Text("\"${m.name}\" sẽ bị xoá hẳn và không khôi phục được.") },
            confirmButton = {
                TextButton(onClick = {
                    deleteTarget = null
                    scope.launch { withContext(Dispatchers.IO) { store.deleteForever(m.id) }; refresh++ }
                }) { Text("Xoá vĩnh viễn", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { deleteTarget = null }) { Text("Huỷ") } }
        )
    }
    if (showSettings) SettingsDialog(prefs) { showSettings = false }
    if (showOnboarding) OnboardingDialog(prefs) { prefs.onboarded = true; showOnboarding = false }
}

@Composable
private fun StartItem(icon: ImageVector, title: String, sub: String, enabled: Boolean = true, onClick: () -> Unit) {
    val alpha = if (enabled) 1f else 0.45f
    ListItem(
        headlineContent = { Text(title, color = MaterialTheme.colorScheme.onSurface.copy(alpha = alpha)) },
        supportingContent = { Text(sub, color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = alpha)) },
        leadingContent = { Icon(icon, null, tint = MaterialTheme.colorScheme.primary.copy(alpha = alpha)) },
        modifier = Modifier.clip(RoundedCornerShape(12.dp)).combinedClickable(enabled = enabled, onClick = onClick)
    )
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun DocCard(store: DocStore, meta: DocMeta, inTrash: Boolean, onOpen: () -> Unit, onAction: (String) -> Unit) {
    var menu by remember { mutableStateOf(false) }
    val thumb by produceState<android.graphics.Bitmap?>(null, meta.id, meta.modified) {
        value = withContext(Dispatchers.IO) {
            store.thumbnailFile(meta.id).takeIf { it.exists() }?.let { BitmapFactory.decodeFile(it.absolutePath) }
        }
    }
    Box {
        Card(
            shape = RoundedCornerShape(16.dp),
            modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp))
                .combinedClickable(onClick = onOpen, onLongClick = { menu = true })
        ) {
            Box(Modifier.fillMaxWidth().aspectRatio(4f / 3f).background(Color.White), contentAlignment = Alignment.Center) {
                thumb?.let { Image(it.asImageBitmap(), null, contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize()) }
            }
            Column(Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) {
                Text(meta.name, maxLines = 1, overflow = TextOverflow.Ellipsis, fontWeight = FontWeight.Medium)
                Text(
                    (if (meta.folder.isNotEmpty()) meta.folder + " · " else "") + fmt(meta.modified),
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1
                )
            }
        }
        DropdownMenu(expanded = menu, onDismissRequest = { menu = false }) {
            val items = if (inTrash) listOf("restore" to "Khôi phục", "delete" to "Xoá vĩnh viễn")
            else listOf("rename" to "Đổi tên", "move" to "Chuyển thư mục", "duplicate" to "Nhân đôi", "trash" to "Chuyển vào thùng rác")
            for ((k, label) in items) DropdownMenuItem(text = { Text(label) }, onClick = { menu = false; onAction(k) })
        }
    }
}

@Composable
private fun MoveDialog(folders: List<String>, current: String, onDismiss: () -> Unit, onPick: (String) -> Unit) {
    var newName by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Chuyển tới thư mục") },
        text = {
            Column {
                ListItem(headlineContent = { Text("(Không thư mục)") }, modifier = Modifier.combinedClickableCompat { onPick("") })
                for (f in folders) ListItem(
                    headlineContent = { Text(f + if (f == current) "  ✓" else "") },
                    modifier = Modifier.combinedClickableCompat { onPick(f) }
                )
                OutlinedTextField(newName, { newName = it }, label = { Text("Thư mục mới") }, singleLine = true, modifier = Modifier.fillMaxWidth())
            }
        },
        confirmButton = { TextButton(onClick = { if (newName.isNotBlank()) onPick(newName.trim()) else onDismiss() }) { Text("OK") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Huỷ") } }
    )
}

@OptIn(ExperimentalFoundationApi::class)
private fun Modifier.combinedClickableCompat(onClick: () -> Unit) = this.combinedClickable(onClick = onClick)

@Composable
fun TextInputDialog(title: String, initial: String, singleLine: Boolean = true, onDismiss: () -> Unit, onOk: (String) -> Unit) {
    var v by remember { mutableStateOf(initial) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = {
            OutlinedTextField(v, { v = it }, singleLine = singleLine, minLines = if (singleLine) 1 else 3, modifier = Modifier.fillMaxWidth())
        },
        confirmButton = { TextButton(onClick = { onOk(v) }) { Text("OK") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Huỷ") } }
    )
}

@Composable
fun OnboardingDialog(prefs: Prefs, onDone: () -> Unit) {
    var page by remember { mutableIntStateOf(0) }
    val pages = listOf(
        "Vẽ bằng bút, điều hướng bằng tay" to "S Pen để vẽ. Một ngón kéo để di chuyển, hai ngón chụm để phóng to. Không gian vô hạn theo mọi hướng.",
        "Giữ bút để thành hình đẹp" to "Vẽ hình tròn, chữ nhật, tam giác, đường thẳng… rồi giữ bút ở cuối nét khoảng nửa giây. Giữ thêm một ngón tay trên màn hình để được hình tròn/vuông chuẩn hoặc đường thẳng.",
        "Khoanh vùng để chọn, kéo để di chuyển" to "Dùng công cụ Chọn, khoanh quanh nội dung rồi kéo đi. Chạm 2 ngón để hoàn tác, 3 ngón để làm lại. Khoanh chữ viết tay rồi chạm \"Chữ\" để chuyển thành văn bản."
    )
    val scope = rememberCoroutineScope()
    var status by remember { mutableStateOf("") }
    AlertDialog(
        onDismissRequest = {},
        title = { Text(if (page < pages.size) pages[page].first else "Gói nhận dạng chữ viết tay") },
        text = {
            Column {
                if (page < pages.size) Text(pages[page].second)
                else {
                    Text("Tải một lần gói nhận dạng Tiếng Việt và English (cần mạng, khoảng 20–40 MB). Sau đó dùng offline hoàn toàn. Bạn có thể tải sau trong Cài đặt.")
                    if (status.isNotEmpty()) { Spacer(Modifier.height(8.dp)); Text(status, color = MaterialTheme.colorScheme.primary) }
                }
            }
        },
        confirmButton = {
            if (page < pages.size) TextButton(onClick = { page++ }) { Text("Tiếp") }
            else TextButton(onClick = {
                status = "Đang tải…"
                scope.launch {
                    status = try {
                        for ((code, _) in com.inkboard.recog.InkToText.LANGS) com.inkboard.recog.InkToText.download(code)
                        "Đã tải xong ✓"
                    } catch (e: Exception) { "Chưa tải được: ${e.message ?: "lỗi mạng"}" }
                }
            }) { Text("Tải ngay") }
        },
        dismissButton = {
            TextButton(onClick = onDone) { Text(if (page < pages.size) "Bỏ qua" else "Xong") }
        }
    )
}

@Composable
fun SettingsDialog(prefs: Prefs, onDismiss: () -> Unit) {
    var finger by remember { mutableStateOf(prefs.fingerDraws) }
    var auto by remember { mutableStateOf(prefs.autoShape) }
    var lang by remember { mutableStateOf(prefs.recogLang) }
    var status by remember { mutableStateOf("") }
    val scope = rememberCoroutineScope()
    LaunchedEffect(Unit) {
        val parts = ArrayList<String>()
        for ((code, name) in com.inkboard.recog.InkToText.LANGS) {
            parts += name + if (com.inkboard.recog.InkToText.isDownloaded(code)) " ✓" else " (chưa tải)"
        }
        status = parts.joinToString(" · ")
    }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Cài đặt") },
        text = {
            Column {
                ToggleRow("Ngón tay cũng vẽ được", "Dành cho máy không có bút", finger) { finger = it; prefs.fingerDraws = it }
                ToggleRow("Giữ bút để thành hình", "Tự làm đẹp hình khi giữ bút cuối nét", auto) { auto = it; prefs.autoShape = it }
                Spacer(Modifier.height(8.dp))
                Text("Ngôn ngữ nhận dạng chữ", fontWeight = FontWeight.Medium)
                Row {
                    for ((code, name) in com.inkboard.recog.InkToText.LANGS) {
                        FilterChip(selected = lang == code, onClick = { lang = code; prefs.recogLang = code }, label = { Text(name) }, modifier = Modifier.padding(end = 8.dp))
                    }
                }
                Text(status, style = MaterialTheme.typography.bodySmall)
                TextButton(onClick = {
                    status = "Đang tải…"
                    scope.launch {
                        status = try {
                            for ((code, _) in com.inkboard.recog.InkToText.LANGS) com.inkboard.recog.InkToText.download(code)
                            "Đã tải xong ✓"
                        } catch (e: Exception) { "Chưa tải được: ${e.message ?: "lỗi mạng"}" }
                    }
                }) { Text("Tải / cập nhật gói nhận dạng") }
                Spacer(Modifier.height(8.dp))
                val ctx = androidx.compose.ui.platform.LocalContext.current
                val ver = remember { runCatching { ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName }.getOrNull() ?: "" }
                Text("InkBoard $ver · Không tài khoản, không quảng cáo. Bản vẽ chỉ lưu trên máy (và bản sao lưu Google của chính bạn nếu bật). Nhận dạng chữ dùng ML Kit của Google, chạy trên máy.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Xong") } }
    )
}

@Composable
private fun ToggleRow(title: String, sub: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
        Column(Modifier.weight(1f)) {
            Text(title)
            Text(sub, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Spacer(Modifier.width(8.dp))
        androidx.compose.material3.Switch(checked = checked, onCheckedChange = onChange)
    }
}

private fun stamp(): String = SimpleDateFormat("dd/MM HH:mm", Locale.getDefault()).format(Date())
private fun fmt(t: Long): String = SimpleDateFormat("dd/MM/yyyy HH:mm", Locale.getDefault()).format(Date(t))
