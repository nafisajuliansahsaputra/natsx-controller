package com.natsx.controller.core.input

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AnalogStickProcessorTest {
    private val processor = AnalogStickProcessor(deadzone = 0.05f)

    @Test
    fun centerIsNeutral() {
        val output = processor.process(
            pointerX = 100f,
            pointerY = 100f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        assertEquals(0, output.x)
        assertEquals(0, output.y)
    }

    @Test
    fun rightEdgeMapsNearMaximumX() {
        val output = processor.process(
            pointerX = 150f,
            pointerY = 100f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        assertEquals(32767, output.x)
        assertEquals(0, output.y)
    }

    @Test
    fun topEdgeMapsToPositiveY() {
        val output = processor.process(
            pointerX = 100f,
            pointerY = 50f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        assertEquals(0, output.x)
        assertEquals(32767, output.y)
    }

    @Test
    fun tinyMovementInsideJitterThresholdKeepsPreviousOutput() {
        val processor = AnalogStickProcessor(
            deadzone = 0.05f,
            jitterThreshold = 150,
        )

        val first = processor.process(
            pointerX = 125f,
            pointerY = 100f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        val second = processor.process(
            pointerX = 125.1f,
            pointerY = 100f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        assertEquals(first, second)
    }

    @Test
    fun resetRemovesPreviousJitterAnchor() {
        val processor = AnalogStickProcessor(
            deadzone = 0.05f,
            jitterThreshold = 500,
        )

        processor.process(
            pointerX = 125f,
            pointerY = 100f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        processor.reset()

        val output = processor.process(
            pointerX = 100f,
            pointerY = 100f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        assertEquals(0, output.x)
        assertEquals(0, output.y)
    }

    @Test
    fun higherSensitivityBoostsMidTravelWithoutChangingFullScale() {
        val normal =
            AnalogStickProcessor(
                deadzone = 0f,
                sensitivity = 1f,
                jitterThreshold = 0,
            )
        val faster =
            AnalogStickProcessor(
                deadzone = 0f,
                sensitivity = 1.25f,
                jitterThreshold = 0,
            )

        val normalMid =
            normal.process(
                pointerX = 125f,
                pointerY = 100f,
                centerX = 100f,
                centerY = 100f,
                radius = 50f,
            )
        val fasterMid =
            faster.process(
                pointerX = 125f,
                pointerY = 100f,
                centerX = 100f,
                centerY = 100f,
                radius = 50f,
            )

        assertTrue(
            fasterMid.x > normalMid.x,
        )

        val fasterEdge =
            faster.process(
                pointerX = 150f,
                pointerY = 100f,
                centerX = 100f,
                centerY = 100f,
                radius = 50f,
            )

        assertEquals(
            32767,
            fasterEdge.x,
        )
    }

    @Test
    fun diagonalIsRadiallyClamped() {
        val output = processor.process(
            pointerX = 200f,
            pointerY = 0f,
            centerX = 100f,
            centerY = 100f,
            radius = 50f,
        )

        assertTrue(output.x in 23100..23200)
        assertTrue(output.y in 23100..23200)
    }
}
