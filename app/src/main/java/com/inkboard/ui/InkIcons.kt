package com.inkboard.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.graphics.vector.path
import androidx.compose.ui.unit.dp

/** Custom icons not present in the Material set. */
object InkIcons {
    val Eraser: ImageVector by lazy {
        ImageVector.Builder("Eraser", 24.dp, 24.dp, 24f, 24f).apply {
            path(stroke = SolidColor(Color.Black), strokeLineWidth = 1.8f, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round) {
                moveTo(16.5f, 3.5f); lineTo(21f, 8f); lineTo(11f, 18f); lineTo(6.5f, 18f); lineTo(3.5f, 15f); close()
                moveTo(9f, 10.5f); lineTo(13.5f, 15f)
                moveTo(6.5f, 20.5f); lineTo(20.5f, 20.5f)
            }
        }.build()
    }

    val Lasso: ImageVector by lazy {
        ImageVector.Builder("Lasso", 24.dp, 24.dp, 24f, 24f).apply {
            path(stroke = SolidColor(Color.Black), strokeLineWidth = 1.8f, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round) {
                moveTo(7f, 15f)
                curveTo(2f, 12.5f, 3.5f, 4.5f, 12f, 4.5f)
                curveTo(20.5f, 4.5f, 22f, 12f, 15f, 14f)
                curveTo(12f, 15f, 9f, 14.5f, 8f, 16.5f)
                curveTo(7.2f, 18.5f, 9f, 20.5f, 11f, 19.5f)
            }
        }.build()
    }

    val Pen: ImageVector by lazy {
        ImageVector.Builder("Pen", 24.dp, 24.dp, 24f, 24f).apply {
            path(stroke = SolidColor(Color.Black), strokeLineWidth = 1.8f, strokeLineCap = StrokeCap.Round, strokeLineJoin = StrokeJoin.Round) {
                moveTo(4f, 20f); lineTo(5f, 15.5f); lineTo(15.5f, 5f); lineTo(19f, 8.5f); lineTo(8.5f, 19f); close()
                moveTo(13.5f, 7f); lineTo(17f, 10.5f)
            }
        }.build()
    }
}
