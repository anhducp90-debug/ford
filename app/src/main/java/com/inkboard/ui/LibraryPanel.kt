package com.inkboard.ui

import android.graphics.Bitmap
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.inkboard.canvas.Renderer
import com.inkboard.data.LibraryStore
import com.inkboard.model.Affine
import com.inkboard.model.Document
import com.inkboard.model.ShapeLibrary
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.min

private val previewCache = HashMap<String, Bitmap>()

/** Render a small preview of some elements (cached by key). */
private fun preview(key: String, size: Int, build: (Document) -> Unit): Bitmap = synchronized(previewCache) {
    previewCache.getOrPut(key) {
        val d = Document().also(build)
        val bmp = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val b = d.contentBounds()
        if (!b.isEmpty) {
            val s = min((size - 12f) / maxOf(b.w, 1f), (size - 12f) / maxOf(b.h, 1f))
            Renderer().drawAll(android.graphics.Canvas(bmp), d, Affine(s, size / 2f - b.cx * s, size / 2f - b.cy * s))
        }
        bmp
    }
}

/** Shape library (built-in, searchable) + "My library" (saved groups). */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun LibraryPanel(
    store: LibraryStore, color: Int, version: Int,
    onDismiss: () -> Unit, onBuiltIn: (ShapeLibrary.Entry) -> Unit, onMine: (LibraryStore.Item) -> Unit, onDeleteMine: (LibraryStore.Item) -> Unit,
) {
    var tab by remember { mutableIntStateOf(0) }
    var query by remember { mutableStateOf("") }
    var cat by remember { mutableStateOf<ShapeLibrary.Category?>(null) }
    val mine by produceState(emptyList<LibraryStore.Item>(), version) { value = withContext(Dispatchers.IO) { store.list() } }

    Box(Modifier.fillMaxSize().background(Color(0x33000000)).clickable(onClick = onDismiss)) {
        Surface(Modifier.align(Alignment.CenterEnd).fillMaxHeight().width(360.dp).clickable(enabled = false) {}, tonalElevation = 3.dp) {
            Column(Modifier.safeDrawingPadding().padding(12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Thư viện", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                    IconButton(onClick = onDismiss) { Icon(Icons.Outlined.Close, "Đóng thư viện") }
                }
                TabRow(selectedTabIndex = tab) {
                    Tab(selected = tab == 0, onClick = { tab = 0 }, text = { Text("Hình có sẵn") })
                    Tab(selected = tab == 1, onClick = { tab = 1 }, text = { Text("Của tôi (${mine.size})") })
                }
                if (tab == 0) {
                    OutlinedTextField(query, { query = it }, leadingIcon = { Icon(Icons.Outlined.Search, null) },
                        placeholder = { Text("Tìm hình: sao, mũi tên, quyết định…") }, singleLine = true,
                        modifier = Modifier.fillMaxWidth().padding(top = 8.dp))
                    LazyRow(Modifier.padding(vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        item { FilterChip(selected = cat == null, onClick = { cat = null }, label = { Text("Tất cả") }) }
                        items(ShapeLibrary.Category.entries) { c -> FilterChip(selected = cat == c, onClick = { cat = c }, label = { Text(c.label) }) }
                    }
                    val shown = ShapeLibrary.entries.filter { (cat == null || it.category == cat) && it.matches(query) }
                    LazyVerticalGrid(GridCells.Adaptive(96.dp), verticalArrangement = Arrangement.spacedBy(6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(shown, key = { it.name }) { e ->
                            Column(Modifier.clip(RoundedCornerShape(12.dp)).clickable { onBuiltIn(e) }.padding(4.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                                val bmp = remember(e.name, color) { preview("b:${e.name}:$color", 160) { d -> d.add(e.build(d, 0f, 0f, 60f, color, 3f)) } }
                                Image(bmp.asImageBitmap(), e.name, Modifier.fillMaxWidth().aspectRatio(1f).background(Color.White, RoundedCornerShape(8.dp)))
                                Text(e.name, style = MaterialTheme.typography.labelSmall, maxLines = 2, textAlign = TextAlign.Center, overflow = TextOverflow.Ellipsis)
                            }
                        }
                    }
                } else {
                    if (mine.isEmpty()) Text("Chọn nội dung trên bản vẽ rồi chạm \"Lưu mẫu\" để dùng lại ở bất kỳ tài liệu nào.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(16.dp))
                    else Text("Nhấn giữ để xoá mẫu.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 6.dp))
                    LazyVerticalGrid(GridCells.Adaptive(120.dp), verticalArrangement = Arrangement.spacedBy(6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        items(mine, key = { it.id }) { item ->
                            val bmp by produceState<Bitmap?>(null, item.id) {
                                value = withContext(Dispatchers.IO) { runCatching { val d = store.load(item); preview("m:${item.id}", 200) { t -> t.elements.addAll(d.elements); t.images.putAll(d.images) } }.getOrNull() }
                            }
                            Column(Modifier.clip(RoundedCornerShape(12.dp)).combinedClickable(onClick = { onMine(item) }, onLongClick = { onDeleteMine(item) }).padding(4.dp),
                                horizontalAlignment = Alignment.CenterHorizontally) {
                                Box(Modifier.fillMaxWidth().aspectRatio(1f).background(Color.White, RoundedCornerShape(8.dp))) {
                                    bmp?.let { Image(it.asImageBitmap(), item.name, Modifier.fillMaxSize()) }
                                }
                                Text(item.name, style = MaterialTheme.typography.labelSmall, maxLines = 1, overflow = TextOverflow.Ellipsis)
                            }
                        }
                    }
                }
            }
        }
    }
}
