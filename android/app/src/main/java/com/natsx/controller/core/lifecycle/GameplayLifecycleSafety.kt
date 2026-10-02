package com.natsx.controller.core.lifecycle

class GameplayLifecycleSafety(
    private val releaseInputs: () -> Unit,
    private val stopGameRumble: () -> Unit,
) {
    fun onWindowFocusChanged(
        hasFocus: Boolean,
    ) {
        if (!hasFocus) {
            releaseInputs()
        }
    }

    fun onStop() {
        releaseInputs()
        stopGameRumble()
    }
}
