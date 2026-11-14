package com.inkboard.export

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.pdf.PdfRenderer
import android.net.Uri
import java.io.ByteArrayOutputStream

/** Render PDF pages to JPEG pictures for annotation (on-device, offline). */
object PdfImport {
    const val MAX_PAGES = 200

    fun render(context: Context, uri: Uri, targetWidth: Int = 1600, onProgress: (Int, Int) -> Unit = { _, _ -> }): List<Triple<ByteArray, Int, Int>> {
        val pfd = context.contentResolver.openFileDescriptor(uri, "r") ?: return emptyList()
        pfd.use { fd ->
            PdfRenderer(fd).use { r ->
                val n = minOf(r.pageCount, MAX_PAGES)
                val out = ArrayList<Triple<ByteArray, Int, Int>>(n)
                for (i in 0 until n) {
                    r.openPage(i).use { page ->
                        val w = targetWidth
                        val h = (targetWidth.toFloat() * page.height / page.width.coerceAtLeast(1)).toInt().coerceIn(1, targetWidth * 4)
                        val bmp = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
                        bmp.eraseColor(Color.WHITE)
                        page.render(bmp, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                        val bo = ByteArrayOutputStream()
                        bmp.compress(Bitmap.CompressFormat.JPEG, 85, bo)
                        bmp.recycle()
                        out += Triple(bo.toByteArray(), w, h)
                    }
                    onProgress(i + 1, n)
                }
                return out
            }
        }
    }
}
