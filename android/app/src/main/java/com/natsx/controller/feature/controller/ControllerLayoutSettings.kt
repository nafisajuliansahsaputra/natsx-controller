package com.natsx.controller.feature.controller

import android.content.SharedPreferences

class ControllerLayoutSettings(
    private val preferences:
        SharedPreferences,
) {
    fun current(): ControllerLayout =
        ControllerLayoutCodec.decode(
            preferences.getString(
                KEY_LAYOUT,
                null,
            ),
        )

    fun update(
        layout: ControllerLayout,
    ) {
        preferences
            .edit()
            .putString(
                KEY_LAYOUT,
                ControllerLayoutCodec
                    .encode(layout),
            )
            .apply()
    }

    fun reset() {
        preferences
            .edit()
            .remove(
                KEY_LAYOUT,
            )
            .apply()
    }

    private companion object {
        const val KEY_LAYOUT =
            "controller_layout"
    }
}
