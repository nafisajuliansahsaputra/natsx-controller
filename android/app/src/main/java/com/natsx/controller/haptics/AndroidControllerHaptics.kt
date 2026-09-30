package com.natsx.controller.haptics

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import com.natsx.controller.core.haptics.ControllerHapticSink
import kotlin.math.max
import kotlin.math.roundToInt

enum class HapticStrength(
    val persistedValue: String,
    val multiplier: Float,
    val touchAmplitude: Int,
    val touchDurationMilliseconds: Long,
) {
    OFF("off", 0f, 0, 0),
    LOW("low", 0.55f, 65, 8),
    MEDIUM("medium", 0.78f, 110, 11),
    HIGH("high", 1f, 175, 15);

    companion object {
        fun fromPersistedValue(value: String?): HapticStrength =
            entries.firstOrNull {
                it.persistedValue == value
            } ?: MEDIUM
    }
}

class ControllerHapticSettings(
    context: Context,
) {
    private val preferences =
        context.getSharedPreferences(
            PREFERENCES_NAME,
            Context.MODE_PRIVATE,
        )

    var strength: HapticStrength
        get() =
            HapticStrength.fromPersistedValue(
                preferences.getString(
                    KEY_STRENGTH,
                    HapticStrength.MEDIUM.persistedValue,
                ),
            )
        set(value) {
            preferences.edit()
                .putString(
                    KEY_STRENGTH,
                    value.persistedValue,
                )
                .apply()
        }

    private companion object {
        const val PREFERENCES_NAME = "natsx_controller_feel"
        const val KEY_STRENGTH = "haptic_strength"
    }
}

class AndroidControllerHaptics(
    context: Context,
    private val settings: ControllerHapticSettings,
) : ControllerHapticSink {
    private val vibrator: Vibrator =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            context
                .getSystemService(VibratorManager::class.java)
                .defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Vibrator::class.java)
        }

    override fun touchTick() {
        val strength = settings.strength

        if (strength == HapticStrength.OFF ||
            !vibrator.hasVibrator()
        ) {
            return
        }

        vibrator.vibrate(
            VibrationEffect.createOneShot(
                strength.touchDurationMilliseconds,
                strength.touchAmplitude,
            ),
        )
    }

    override fun applyRumble(
        lowFrequency: Int,
        highFrequency: Int,
        durationMilliseconds: Int,
    ) {
        val strength = settings.strength

        if (strength == HapticStrength.OFF ||
            !vibrator.hasVibrator()
        ) {
            stopRumble()
            return
        }

        val low = lowFrequency.coerceIn(0, 255)
        val high = highFrequency.coerceIn(0, 255)
        val rawIntensity = max(low, high)

        if (rawIntensity == 0) {
            stopRumble()
            return
        }

        val amplitude =
            (rawIntensity * strength.multiplier)
                .roundToInt()
                .coerceIn(1, 255)

        val duration =
            if (durationMilliseconds > 0) {
                durationMilliseconds
                    .coerceIn(
                        MIN_RUMBLE_DURATION_MS,
                        MAX_RUMBLE_DURATION_MS,
                    )
                    .toLong()
            } else {
                DEFAULT_RUMBLE_REFRESH_MS
            }

        vibrator.vibrate(
            VibrationEffect.createOneShot(
                duration,
                amplitude,
            ),
        )
    }

    override fun stopRumble() {
        vibrator.cancel()
    }

    private companion object {
        const val DEFAULT_RUMBLE_REFRESH_MS = 220L
        const val MIN_RUMBLE_DURATION_MS = 10
        const val MAX_RUMBLE_DURATION_MS = 2_000
    }
}
