package com.natsx.controller.core.input

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.cos
import kotlin.math.sin

class StickCalibrationEstimatorTest {
    @Test
    fun centerUsesRepeatedNaturalThumbTaps() {
        val estimator =
            StickCalibrationEstimator(
                minimumRangeSamples = 16,
                requiredDirectionSectors = 4,
            )

        listOf(
            0.08f to -0.04f,
            0.10f to -0.05f,
            0.09f to -0.06f,
            0.11f to -0.05f,
            0.10f to -0.05f,
        ).forEach { (x, y) ->
            estimator.addCenterTap(x, y)
        }

        repeat(16) { index ->
            val angle =
                Math.PI * 2.0 *
                    index /
                    16.0
            estimator.addRangeSample(
                x =
                    0.10f +
                        (cos(angle) * 0.82)
                            .toFloat(),
                y =
                    -0.05f +
                        (sin(angle) * 0.82)
                            .toFloat(),
            )
        }

        val result =
            estimator.estimate()

        assertTrue(
            result.centerOffsetX in
                0.095f..0.105f,
        )
        assertTrue(
            result.centerOffsetY in
                -0.055f..-0.045f,
        )
        assertTrue(
            result.travelScale in
                0.80f..0.84f,
        )
    }

    @Test
    fun rangeRequiresDirectionalCoverage() {
        val estimator =
            StickCalibrationEstimator(
                minimumRangeSamples = 12,
                requiredDirectionSectors = 4,
            )

        repeat(5) {
            estimator.addCenterTap(
                0f,
                0f,
            )
        }

        repeat(20) { index ->
            estimator.addRangeSample(
                x =
                    0.70f +
                        index * 0.005f,
                y = 0f,
            )
        }

        assertEquals(
            false,
            estimator.isRangeReady,
        )
    }

    @Test
    fun extremeCaptureIsClampedToSafeCalibrationBounds() {
        val estimator =
            StickCalibrationEstimator(
                minimumRangeSamples = 16,
                requiredDirectionSectors = 4,
            )

        repeat(5) {
            estimator.addCenterTap(
                0.50f,
                -0.50f,
            )
        }

        repeat(16) { index ->
            val angle =
                Math.PI * 2.0 *
                    index /
                    16.0

            estimator.addRangeSample(
                x =
                    0.50f +
                        (cos(angle) * 1.30)
                            .toFloat(),
                y =
                    -0.50f +
                        (sin(angle) * 1.30)
                            .toFloat(),
            )
        }

        val result =
            estimator.estimate()

        assertEquals(
            StickCalibration.MAX_CENTER_OFFSET,
            result.centerOffsetX,
        )
        assertEquals(
            -StickCalibration.MAX_CENTER_OFFSET,
            result.centerOffsetY,
        )
        assertEquals(
            StickCalibration.MAX_TRAVEL_SCALE,
            result.travelScale,
        )
    }
}
