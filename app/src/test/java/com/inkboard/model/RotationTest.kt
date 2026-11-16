package com.inkboard.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

class RotationTest {
    private val q = (Math.PI / 2).toFloat()

    @Test fun strokeRotatesAroundPoint() {
        val s = Stroke(1, floatArrayOf(10f, 0f, 0.5f, 20f, 0f, 0.5f), 0, 2f, PenType.PEN)
        val r = s.rotated(q, 0f, 0f) as Stroke
        assertEquals(0f, r.pts[0], 1e-4f); assertEquals(10f, r.pts[1], 1e-4f)
        assertEquals(0.5f, r.pts[2], 0f)
    }

    @Test fun ellipseAccumulatesAngle() {
        val e = ShapeEl(1, ShapeKind.ELLIPSE, floatArrayOf(100f, 0f, 50f, 20f, 0f), 0, 2f)
        val r = e.rotated(q, 0f, 0f) as ShapeEl
        assertEquals(0f, r.geo[0], 1e-3f); assertEquals(100f, r.geo[1], 1e-3f); assertEquals(q, r.geo[4], 1e-6f)
    }

    @Test fun rotatedTextHitTestsInItsOwnFrame() {
        val t = TextEl(1, 0f, 0f, "Một dòng chữ khá dài", 20f, 0)
        val r = t.rotated(q, t.box.cx, t.box.cy) as TextEl
        assertEquals(q, r.rot, 1e-6f)
        assertTrue(r.bounds.h > r.bounds.w) // a long line stands upright after 90°
        assertTrue(r.hit(t.box.cx, t.box.cy - t.box.w * 0.4f, 1f, true))
        assertFalse(r.hit(t.box.cx + t.box.w * 0.4f, t.box.cy, 1f, true))
    }

    @Test fun blocksAndTablesStayUprightButMove() {
        val n = Node(1, 100f, -20f, 80f, 40f, "A", NodeKind.ROUND, 0, 16f)
        val r = n.rotated(q, 0f, 0f) as Node
        assertEquals(n.w, r.w, 0f); assertEquals(n.h, r.h, 0f)
        assertEquals(0f, r.x + r.w / 2, 1e-3f); assertEquals(140f, r.y + r.h / 2, 1e-3f)
    }

    @Test fun rotationSurvivesSaveLoad() {
        val d = Document()
        val t = TextEl(d.nextId(), 0f, 0f, "x", 20f, 0, 0.5f)
        val s = StickyEl(d.nextId(), 0f, 0f, 100f, 100f, "y", 0, 14f, 0.25f)
        val i = ImageEl(d.nextId(), 0f, 0f, 50f, 50f, "k", 1.25f); d.images["k"] = byteArrayOf(1)
        d.add(t, s, i)
        val r = DocCodec.read(ByteArrayInputStream(ByteArrayOutputStream().also { DocCodec.write(d, it) }.toByteArray()))
        assertEquals(0.5f, (r.byId(t.id) as TextEl).rot, 0f)
        assertEquals(0.25f, (r.byId(s.id) as StickyEl).rot, 0f)
        assertEquals(1.25f, (r.byId(i.id) as ImageEl).rot, 0f)
    }
}
