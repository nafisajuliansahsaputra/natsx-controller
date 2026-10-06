package com.natsx.controller.core.haptics

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import com.natsx.controller.core.protocol.RumblePayload
import java.io.Closeable
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicLong
import kotlin.math.max
import kotlin.math.roundToInt

/**
 * Converts the Xbox low/high motor pair into the single vibrator exposed by
 * most phones. A watchdog always cancels stale output so a lost control link
 * can never leave the phone vibrating indefinitely.
 */
class AndroidHapticEngine(
    context: Context,
    private val settings: HapticSettings,
) : Closeable {
    private val vibrator: Vibrator? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            context
                .getSystemService(
                    VibratorManager::class.java,
                )
                ?.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(
                Context.VIBRATOR_SERVICE,
            ) as? Vibrator
        }

    private val watchdog =
        Executors.newSingleThreadScheduledExecutor { runnable ->
            Thread(
                runnable,
                "natsx-haptic-watchdog",
            ).apply {
                isDaemon = true
            }
        }

    private val generation =
        AtomicLong(0)

    fun handleGameRumble(
        payload: RumblePayload,
    ) {
        val activeVibrator =
            vibrator
                ?.takeIf {
                    it.hasVibrator()
                }
                ?: return

        val currentGeneration =
            generation.incrementAndGet()

        val level =
            settings.level

        val rawAmplitude =
            max(
                payload.lowFrequencyMotor,
                payload.highFrequencyMotor,
            )

        if (
            level == HapticLevel.OFF ||
            rawAmplitude <= 0
        ) {
            runCatching {
                activeVibrator.cancel()
            }
            return
        }

        val amplitude =
            (
                rawAmplitude *
                    level.gameAmplitudeScale
                )
                .roundToInt()
                .coerceIn(
                    1,
                    255,
                )

        val effect =
            if (activeVibrator.hasAmplitudeControl()) {
                VibrationEffect.createWaveform(
                    longArrayOf(
                        0,
                        PULSE_WINDOW_MILLIS,
                    ),
                    intArrayOf(
                        0,
                        amplitude,
                    ),
                    0,
                )
            } else {
                VibrationEffect.createWaveform(
                    longArrayOf(
                        0,
                        PULSE_WINDOW_MILLIS,
                    ),
                    0,
                )
            }

        runCatching {
            activeVibrator.vibrate(
                effect,
            )
        }

        watchdog.schedule(
            {
                if (
                    generation.get() ==
                    currentGeneration
                ) {
                    runCatching {
                        activeVibrator.cancel()
                    }
                }
            },
            STALE_RUMBLE_TIMEOUT_MILLIS,
            TimeUnit.MILLISECONDS,
        )
    }

    fun stopGameRumble() {
        generation.incrementAndGet()

        runCatching {
            vibrator?.cancel()
        }
    }

    override fun close() {
        stopGameRumble()
        watchdog.shutdownNow()
    }

    private companion object {
        const val PULSE_WINDOW_MILLIS =
            1_000L

        // Windows refreshes a non-zero rumble state every 500 ms. This margin
        // survives a delayed refresh/handover while still bounding stuck
        // vibration after a total control-path failure.
        const val STALE_RUMBLE_TIMEOUT_MILLIS =
            1_500L
    }
}
