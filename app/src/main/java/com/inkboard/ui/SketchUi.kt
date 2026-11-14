package com.inkboard.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.unit.dp
import com.inkboard.model.SymmetryMode
import java.io.File
import kotlin.math.roundToInt

// ------------------------------------------------------------------ colour

/** HSV colour picker: saturation/value square + hue bar + hex, with recent colours. */
@Composable
fun ColorPickerDialog(initial: Int, recent: List<Int>, onDismiss: () -> Unit, onPick: (Int) -> Unit) {
    val hsv = remember { FloatArray(3).also { android.graphics.Color.colorToHSV(initial, it) } }
    var h by remember { mutableFloatStateOf(hsv[0]) }
    var sat by remember { mutableFloatStateOf(hsv[1]) }
    var v by remember { mutableFloatStateOf(hsv[2]) }
    val current = android.graphics.Color.HSVToColor(floatArrayOf(h, sat, v))
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Chọn màu") },
        text = {
            Column {
                // Saturation / value square
                Canvas(
                    Modifier.fillMaxWidth().height(200.dp).clip(RoundedCornerShape(8.dp))
                        .pointerInput(Unit) {
                            fun upd(o: Offset) {
                                sat = (o.x / size.width).coerceIn(0f, 1f); v = 1f - (o.y / size.height).coerceIn(0f, 1f)
                            }
                            detectDragGestures(onDragStart = { upd(it) }) { ch, _ -> upd(ch.position) }
                        }
                        .pointerInput(Unit) {
                            detectTapGestures { o -> sat = (o.x / size.width).coerceIn(0f, 1f); v = 1f - (o.y / size.height).coerceIn(0f, 1f) }
                        }
                ) {
                    val hueColor = Color(android.graphics.Color.HSVToColor(floatArrayOf(h, 1f, 1f)))
                    drawRect(Brush.horizontalGradient(listOf(Color.White, hueColor)))
                    drawRect(Brush.verticalGradient(listOf(Color.Transparent, Color.Black)))
                    val c = Offset(sat * size.width, (1f - v) * size.height)
                    drawCircle(Color.White, 9.dp.toPx(), c, style = Stroke(3.dp.toPx()))
                    drawCircle(Color.Black.copy(alpha = 0.4f), 11.dp.toPx(), c, style = Stroke(1.dp.toPx()))
                }
                Spacer(Modifier.height(12.dp))
                // Hue bar
                val hues = (0..6).map { Color(android.graphics.Color.HSVToColor(floatArrayOf(it * 60f, 1f, 1f))) }
                Canvas(
                    Modifier.fillMaxWidth().height(28.dp).clip(RoundedCornerShape(14.dp))
                        .pointerInput(Unit) {
                            detectDragGestures(onDragStart = { h = (it.x / size.width * 360f).coerceIn(0f, 359.9f) }) { ch, _ ->
                                h = (ch.position.x / size.width * 360f).coerceIn(0f, 359.9f)
                            }
                        }
                        .pointerInput(Unit) { detectTapGestures { o -> h = (o.x / size.width * 360f).coerceIn(0f, 359.9f) } }
                ) {
                    drawRect(Brush.horizontalGradient(hues))
                    val x = h / 360f * size.width
                    drawCircle(Color.White, size.height / 2 - 2.dp.toPx(), Offset(x, size.height / 2), style = Stroke(3.dp.toPx()))
                }
                Spacer(Modifier.height(12.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(36.dp).clip(CircleShape).background(Color(current)).border(1.dp, Color.Gray, CircleShape))
                    Spacer(Modifier.width(12.dp))
                    Text("#%06X".format(current and 0xFFFFFF), fontWeight = FontWeight.Medium)
                }
                if (recent.isNotEmpty()) {
                    Spacer(Modifier.height(12.dp))
                    Text("Gần đây", style = MaterialTheme.typography.labelMedium)
                    Spacer(Modifier.height(6.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        for (c in recent.take(8)) Box(Modifier.size(28.dp).clip(CircleShape).background(Color(c)).clickable { onPick(c) })
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = { onPick(current) }) { Text("Chọn") } },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Huỷ") } }
    )
}

// ------------------------------------------------------------- drawing aids

@Composable
fun GuidesDialog(
    symmetry: SymmetryMode, radialN: Int, perspective: Int, assist: Boolean, hasRef: Boolean,
    onSymmetry: (SymmetryMode, Int) -> Unit, onPerspective: (Int) -> Unit, onAssist: (Boolean) -> Unit,
    onReference: () -> Unit, onDismiss: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Trợ vẽ") },
        text = {
            Column {
                Text("Đối xứng", fontWeight = FontWeight.Medium)
                Text("Kéo chấm hồng để dời tâm.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                ChipRow(listOf("Tắt" to SymmetryMode.OFF, "Dọc" to SymmetryMode.VERTICAL, "Ngang" to SymmetryMode.HORIZONTAL, "4 phía" to SymmetryMode.QUAD), symmetry) { onSymmetry(it, radialN) }
                Row {
                    for (n in listOf(4, 6, 8, 12)) {
                        FilterChip(selected = symmetry == SymmetryMode.RADIAL && radialN == n, onClick = { onSymmetry(SymmetryMode.RADIAL, n) },
                            label = { Text("Toả $n") }, modifier = Modifier.padding(end = 6.dp))
                    }
                }
                HorizontalDivider(Modifier.padding(vertical = 8.dp))
                Text("Phối cảnh", fontWeight = FontWeight.Medium)
                Text("Kéo điểm tụ để chỉnh. Nét vẽ tự thẳng theo đường phối cảnh, ngang hoặc dọc.", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                ChipRow(listOf("Tắt" to 0, "1 điểm tụ" to 1, "2 điểm tụ" to 2), perspective, onPerspective)
                if (perspective > 0) Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                    Text("Bắt nét theo phối cảnh", modifier = Modifier.weight(1f))
                    Switch(checked = assist, onCheckedChange = onAssist)
                }
                HorizontalDivider(Modifier.padding(vertical = 8.dp))
                TextButton(onClick = onReference) { Text(if (hasRef) "Đổi ảnh tham khảo…" else "Ảnh tham khảo…") }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Xong") } }
    )
}

@Composable
private fun <T> ChipRow(items: List<Pair<String, T>>, selected: T, onPick: (T) -> Unit) {
    Row {
        for ((label, v) in items) FilterChip(selected = selected == v, onClick = { onPick(v) }, label = { Text(label) }, modifier = Modifier.padding(end = 6.dp))
    }
}

// --------------------------------------------------------- reference image

/**
 * Floating reference picture pinned to the screen (not part of the drawing,
 * never exported). Drag the top bar to move, the corner to resize, long-press
 * the picture to pick a colour from it.
 */
@Composable
fun ReferenceWindow(file: File, version: Int, onClose: () -> Unit, onColor: (Int) -> Unit) {
    val bmp = remember(version) { if (file.exists()) BitmapFactory.decodeFile(file.absolutePath) else null } ?: return
    val density = LocalDensity.current
    var ox by remember { mutableFloatStateOf(with(density) { 24.dp.toPx() }) }
    var oy by remember { mutableFloatStateOf(with(density) { 140.dp.toPx() }) }
    var w by remember { mutableFloatStateOf(with(density) { 220.dp.toPx() }) }
    val hpx = w * bmp.height / bmp.width
    Surface(
        Modifier.offset { IntOffset(ox.roundToInt(), oy.roundToInt()) }.shadow(10.dp, RoundedCornerShape(12.dp)),
        shape = RoundedCornerShape(12.dp), color = MaterialTheme.colorScheme.surface,
    ) {
        Column {
            Row(
                Modifier.width(with(density) { w.toDp() }).height(28.dp)
                    .pointerInput(Unit) { detectDragGestures { ch, d -> ch.consume(); ox += d.x; oy += d.y } }
                    .padding(start = 10.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text("Tham khảo", style = MaterialTheme.typography.labelMedium, modifier = Modifier.weight(1f))
                Icon(Icons.Outlined.Close, "Đóng ảnh tham khảo", Modifier.size(28.dp).clickable(onClick = onClose).padding(5.dp))
            }
            Box(Modifier.size(with(density) { w.toDp() }, with(density) { hpx.toDp() })) {
                Image(
                    bmp.asImageBitmap(), "Ảnh tham khảo", contentScale = ContentScale.FillBounds,
                    modifier = Modifier.fillMaxSize().pointerInput(bmp) {
                        detectTapGestures(onLongPress = { o ->
                            val px = (o.x / size.width * bmp.width).toInt().coerceIn(0, bmp.width - 1)
                            val py = (o.y / size.height * bmp.height).toInt().coerceIn(0, bmp.height - 1)
                            onColor(bmp.getPixel(px, py) or 0xFF000000.toInt())
                        })
                    }
                )
                // Resize handle
                Box(
                    Modifier.align(Alignment.BottomEnd).size(26.dp).padding(4.dp).clip(CircleShape)
                        .background(MaterialTheme.colorScheme.primary.copy(alpha = 0.8f))
                        .pointerInput(Unit) {
                            detectDragGestures { ch, d -> ch.consume(); w = (w + d.x).coerceIn(with(density) { 120.dp.toPx() }, with(density) { 700.dp.toPx() }) }
                        }
                )
            }
        }
    }
}

/** Copy a picked picture into app storage (downscaled) for the reference window. */
fun saveReferenceImage(context: android.content.Context, uri: android.net.Uri): File? {
    val bytes = context.contentResolver.openInputStream(uri)?.use { it.readBytes() } ?: return null
    val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)
    var sample = 1
    while (maxOf(opts.outWidth, opts.outHeight) / sample > 2048) sample *= 2
    val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample }) ?: return null
    val f = File(context.filesDir, "reference.jpg")
    f.outputStream().use { bmp.compress(Bitmap.CompressFormat.JPEG, 90, it) }
    return f
}
