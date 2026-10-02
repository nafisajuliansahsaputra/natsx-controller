package com.natsx.controller.feature.controller

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ControllerLayoutCodecTest {
    @Test
    fun customPositionsRoundTripDeterministically() {
        val layout =
            ControllerLayout.Default
                .withPosition(
                    "LEFT_STICK",
                    0.22f,
                    0.66f,
                )
                .withPosition(
                    "A",
                    0.84f,
                    0.78f,
                )

        val encoded =
            ControllerLayoutCodec
                .encode(layout)
        val decoded =
            ControllerLayoutCodec
                .decode(encoded)

        assertEquals(
            layout,
            decoded,
        )
        assertTrue(
            encoded.startsWith(
                "A,",
            ),
        )
    }

    @Test
    fun malformedEntriesAreIgnored() {
        val decoded =
            ControllerLayoutCodec.decode(
                "LEFT_STICK,0.2,0.6;bad entry;A,3.0,0.5;B,0.8,0.7",
            )

        assertEquals(
            2,
            decoded.positions.size,
        )
        assertTrue(
            "LEFT_STICK" in
                decoded.positions,
        )
        assertTrue(
            "B" in decoded.positions,
        )
    }
}
