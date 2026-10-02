package com.natsx.controller.feature.controller

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ControllerDesignViewportTest {
    @Test fun phoneArtboardHasExactlyFiftyPixelMargins() {
        val view = ControllerDesignViewport.fit(2400f, 1080f)
        assertEquals(1f, view.scale, 0.0001f)
        assertEquals(50f, view.x(50f), 0.0001f)
        assertEquals(2350f, view.x(2350f), 0.0001f)
        assertEquals(50f, view.y(50f), 0.0001f)
        assertEquals(1030f, view.y(1030f), 0.0001f)
    }

    @Test fun ordinaryCutoutUsesExistingMarginInsteadOfAddingAnother() {
        val view = ControllerDesignViewport.fit(2400f, 1080f, 32f, 8f, 0f, 16f)
        assertEquals(50f, view.x(50f), 0.0001f)
        assertEquals(50f, view.y(50f), 0.0001f)
        assertEquals(1f, view.scale, 0.0001f)
    }

    @Test fun sixteenByNineKeepsCirclesAndShouldersUnstretched() {
        val view = ControllerDesignViewport.fit(1920f, 1080f)
        assertEquals(0.8f, view.scale, 0.0001f)
        assertEquals(108f, view.top, 0.0001f)
        assertEquals(view.x(450f) - view.x(50f), (view.y(450f) - view.y(50f)), 0.0001f)
    }

    @Test fun largeInsetsRemainOutsideVisibleArtwork() {
        for ((width, height) in listOf(2400f to 1080f, 1920f to 1080f, 1870f to 841f)) {
            val view = ControllerDesignViewport.fit(width, height, 110f, 70f, 20f, 90f)
            assertTrue(view.x(50f) >= 110f - 0.001f)
            assertTrue(view.x(2350f) <= width - 20f + 0.001f)
            assertTrue(view.y(50f) >= 70f - 0.001f)
            assertTrue(view.y(1030f) <= height - 90f + 0.001f)
        }
    }
}
