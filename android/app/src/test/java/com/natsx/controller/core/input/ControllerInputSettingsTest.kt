package com.natsx.controller.core.input

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ControllerInputSettingsTest {
    @Test
    fun standardProfilePreservesLegacyStickDefaults() {
        assertEquals(
            ControllerInputTuning.Default,
            ControllerProfile.STANDARD.preset,
        )
        assertEquals(
            0.05f,
            ControllerProfile.STANDARD
                .preset!!
                .leftDeadzone,
        )
        assertEquals(
            0.07f,
            ControllerProfile.STANDARD
                .preset!!
                .rightDeadzone,
        )
        assertEquals(
            1f,
            ControllerProfile.STANDARD
                .preset!!
                .leftSensitivity,
        )
        assertEquals(
            1f,
            ControllerProfile.STANDARD
                .preset!!
                .rightSensitivity,
        )
    }

    @Test
    fun allPresetProfilesStayInsideSupportedTuningBounds() {
        ControllerProfile.entries
            .mapNotNull {
                it.preset
            }
            .forEach { tuning ->
                assertTrue(
                    tuning.leftDeadzone in
                        ControllerInputTuning.MIN_DEADZONE..
                            ControllerInputTuning.MAX_DEADZONE,
                )
                assertTrue(
                    tuning.rightDeadzone in
                        ControllerInputTuning.MIN_DEADZONE..
                            ControllerInputTuning.MAX_DEADZONE,
                )
                assertTrue(
                    tuning.leftSensitivity in
                        ControllerInputTuning.MIN_SENSITIVITY..
                            ControllerInputTuning.MAX_SENSITIVITY,
                )
                assertTrue(
                    tuning.rightSensitivity in
                        ControllerInputTuning.MIN_SENSITIVITY..
                            ControllerInputTuning.MAX_SENSITIVITY,
                )
            }

        assertNull(
            ControllerProfile.CUSTOM.preset,
        )
    }
}
