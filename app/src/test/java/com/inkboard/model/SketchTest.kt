package com.inkboard.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import kotlin.math.abs
import kotlin.math.hypot

class SketchTest {
    @Test
    fun stabilizerTrailsAndRemovesJitter() {
        val s = Stabilizer(10f)
        s.reset(0f, 0f)
        assertNull(s.feed(3f, 4f)) // within the string length: no movement
        val p = s.feed(20f, 0f)!!
        assertEquals(10f, p.first, 1e-4f) // trails by the radius
        // A jittery horizontal line comes out much smoother
        val s2 = Stabilizer(6f); s2.reset(0f, 0f)
        var maxDev = 0f
        for (i in 1..200) {
            val q = s2.feed(i * 2f, if (i % 2 == 0) 3f else -3f) ?: continue
            maxDev = maxOf(maxDev, abs(q.second))
        }
        assertTrue("max deviation $maxDev", maxDev < 1.5f)
    }

    @Test
    fun pressureCurves() {
        assertEquals(0.5f, PressureCurve.NORMAL.map(0.5f), 1e-6f)
        assertTrue(PressureCurve.SOFT.map(0.5f) > 0.6f)
        assertTrue(PressureCurve.FIRM.map(0.5f) < 0.35f)
        assertEquals(1f, PressureCurve.FIRM.map(1.2f), 1e-6f)
    }

    @Test
    fun symmetryCopies() {
        val pts = floatArrayOf(10f, 5f, 0.5f, 20f, 7f, 0.6f)
        val g = Guides(SymmetryMode.VERTICAL, symX = 0f, symY = 0f)
        val v = Symmetry.copies(pts, g)
        assertEquals(1, v.size)
        assertEquals(-10f, v[0][0], 1e-4f); assertEquals(5f, v[0][1], 1e-4f); assertEquals(0.5f, v[0][2], 1e-6f)
        assertEquals(3, Symmetry.copies(pts, g.copy(symmetry = SymmetryMode.QUAD)).size)
        val r = Symmetry.copies(pts, g.copy(symmetry = SymmetryMode.RADIAL, radialN = 6))
        assertEquals(5, r.size)
        // Every radial copy keeps the distance to the centre
        for (c in r) assertEquals(hypot(10f, 5f), hypot(c[0], c[1]), 1e-3f)
        assertEquals(0, Symmetry.copies(pts, Guides()).size)
    }

    @Test
    fun perspectiveSnap() {
        val g = Guides(perspective = 1, vp1x = 1000f, vp1y = 0f)
        // Almost horizontal → horizontal
        val h = Perspective.snap(0f, 500f, 300f, 510f, g)
        assertEquals(500f, h[3], 1e-3f)
        // Heading roughly to the VP → exactly toward it
        val t = Perspective.snap(0f, 500f, 300f, 330f, g)
        val cross = (t[2] - t[0]) * (0f - t[1]) - (t[3] - t[1]) * (1000f - t[0])
        assertEquals(0f, cross, 1e-1f)
        // Near-vertical → vertical
        val v = Perspective.snap(0f, 0f, 8f, 300f, g)
        assertEquals(0f, v[2], 1e-3f)
        // Drawing away from the VP still snaps (opposite direction)
        val away = Perspective.snap(500f, 250f, 300f, 350f, g)
        assertTrue(away[2] < 500f)
    }

    @Test
    fun codecV3KeepsGuidesAndTaper() {
        val d = Document()
        d.guides = Guides(SymmetryMode.RADIAL, 8, 12f, 34f, 2, 1f, 2f, 3f, 2f, false)
        val s = Stroke(d.nextId(), floatArrayOf(0f, 0f, 0.5f, 10f, 0f, 0.5f), 0, 3f, PenType.PEN, taper = true)
        d.add(s)
        val bytes = ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray()
        val r = DocCodec.read(ByteArrayInputStream(bytes))
        assertEquals(d.guides, r.guides)
        assertTrue((r.byId(s.id) as Stroke).taper)
    }

    @Test
    fun markDirtyBumpsVersionWithoutHistory() {
        val d = Document()
        val v = d.version
        d.markDirty()
        assertTrue(d.version > v)
        assertTrue(!d.canUndo)
    }
}
