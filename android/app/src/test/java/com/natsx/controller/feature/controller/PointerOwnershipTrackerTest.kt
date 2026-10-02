package com.natsx.controller.feature.controller

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class PointerOwnershipTrackerTest {
    @Test
    fun eFootballStyleFourFingerCombinationKeepsIndependentOwnership() {
        val tracker =
            PointerOwnershipTracker<String>()

        assertTrue(
            tracker.tryClaim(
                10,
                "LEFT_STICK",
            ),
        )
        assertTrue(
            tracker.tryClaim(
                11,
                "RB",
            ),
        )
        assertTrue(
            tracker.tryClaim(
                12,
                "A",
            ),
        )
        assertTrue(
            tracker.tryClaim(
                13,
                "RIGHT_STICK",
            ),
        )

        assertEquals(
            4,
            tracker.activePointerCount,
        )
        assertEquals(
            "LEFT_STICK",
            tracker.controlFor(10),
        )
        assertEquals(
            "RB",
            tracker.controlFor(11),
        )
        assertEquals(
            "A",
            tracker.controlFor(12),
        )
        assertEquals(
            "RIGHT_STICK",
            tracker.controlFor(13),
        )
    }

    @Test
    fun onePhysicalControlCannotBeOwnedByTwoPointers() {
        val tracker =
            PointerOwnershipTracker<String>()

        assertTrue(
            tracker.tryClaim(
                1,
                "A",
            ),
        )
        assertFalse(
            tracker.tryClaim(
                2,
                "A",
            ),
        )

        assertEquals(
            1,
            tracker.activePointerCount,
        )
        assertNull(
            tracker.controlFor(2),
        )
    }

    @Test
    fun releasingOneFingerDoesNotCancelOtherControls() {
        val tracker =
            PointerOwnershipTracker<String>()

        tracker.tryClaim(
            20,
            "LEFT_STICK",
        )
        tracker.tryClaim(
            21,
            "RT",
        )
        tracker.tryClaim(
            22,
            "B",
        )

        assertEquals(
            "RT",
            tracker.release(21),
        )

        assertEquals(
            2,
            tracker.activePointerCount,
        )
        assertEquals(
            "LEFT_STICK",
            tracker.controlFor(20),
        )
        assertEquals(
            "B",
            tracker.controlFor(22),
        )
        assertFalse(
            tracker.isControlClaimed(
                "RT",
            ),
        )
    }

    @Test
    fun clearDropsAllOwnershipAfterCancelOrFocusLoss() {
        val tracker =
            PointerOwnershipTracker<String>()

        tracker.tryClaim(
            1,
            "LEFT_STICK",
        )
        tracker.tryClaim(
            2,
            "X",
        )

        tracker.clear()

        assertEquals(
            0,
            tracker.activePointerCount,
        )
        assertNull(
            tracker.controlFor(1),
        )
        assertNull(
            tracker.controlFor(2),
        )
    }
}
