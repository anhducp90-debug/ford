package com.inkboard.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.inkboard.model.Document
import com.inkboard.model.Search

/** Find text on the board — typed, labels, notes, cells, and recognised handwriting. */
@Composable
fun SearchPanel(doc: Document, tick: Int, indexing: Boolean, onDismiss: () -> Unit, onPick: (Search.Hit) -> Unit) {
    var q by remember { mutableStateOf("") }
    val hits = remember(q, tick) { Search.find(doc, q) }
    Box(Modifier.fillMaxSize().background(Color(0x33000000)).clickable(onClick = onDismiss)) {
        Surface(Modifier.align(Alignment.CenterEnd).fillMaxHeight().width(340.dp).clickable(enabled = false) {}, tonalElevation = 3.dp) {
            Column(Modifier.safeDrawingPadding().padding(12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Tìm trên bảng vẽ", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                    IconButton(onClick = onDismiss) { Icon(Icons.Outlined.Close, "Đóng tìm kiếm") }
                }
                OutlinedTextField(q, { q = it }, leadingIcon = { Icon(Icons.Outlined.Search, null) }, singleLine = true,
                    placeholder = { Text("Chữ gõ hoặc viết tay…") }, modifier = Modifier.fillMaxWidth())
                Text(
                    if (indexing) "Chữ viết tay được nhận dạng ngầm trên máy để tìm kiếm."
                    else "Bật \"Tìm trong chữ viết tay\" trong Cài đặt và tải gói nhận dạng để tìm cả chữ viết tay.",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(vertical = 6.dp)
                )
                if (q.isNotBlank() && hits.isEmpty()) Text("Không tìm thấy", modifier = Modifier.padding(12.dp))
                LazyColumn {
                    items(hits) { h ->
                        Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).clickable { onPick(h) }.padding(horizontal = 8.dp, vertical = 10.dp)) {
                            Text(h.text.replace('\n', ' '), maxLines = 2, overflow = TextOverflow.Ellipsis)
                            Spacer(Modifier.padding(1.dp))
                            Text(
                                when (h.kind) {
                                    Search.Kind.INK -> "Chữ viết tay"; Search.Kind.TEXT -> "Chữ"; Search.Kind.LABEL -> "Chữ trong hình"
                                    Search.Kind.BLOCK -> "Khối"; Search.Kind.NOTE -> "Ghi chú"; Search.Kind.CELL -> "Ô bảng"; Search.Kind.CONNECTOR -> "Nhãn đường nối"
                                },
                                style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                    }
                }
            }
        }
    }
}
