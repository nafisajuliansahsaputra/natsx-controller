package com.natsx.controller.core.input

import kotlin.math.atan2
import kotlin.math.hypot

data class CalibrationPoint(
    val x: Float,
    val y: Float,
)

class StickCalibrationEstimator(
    private val requiredCenterTaps: Int = DEFAULT_CENTER_TAPS,
    private val minimumRangeSamples: Int = DEFAULT_MINIMUM_RANGE_SAMPLES,
    private val requiredDirectionSectors: Int = DEFAULT_REQUIRED_DIRECTION_SECTORS,
) {
    private val centerSamples =
        mutableListOf<CalibrationPoint>()
    private val rangeSamples =
        mutableListOf<CalibrationPoint>()

    init {
        require(requiredCenterTaps >= 3)
        require(minimumRangeSamples >= 12)
        require(requiredDirectionSectors in 4..DIRECTION_SECTOR_COUNT)
    }

    val centerTapCount: Int
        get() = centerSamples.size

    val rangeSampleCount: Int
        get() = rangeSamples.size

    val isCenterReady: Boolean
        get() = centerSamples.size >= requiredCenterTaps

    val coveredDirectionSectors: Int
        get() =
            if (!isCenterReady) {
                0
            } else {
                val center = centerAverage()
                rangeSamples
                    .mapNotNull { sample ->
                        val dx = sample.x - center.x
                        val dy = sample.y - center.y
                        val distance = hypot(dx, dy)

                        if (distance < MIN_DIRECTION_RADIUS) {
                            null
                        } else {
                            directionSector(dx, dy)
                        }
                    }
                    .toSet()
                    .size
            }

    val isRangeReady: Boolean
        get() =
            isCenterReady &&
                rangeSamples.size >= minimumRangeSamples &&
                coveredDirectionSectors >= requiredDirectionSectors

    fun addCenterTap(
        x: Float,
        y: Float,
    ) {
        if (isCenterReady) {
            return
        }

        centerSamples +=
            CalibrationPoint(
                x = x.coerceIn(-MAX_CAPTURE_RADIUS, MAX_CAPTURE_RADIUS),
                y = y.coerceIn(-MAX_CAPTURE_RADIUS, MAX_CAPTURE_RADIUS),
            )
    }

    fun addRangeSample(
        x: Float,
        y: Float,
    ) {
        if (!isCenterReady) {
            return
        }

        rangeSamples +=
            CalibrationPoint(
                x = x.coerceIn(-MAX_CAPTURE_RADIUS, MAX_CAPTURE_RADIUS),
                y = y.coerceIn(-MAX_CAPTURE_RADIUS, MAX_CAPTURE_RADIUS),
            )
    }

    fun estimate(): StickCalibration {
        check(isCenterReady) {
            "Center calibration is incomplete."
        }
        check(isRangeReady) {
            "Range calibration is incomplete."
        }

        val center =
            centerAverage()

        val distances =
            rangeSamples
                .map { sample ->
                    hypot(
                        sample.x - center.x,
                        sample.y - center.y,
                    )
                }
                .sorted()

        val percentileIndex =
            ((distances.lastIndex) * RANGE_PERCENTILE)
                .toInt()
                .coerceIn(
                    0,
                    distances.lastIndex,
                )

        return StickCalibration(
            centerOffsetX =
                center.x.coerceIn(
                    -StickCalibration.MAX_CENTER_OFFSET,
                    StickCalibration.MAX_CENTER_OFFSET,
                ),
            centerOffsetY =
                center.y.coerceIn(
                    -StickCalibration.MAX_CENTER_OFFSET,
                    StickCalibration.MAX_CENTER_OFFSET,
                ),
            travelScale =
                distances[percentileIndex]
                    .coerceIn(
                        StickCalibration.MIN_TRAVEL_SCALE,
                        StickCalibration.MAX_TRAVEL_SCALE,
                    ),
        )
    }

    private fun centerAverage(): CalibrationPoint {
        check(centerSamples.isNotEmpty())

        return CalibrationPoint(
            x =
                centerSamples
                    .map { it.x }
                    .average()
                    .toFloat(),
            y =
                centerSamples
                    .map { it.y }
                    .average()
                    .toFloat(),
        )
    }

    private fun directionSector(
        x: Float,
        y: Float,
    ): Int {
        var angle =
            atan2(
                y.toDouble(),
                x.toDouble(),
            )

        if (angle < 0.0) {
            angle +=
                Math.PI * 2.0
        }

        val sectorSize =
            (Math.PI * 2.0) /
                DIRECTION_SECTOR_COUNT

        return (angle / sectorSize)
            .toInt()
            .coerceIn(
                0,
                DIRECTION_SECTOR_COUNT - 1,
            )
    }

    companion object {
        const val DEFAULT_CENTER_TAPS = 5
        const val DEFAULT_MINIMUM_RANGE_SAMPLES = 24
        const val DEFAULT_REQUIRED_DIRECTION_SECTORS = 6
        const val DIRECTION_SECTOR_COUNT = 8

        private const val MAX_CAPTURE_RADIUS = 1.35f
        private const val MIN_DIRECTION_RADIUS = 0.35f
        private const val RANGE_PERCENTILE = 0.90f
    }
}
