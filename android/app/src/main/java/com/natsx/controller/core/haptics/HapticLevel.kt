package com.natsx.controller.core.haptics

enum class HapticLevel(
    val gameAmplitudeScale: Float,
) {
    OFF(0f),
    LOW(0.35f),
    MEDIUM(0.65f),
    HIGH(1f);

    companion object {
        fun fromStoredValue(
            value: String?,
        ): HapticLevel =
            entries.firstOrNull {
                it.name == value
            } ?: MEDIUM
    }
}
