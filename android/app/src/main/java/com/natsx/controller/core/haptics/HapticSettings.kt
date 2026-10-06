package com.natsx.controller.core.haptics

import android.content.SharedPreferences

class HapticSettings(
    private val preferences: SharedPreferences,
) {
    var level: HapticLevel
        get() =
            HapticLevel.fromStoredValue(
                preferences.getString(
                    KEY_LEVEL,
                    HapticLevel.MEDIUM.name,
                ),
            )
        set(value) {
            preferences
                .edit()
                .putString(
                    KEY_LEVEL,
                    value.name,
                )
                .apply()
        }

    private companion object {
        const val KEY_LEVEL =
            "haptic_level"
    }
}
