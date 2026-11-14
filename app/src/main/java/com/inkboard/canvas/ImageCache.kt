package com.inkboard.canvas

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.LruCache
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executors

/**
 * Level-of-detail picture cache: pictures (photos, PDF pages) are decoded at
 * the power-of-two downsample that matches their on-screen size, on a
 * background thread, within a fixed memory budget. While a sharper version
 * decodes, the closest cached version is shown.
 */
class ImageCache(budgetBytes: Int = 96 * 1024 * 1024, private val onReady: (() -> Unit)? = null) {
    private val lru = object : LruCache<String, Bitmap>(budgetBytes) {
        override fun sizeOf(key: String, value: Bitmap) = value.allocationByteCount
    }
    private val dims = ConcurrentHashMap<String, Pair<Int, Int>>()
    private val inflight = ConcurrentHashMap.newKeySet<String>()
    private val exec by lazy { Executors.newFixedThreadPool(2) { r -> Thread(r, "img-decode").apply { isDaemon = true } } }
    private val main = android.os.Handler(android.os.Looper.getMainLooper())

    /** Bumped whenever a new decode lands (render caches holding pictures re-record). */
    @Volatile var version = 0
        private set

    fun size(key: String, bytes: ByteArray): Pair<Int, Int> = dims.getOrPut(key) {
        val o = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, o)
        o.outWidth.coerceAtLeast(1) to o.outHeight.coerceAtLeast(1)
    }

    /** Downsample (1, 2, 4 … 64) so the decoded width is still ≥ the on-screen width. */
    fun sampleFor(key: String, bytes: ByteArray, targetPx: Float): Int {
        val w = size(key, bytes).first
        var s = 1
        while (s < 64 && w / (s * 2) >= targetPx) s *= 2
        return s
    }

    private fun decode(bytes: ByteArray, sample: Int): Bitmap? =
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, BitmapFactory.Options().apply { inSampleSize = sample })

    /**
     * Bitmap for [key] at [sample]. With [sync] (exports, thumbnails) it decodes
     * now; otherwise it returns the best cached level and decodes in background.
     */
    fun get(key: String, bytes: ByteArray, sample: Int, sync: Boolean): Bitmap? {
        val k = "$key@$sample"
        lru.get(k)?.let { return it }
        if (sync) return decode(bytes, sample)?.also { lru.put(k, it) }
        if (inflight.add(k)) exec.execute {
            val bmp = runCatching { decode(bytes, sample) }.getOrNull()
            main.post {
                inflight.remove(k)
                if (bmp != null) { lru.put(k, bmp); version++; onReady?.invoke() }
            }
        }
        // Closest available level meanwhile (coarser first, then finer).
        var s = sample * 2
        while (s <= 64) { lru.get("$key@$s")?.let { return it }; s *= 2 }
        s = sample / 2
        while (s >= 1) { lru.get("$key@$s")?.let { return it }; s /= 2 }
        return null
    }

    fun clear() = lru.evictAll()
}
