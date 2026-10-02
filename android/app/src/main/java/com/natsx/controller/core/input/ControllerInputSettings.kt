package com.natsx.controller.core.input

import android.content.SharedPreferences

data class ControllerInputTuning(
    val leftDeadzone: Float,
    val rightDeadzone: Float,
    val leftSensitivity: Float,
    val rightSensitivity: Float,
) {
    init {
        require(leftDeadzone in MIN_DEADZONE..MAX_DEADZONE)
        require(rightDeadzone in MIN_DEADZONE..MAX_DEADZONE)
        require(leftSensitivity in MIN_SENSITIVITY..MAX_SENSITIVITY)
        require(rightSensitivity in MIN_SENSITIVITY..MAX_SENSITIVITY)
    }

    companion object {
        const val MIN_DEADZONE = 0f
        const val MAX_DEADZONE = 0.25f
        const val MIN_SENSITIVITY = 0.5f
        const val MAX_SENSITIVITY = 1.5f

        val Default =
            ControllerInputTuning(
                leftDeadzone = 0.05f,
                rightDeadzone = 0.07f,
                leftSensitivity = 1f,
                rightSensitivity = 1f,
            )
    }
}

enum class ControllerProfile(
    val displayName: String,
    val preset: ControllerInputTuning?,
) {
    STANDARD(
        displayName = "Standard",
        preset = ControllerInputTuning.Default,
    ),
    EFOOTBALL(
        displayName = "eFootball",
        preset =
            ControllerInputTuning(
                leftDeadzone = 0.04f,
                rightDeadzone = 0.06f,
                leftSensitivity = 1.10f,
                rightSensitivity = 1.08f,
            ),
    ),
    PRECISION(
        displayName = "Precision",
        preset =
            ControllerInputTuning(
                leftDeadzone = 0.08f,
                rightDeadzone = 0.10f,
                leftSensitivity = 0.90f,
                rightSensitivity = 0.90f,
            ),
    ),
    CUSTOM(
        displayName = "Custom",
        preset = null,
    );

    companion object {
        fun fromStoredValue(
            value: String?,
        ): ControllerProfile =
            entries.firstOrNull {
                it.name == value
            } ?: STANDARD
    }
}

class ControllerInputSettings(
    private val preferences: SharedPreferences,
) {
    val profile: ControllerProfile
        get() =
            ControllerProfile.fromStoredValue(
                preferences.getString(
                    KEY_PROFILE,
                    ControllerProfile.STANDARD.name,
                ),
            )

    fun current(): ControllerInputTuning =
        ControllerInputTuning(
            leftDeadzone =
                preferences.getFloat(
                    KEY_LEFT_DEADZONE,
                    ControllerInputTuning.Default.leftDeadzone,
                ),
            rightDeadzone =
                preferences.getFloat(
                    KEY_RIGHT_DEADZONE,
                    ControllerInputTuning.Default.rightDeadzone,
                ),
            leftSensitivity =
                preferences.getFloat(
                    KEY_LEFT_SENSITIVITY,
                    ControllerInputTuning.Default.leftSensitivity,
                ),
            rightSensitivity =
                preferences.getFloat(
                    KEY_RIGHT_SENSITIVITY,
                    ControllerInputTuning.Default.rightSensitivity,
                ),
        )

    fun applyProfile(
        profile: ControllerProfile,
    ): ControllerInputTuning {
        val tuning =
            profile.preset ?: current()

        persist(
            profile = profile,
            tuning = tuning,
        )

        return tuning
    }

    fun updateCustom(
        tuning: ControllerInputTuning,
    ) {
        persist(
            profile = ControllerProfile.CUSTOM,
            tuning = tuning,
        )
    }

    private fun persist(
        profile: ControllerProfile,
        tuning: ControllerInputTuning,
    ) {
        preferences
            .edit()
            .putString(
                KEY_PROFILE,
                profile.name,
            )
            .putFloat(
                KEY_LEFT_DEADZONE,
                tuning.leftDeadzone,
            )
            .putFloat(
                KEY_RIGHT_DEADZONE,
                tuning.rightDeadzone,
            )
            .putFloat(
                KEY_LEFT_SENSITIVITY,
                tuning.leftSensitivity,
            )
            .putFloat(
                KEY_RIGHT_SENSITIVITY,
                tuning.rightSensitivity,
            )
            .apply()
    }

    private companion object {
        const val KEY_PROFILE =
            "controller_profile"
        const val KEY_LEFT_DEADZONE =
            "left_deadzone"
        const val KEY_RIGHT_DEADZONE =
            "right_deadzone"
        const val KEY_LEFT_SENSITIVITY =
            "left_sensitivity"
        const val KEY_RIGHT_SENSITIVITY =
            "right_sensitivity"
    }
}
