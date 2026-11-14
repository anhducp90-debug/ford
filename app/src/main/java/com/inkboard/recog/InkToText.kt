package com.inkboard.recog

import com.google.android.gms.tasks.Task
import com.google.mlkit.common.model.DownloadConditions
import com.google.mlkit.common.model.RemoteModelManager
import com.google.mlkit.vision.digitalink.recognition.DigitalInkRecognition
import com.google.mlkit.vision.digitalink.recognition.DigitalInkRecognitionModel
import com.google.mlkit.vision.digitalink.recognition.DigitalInkRecognitionModelIdentifier
import com.google.mlkit.vision.digitalink.recognition.DigitalInkRecognizer
import com.google.mlkit.vision.digitalink.recognition.DigitalInkRecognizerOptions
import com.google.mlkit.vision.digitalink.recognition.Ink
import com.google.mlkit.vision.digitalink.recognition.RecognitionContext
import com.google.mlkit.vision.digitalink.recognition.WritingArea
import com.inkboard.model.Stroke
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * Offline handwriting recognition (ML Kit Digital Ink). Models are downloaded
 * once (first launch) and then work without a network connection.
 */
object InkToText {
    val LANGS = listOf("vi" to "Tiếng Việt", "en" to "English")

    private val manager by lazy { RemoteModelManager.getInstance() }
    private val recognizers = HashMap<String, DigitalInkRecognizer>()

    private fun model(lang: String): DigitalInkRecognitionModel {
        val id = when (lang) {
            "vi" -> DigitalInkRecognitionModelIdentifier.VI
            else -> DigitalInkRecognitionModelIdentifier.EN
        }
        return DigitalInkRecognitionModel.builder(id).build()
    }

    suspend fun isDownloaded(lang: String): Boolean = try {
        manager.isModelDownloaded(model(lang)).await()
    } catch (e: Exception) { false }

    suspend fun download(lang: String) {
        manager.download(model(lang), DownloadConditions.Builder().build()).await()
    }

    private fun recognizer(lang: String) = recognizers.getOrPut(lang) {
        DigitalInkRecognition.getClient(DigitalInkRecognizerOptions.builder(model(lang)).build())
    }

    /**
     * Recognize strokes as text. Strokes are grouped into lines by vertical
     * overlap, each line recognized separately, then joined with newlines.
     */
    suspend fun recognize(strokes: List<Stroke>, lang: String): String {
        if (strokes.isEmpty()) return ""
        val lines = groupLines(strokes)
        val out = StringBuilder()
        var pre = ""
        for (line in lines) {
            val ink = Ink.builder()
            var minX = Float.MAX_VALUE; var maxX = -Float.MAX_VALUE
            var minY = Float.MAX_VALUE; var maxY = -Float.MAX_VALUE
            for (s in line.sortedBy { it.bounds.l }) {
                val sb = Ink.Stroke.builder()
                for (i in 0 until s.count) {
                    val x = s.pts[3 * i]; val y = s.pts[3 * i + 1]
                    sb.addPoint(Ink.Point.create(x, y))
                    if (x < minX) minX = x; if (x > maxX) maxX = x
                    if (y < minY) minY = y; if (y > maxY) maxY = y
                }
                ink.addStroke(sb.build())
            }
            val ctx = RecognitionContext.builder()
                .setPreContext(pre.takeLast(20))
                .setWritingArea(WritingArea(maxOf(1f, maxX - minX), maxOf(1f, maxY - minY)))
                .build()
            val result = recognizer(lang).recognize(ink.build(), ctx).await()
            val text = result.candidates.firstOrNull()?.text.orEmpty()
            if (out.isNotEmpty()) out.append('\n')
            out.append(text)
            pre = text
        }
        return out.toString()
    }

    /** Cluster strokes into text lines by vertical overlap of their bounds. */
    fun groupLines(strokes: List<Stroke>): List<List<Stroke>> {
        val sorted = strokes.sortedBy { it.bounds.cy }
        val lines = ArrayList<MutableList<Stroke>>()
        var curT = 0f; var curB = 0f
        for (s in sorted) {
            val b = s.bounds
            val h = b.h
            if (lines.isEmpty() || b.cy > curB + h * 0.1f && b.t > curT + (curB - curT) * 0.6f) {
                lines += mutableListOf(s); curT = b.t; curB = b.b
            } else {
                lines.last() += s; curT = minOf(curT, b.t); curB = maxOf(curB, b.b)
            }
        }
        return lines
    }
}

suspend fun <T> Task<T>.await(): T = suspendCancellableCoroutine { cont ->
    addOnSuccessListener { cont.resume(it) }
    addOnFailureListener { cont.resumeWithException(it) }
    addOnCanceledListener { cont.cancel() }
}
