package com.natsx.controller.feature.controller

import kotlin.math.max
import kotlin.math.min

/** One uniform transform for the 2400 x 1080 landscape artwork, controls and HUD.
 * The 50px artwork margin already accommodates ordinary cutouts; only excess
 * insets consume additional space. This avoids adding a second margin on phones.
 */
internal data class ControllerDesignViewport(
    val left: Float,
    val top: Float,
    val scale: Float,
) {
    val width: Float get() = 2400f * scale
    val height: Float get() = 1080f * scale
    fun x(designX: Float): Float = left + designX * scale
    fun y(designY: Float): Float = top + designY * scale

    companion object {
        fun fit(
            width: Float,
            height: Float,
            insetLeft: Float = 0f,
            insetTop: Float = 0f,
            insetRight: Float = 0f,
            insetBottom: Float = 0f,
        ): ControllerDesignViewport {
            val w = width.coerceAtLeast(1f)
            val h = height.coerceAtLeast(1f)
            // The visible artwork fits inside the insets; its empty 50px border
            // can lie under a cutout. Keep the artboard centered and unstretched.
            val scale = min(min(w / 2400f, h / 1080f),
                min((w - 2f * max(insetLeft, insetRight)).coerceAtLeast(1f) / 2300f,
                    (h - 2f * max(insetTop, insetBottom)).coerceAtLeast(1f) / 980f))

            return ControllerDesignViewport(
                left = (w - 2400f * scale) / 2f,
                top = (h - 1080f * scale) / 2f,
                scale = scale,
            )
        }
    }
}
