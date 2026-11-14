package com.inkboard.core

import android.util.Log

/** Recognized shape from the Rust core. */
sealed class RecognizedShape {
    data class Line(val x0: Float, val y0: Float, val x1: Float, val y1: Float) : RecognizedShape()
    class Polygon(val pts: FloatArray) : RecognizedShape()
    data class Ellipse(val cx: Float, val cy: Float, val rx: Float, val ry: Float, val angle: Float) : RecognizedShape()
}

/** JNI bridge to libinkcore.so (Rust). Fails soft: the app works without it. */
object InkCore {
    val available: Boolean = try {
        System.loadLibrary("inkcore"); true
    } catch (t: Throwable) {
        Log.w("InkCore", "native core unavailable", t); false
    }

    @JvmStatic private external fun nativeRecognize(pts: FloatArray, perfect: Boolean): FloatArray?
    @JvmStatic private external fun nativeSimplify(pts: FloatArray, epsilon: Float): FloatArray?

    /** [xy] = packed x,y pairs. */
    fun recognize(xy: FloatArray, perfect: Boolean): RecognizedShape? {
        if (!available || xy.size < 6) return null
        val r = try { nativeRecognize(xy, perfect) } catch (t: Throwable) { null } ?: return null
        if (r.isEmpty()) return null
        return when (r[0].toInt()) {
            1 -> RecognizedShape.Line(r[1], r[2], r[3], r[4])
            2 -> RecognizedShape.Polygon(r.copyOfRange(2, 2 + 2 * r[1].toInt()))
            3 -> RecognizedShape.Ellipse(r[1], r[2], r[3], r[4], r[5])
            else -> null
        }
    }

    fun simplify(xy: FloatArray, epsilon: Float): FloatArray =
        if (!available) xy else try { nativeSimplify(xy, epsilon) ?: xy } catch (t: Throwable) { xy }
}
