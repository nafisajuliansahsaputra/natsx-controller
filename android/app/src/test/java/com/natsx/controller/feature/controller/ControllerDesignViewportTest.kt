package com.natsx.controller.feature.controller

import org.junit.Assert.assertEquals
import org.junit.Test

class ControllerDesignViewportTest {
    @Test fun figmaPhoneKeepsItsOwnFiftyPixelMargins() {
        val view = ControllerDesignViewport.fit(2400f, 1080f)
        assertEquals(1f, view.scale, 0.0001f)
        assertEquals(50f, view.x(50f), 0.0001f)
        assertEquals(2350f, view.x(2350f), 0.0001f)
        assertEquals(50f, view.y(50f), 0.0001f)
        assertEquals(1030f, view.y(1030f), 0.0001f)
    }

    @Test fun cameraAndGestureInsetsDoNotShrinkOrLetterboxTheArtboard() {
        val view = ControllerDesignViewport.fit(2400f, 1080f, 110f, 70f, 20f, 90f)
        assertEquals(0f, view.left, 0.0001f)
        assertEquals(0f, view.top, 0.0001f)
        assertEquals(2400f, view.width, 0.0001f)
        assertEquals(1080f, view.height, 0.0001f)
        assertEquals(1f, view.scale, 0.0001f)
    }

    @Test fun differentAspectsFillTheScreenButRetainOneCircleSizeScale() {
        for ((w, h) in listOf(1920f to 1080f, 1870f to 841f, 2560f to 1080f)) {
            val view = ControllerDesignViewport.fit(w, h)
            assertEquals(0f, view.x(0f), 0.0001f)
            assertEquals(0f, view.y(0f), 0.0001f)
            assertEquals(w, view.x(2400f), 0.001f)
            assertEquals(h, view.y(1080f), 0.001f)
            assertEquals(kotlin.math.min(w / 2400f, h / 1080f), view.scale, 0.0001f)
        }
    }
}
