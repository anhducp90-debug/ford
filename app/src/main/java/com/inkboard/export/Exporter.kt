package com.inkboard.export

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.pdf.PdfDocument
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import androidx.core.content.FileProvider
import com.inkboard.canvas.Renderer
import com.inkboard.model.Affine
import com.inkboard.model.Box
import com.inkboard.model.Document
import java.io.File
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt

enum class ExportFormat(val ext: String, val mime: String) {
    PNG("png", "image/png"),
    PDF("pdf", "application/pdf"),
}

/** Renders a document region to PNG or vector PDF. Runs off the UI thread. */
object Exporter {
    private const val MAX_PIXELS = 40_000_000 // ~ 6300 x 6300

    fun export(context: Context, doc: Document, region: Box, format: ExportFormat, name: String): File {
        val dir = File(context.cacheDir, "exports").apply { mkdirs() }
        val safe = name.replace(Regex("[\\\\/:*?\"<>|]"), "_").ifBlank { "InkBoard" }
        val file = File(dir, "$safe.${format.ext}")
        val pad = 24f
        val b = Box(region.l - pad, region.t - pad, region.r + pad, region.b + pad)
        when (format) {
            ExportFormat.PNG -> {
                var s = 2f
                val px = b.w * b.h * s * s
                if (px > MAX_PIXELS) s = sqrt(MAX_PIXELS / (b.w * b.h))
                val w = max(1, (b.w * s).toInt()); val h = max(1, (b.h * s).toInt())
                val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
                val c = Canvas(bmp)
                c.drawColor(0xFFFFFFFF.toInt())
                Renderer().drawAll(c, doc, Affine(s, -b.l * s, -b.t * s))
                file.outputStream().buffered().use { bmp.compress(Bitmap.CompressFormat.PNG, 100, it) }
                bmp.recycle()
            }
            ExportFormat.PDF -> {
                // One vector page sized to the content (max 14400pt per side, PDF limit).
                val s = min(1f, 14400f / max(b.w, b.h))
                val pdf = PdfDocument()
                val page = pdf.startPage(PdfDocument.PageInfo.Builder(max(1, (b.w * s).toInt()), max(1, (b.h * s).toInt()), 1).create())
                Renderer().drawAll(page.canvas, doc, Affine(s, -b.l * s, -b.t * s))
                pdf.finishPage(page)
                file.outputStream().buffered().use { pdf.writeTo(it) }
                pdf.close()
            }
        }
        return file
    }

    fun share(context: Context, file: File, format: ExportFormat) {
        val uri = FileProvider.getUriForFile(context, context.packageName + ".files", file)
        val send = Intent(Intent.ACTION_SEND).apply {
            type = format.mime
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        context.startActivity(Intent.createChooser(send, "Chia sẻ").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    /** Save into the public Downloads/InkBoard folder. Returns the content Uri. */
    fun saveToDownloads(context: Context, file: File, format: ExportFormat): Uri? {
        val values = ContentValues().apply {
            put(MediaStore.MediaColumns.DISPLAY_NAME, file.name)
            put(MediaStore.MediaColumns.MIME_TYPE, format.mime)
            put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/InkBoard")
        }
        val resolver = context.contentResolver
        val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: return null
        resolver.openOutputStream(uri)?.use { out -> file.inputStream().use { it.copyTo(out) } }
        return uri
    }
}
