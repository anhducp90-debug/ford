package com.inkboard.perf

import com.inkboard.model.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class PerfAuditTest {
    private inline fun ms(block: () -> Unit): Double { val t = System.nanoTime(); block(); return (System.nanoTime() - t) / 1e6 }

    @Test
    fun measure() {
        if (System.getenv("PERF_AUDIT") == null) return
        for (n in listOf(10_000, 100_000)) {
            val d = Document()
            val els = ArrayList<Element>(n)
            val rnd = java.util.Random(1)
            for (i in 0 until n) {
                val bx = (i % 400) * 120f; val by = (i / 400) * 120f
                val pts = FloatArray(40 * 3) { k -> when (k % 3) { 0 -> bx + (k / 3) * 2f; 1 -> by + rnd.nextFloat() * 30f; else -> 0.5f } }
                els += Stroke(d.nextId(), pts, 0, 3f, PenType.PEN)
            }
            d.add(els)
            val node = Node(d.nextId(), 0f, 0f, 100f, 40f, "A", NodeKind.ROUND, 0, 16f); d.add(node)
            val text = TextEl(d.nextId(), 500f, 500f, "x", 16f, 0); d.add(text)
            var bytes = ByteArray(0)
            val snap = ms { d.snapshot() }
            val w = ms { bytes = ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray() }
            val r = ms { DocCodec.read(ByteArrayInputStream(bytes)) }
            val hit = ms { repeat(100) { d.hitTop(5000f + it, 5000f, 6f) } } / 100
            val drop = ms { repeat(100) { DropRules.resolve(d, setOf(text.id), text.id, 50f, 20f) } } / 100
            val comp = ms { Components.collect(d) }
            val bounds = ms { d.contentBounds() }
            val one = Stroke(d.nextId(), floatArrayOf(0f, 0f, 0.5f, 5f, 5f, 0.5f), 0, 3f, PenType.PEN)
            val add = ms { d.add(one) }
            val rem = ms { d.remove(listOf(one.id)) }
            println("PERF n=$n file=${bytes.size / 1024}KB snapshot=%.1f write=%.0f read=%.0f hitTop=%.2f dropResolve=%.2f components=%.0f contentBounds=%.1f add=%.2f remove=%.1f (ms)".format(snap, w, r, hit, drop, comp, bounds, add, rem))
        }
    }
}
