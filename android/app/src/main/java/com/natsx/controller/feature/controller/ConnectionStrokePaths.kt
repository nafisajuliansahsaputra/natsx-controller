package com.natsx.controller.feature.controller

import android.graphics.Path

/** Exact panel contours from the supplied SVG, in 2400 x 1080 coordinates. */
internal object ConnectionStrokePaths {
    fun create(): List<Path> = listOf(
        Path().apply {
            moveTo(1651.67f, -0.5f)
            lineTo(1651.24f, 0.25f)
            lineTo(1481.6f, 294.073f)
            cubicTo(1476.58f, 302.776f, 1467.97f, 310.628f, 1458.15f, 316.302f)
            cubicTo(1448.63f, 321.798f, 1437.91f, 325.277f, 1428.11f, 325.489f)
            lineTo(1427.17f, 325.5f)
            lineTo(972.64f, 325.502f)
            cubicTo(962.59f, 325.502f, 951.49f, 321.978f, 941.662f, 316.304f)
            cubicTo(931.834f, 310.63f, 923.232f, 302.778f, 918.207f, 294.075f)
            lineTo(748.567f, 0.25f)
            lineTo(748.134f, -0.5f)
            lineTo(1651.67f, -0.5f)
            close()
        },
        Path().apply {
            moveTo(2400.91f, 869.5f)
            lineTo(2400.91f, 1080.5f)
            lineTo(1757.13f, 1080.5f)
            lineTo(1757.57f, 1079.75f)
            lineTo(1860.81f, 900.927f)
            cubicTo(1865.84f, 892.224f, 1874.44f, 884.372f, 1884.27f, 878.698f)
            cubicTo(1894.09f, 873.024f, 1905.19f, 869.5f, 1915.24f, 869.5f)
            lineTo(2400.91f, 869.5f)
            close()
        },
        Path().apply {
            moveTo(-0.499756f, 869.5f)
            lineTo(-0.499756f, 1080.5f)
            lineTo(643.279f, 1080.5f)
            lineTo(642.845f, 1079.75f)
            lineTo(539.602f, 900.927f)
            cubicTo(534.577f, 892.224f, 525.975f, 884.372f, 516.147f, 878.698f)
            cubicTo(506.319f, 873.024f, 495.219f, 869.5f, 485.169f, 869.5f)
            lineTo(-0.499756f, 869.5f)
            close()
        },
    )
}
