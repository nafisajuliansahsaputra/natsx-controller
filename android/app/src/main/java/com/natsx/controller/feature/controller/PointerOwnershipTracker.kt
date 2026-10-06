package com.natsx.controller.feature.controller

internal class PointerOwnershipTracker<T> {
    private val controlByPointer =
        mutableMapOf<Int, T>()

    fun tryClaim(
        pointerId: Int,
        control: T,
    ): Boolean {
        if (
            pointerId in controlByPointer ||
            isControlClaimed(
                control,
            )
        ) {
            return false
        }

        controlByPointer[pointerId] =
            control

        return true
    }

    fun controlFor(
        pointerId: Int,
    ): T? =
        controlByPointer[pointerId]

    fun isControlClaimed(
        control: T,
    ): Boolean =
        controlByPointer.values
            .any {
                it ==
                    control
            }

    fun release(
        pointerId: Int,
    ): T? =
        controlByPointer.remove(
            pointerId,
        )

    fun clear() {
        controlByPointer.clear()
    }

    val activePointerCount: Int
        get() =
            controlByPointer.size
}
