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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.AccountTree
import androidx.compose.material.icons.outlined.Category
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material.icons.outlined.Edit
import androidx.compose.material.icons.outlined.Gesture
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.TableChart
import androidx.compose.material.icons.outlined.TextFields
import androidx.compose.material.icons.outlined.ViewAgenda
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.inkboard.model.Component
import com.inkboard.model.ComponentKind
import com.inkboard.model.Components
import com.inkboard.model.Document
import kotlin.math.roundToInt

/** Side panel listing everything on the board with its position; tap to jump there. */
@Composable
fun ComponentsPanel(doc: Document, tick: Int, onDismiss: () -> Unit, onPick: (Component) -> Unit) {
    val all = remember(tick) { Components.collect(doc) }
    var filter by remember { mutableStateOf<ComponentKind?>(null) }
    val shown = if (filter == null) all else all.filter { it.kind == filter }
    val kinds = all.map { it.kind }.distinct()

    Box(Modifier.fillMaxSize().background(Color(0x33000000)).clickable(onClick = onDismiss)) {
        Surface(
            Modifier.align(Alignment.CenterEnd).fillMaxHeight().width(340.dp).clickable(enabled = false) {},
            color = MaterialTheme.colorScheme.surface, tonalElevation = 3.dp,
        ) {
            Column(Modifier.safeDrawingPadding().padding(12.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Thành phần (${all.size})", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                    IconButton(onClick = onDismiss) { Icon(Icons.Outlined.Close, "Đóng") }
                }
                if (kinds.size > 1) {
                    Row(Modifier.padding(bottom = 8.dp)) {
                        FilterChip(selected = filter == null, onClick = { filter = null }, label = { Text("Tất cả") }, modifier = Modifier.padding(end = 6.dp))
                        androidx.compose.foundation.lazy.LazyRow {
                            items(kinds) { k ->
                                FilterChip(selected = filter == k, onClick = { filter = k }, label = { Text(kindName(k)) }, modifier = Modifier.padding(end = 6.dp))
                            }
                        }
                    }
                }
                if (shown.isEmpty()) {
                    Text("Bảng vẽ đang trống", color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(16.dp))
                }
                LazyColumn {
                    items(shown) { c ->
                        Row(
                            Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).clickable { onPick(c) }.padding(horizontal = 8.dp, vertical = 10.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Icon(kindIcon(c.kind), null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(22.dp))
                            Spacer(Modifier.width(12.dp))
                            Column(Modifier.weight(1f)) {
                                Text(c.label, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                Text(
                                    "X ${c.box.l.roundToInt()}  Y ${(-c.box.t).roundToInt()}  ·  ${c.box.w.roundToInt()} × ${c.box.h.roundToInt()}" +
                                        if (c.kind == ComponentKind.MINDMAP) "  ·  ${c.count} ý" else "",
                                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

private fun kindName(k: ComponentKind) = when (k) {
    ComponentKind.MINDMAP -> "Mindmap"
    ComponentKind.NODE -> "Khối"
    ComponentKind.TEXT -> "Chữ"
    ComponentKind.STICKY -> "Ghi chú"
    ComponentKind.TABLE -> "Bảng"
    ComponentKind.IMAGE -> "Ảnh"
    ComponentKind.SHAPE -> "Hình"
    ComponentKind.SKETCH -> "Nét vẽ"
}

private fun kindIcon(k: ComponentKind) = when (k) {
    ComponentKind.MINDMAP -> Icons.Outlined.AccountTree
    ComponentKind.NODE -> Icons.Outlined.ViewAgenda
    ComponentKind.TEXT -> Icons.Outlined.TextFields
    ComponentKind.STICKY -> Icons.Outlined.Edit
    ComponentKind.TABLE -> Icons.Outlined.TableChart
    ComponentKind.IMAGE -> Icons.Outlined.Image
    ComponentKind.SHAPE -> Icons.Outlined.Category
    ComponentKind.SKETCH -> Icons.Outlined.Gesture
}
