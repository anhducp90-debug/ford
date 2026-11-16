package com.inkboard.recog

import com.inkboard.model.Document
import com.inkboard.model.Search
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Recognises handwriting in the background (on-device ML Kit) so it can be
 * searched. Runs a few seconds after editing stops, a bounded batch at a time,
 * and only when the recognition model is already installed.
 */
class InkIndexer(
    private val scope: CoroutineScope,
    private val doc: Document,
    private val lang: () -> String,
    private val enabled: () -> Boolean,
    private val onUpdate: () -> Unit,
) {
    private var job: Job? = null

    fun schedule(delayMs: Long = 4000) {
        job?.cancel()
        if (!enabled()) return
        job = scope.launch(Dispatchers.Main) {
            delay(delayMs)
            runCatching { pass() }
        }
    }

    fun cancel() { job?.cancel() }

    private suspend fun pass() {
        val l = lang()
        if (!InkToText.isDownloaded(l)) return
        val snap = doc.snapshot()
        val clusters = withContext(Dispatchers.Default) { Search.inkClusters(snap) }
        val keys = clusters.map { it.first }.toHashSet()
        val stale = doc.inkText.keys.filter { it !in keys }
        val missing = clusters.filter { it.first !in doc.inkText }.take(BATCH)
        var changed = stale.isNotEmpty()
        stale.forEach { doc.inkText.remove(it) }
        for ((key, strokes) in missing) {
            val text = runCatching { InkToText.recognize(strokes, l) }.getOrNull() ?: continue
            doc.inkText[key] = text
            changed = true
        }
        if (changed) { doc.markDirty(); onUpdate() }
        if (clusters.count { it.first !in doc.inkText } > 0) schedule(1500) // continue with the next batch
    }

    companion object { const val BATCH = 25 }
}
