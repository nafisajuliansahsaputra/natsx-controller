package com.natsx.controller.feature.controller

import kotlin.math.min

/** Full-screen artboard anchors, with one uniform size scale for round controls.
 * Insets belong to the Android window; they must not add another border to Figma's layout.
 */
internal data class ControllerDesignViewport(
    val left: Float,
    val top: Float,
    val scale: Float,
    val scaleX: Float = scale,
    val scaleY: Float = scale,
) {
    val width: Float get() = 2400f * scaleX
    val height: Float get() = 1080f * scaleY
    fun x(designX: Float): Float = left + designX * scaleX
    fun y(designY: Float): Float = top + designY * scaleY

    companion object {
        @Suppress("UNUSED_PARAMETER")
        fun fit(
            width: Float,
            height: Float,
            insetLeft: Float = 0f,
            insetTop: Float = 0f,
            insetRight: Float = 0f,
            insetBottom: Float = 0f,
        ): ControllerDesignViewport {
            val sx = width.coerceAtLeast(1f) / 2400f
            val sy = height.coerceAtLeast(1f) / 1080f
            return ControllerDesignViewport(0f, 0f, min(sx, sy), sx, sy)
        }
    }
}
