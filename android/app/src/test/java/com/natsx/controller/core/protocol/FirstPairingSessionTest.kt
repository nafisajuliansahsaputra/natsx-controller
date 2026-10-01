package com.natsx.controller.core.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class FirstPairingSessionTest {
    private val allCapabilities =
        TransportCapabilities.WIFI or
            TransportCapabilities.BLUETOOTH or
            TransportCapabilities.USB_DIRECT

    @Test
    fun twoSidedConfirmationDerivesSameTrustSecret() {
        val androidPeer = PeerId.createRandom()
        val windowsPeer = PeerId.createRandom()

        AndroidFirstPairingSession(
            androidPeer,
            allCapabilities,
        ).use { android ->
            WindowsFirstPairingSession(
                windowsPeer,
                allCapabilities,
            ).use { windows ->
                val response =
                    windows.acceptHelloFrame(
                        android.createHelloFrame(100uL),
                        200uL,
                    )

                val androidSas =
                    android.acceptResponseFrame(
                        response.frameBytes,
                    )

                assertEquals(
                    response.sas,
                    androidSas,
                )
                assertEquals(
                    6,
                    androidSas.length,
                )

                val confirm =
                    android.createConfirmFrame(
                        userConfirmedSas = true,
                        monotonicTimestampMicros = 300uL,
                    )

                val completion =
                    windows.acceptConfirmFrame(
                        confirm,
                        userConfirmedSas = true,
                        monotonicTimestampMicros = 400uL,
                    )

                completion.result.use { windowsResult ->
                    android.acceptCompleteFrame(
                        completion.frameBytes,
                    ).use { androidResult ->
                        val windowsSecret =
                            windowsResult.copyTrustSecret()
                        val androidSecret =
                            androidResult.copyTrustSecret()

                        try {
                            assertArrayEquals(
                                windowsSecret,
                                androidSecret,
                            )
                            assertEquals(
                                windowsPeer,
                                androidResult.remotePeerId,
                            )
                            assertEquals(
                                androidPeer,
                                windowsResult.remotePeerId,
                            )
                        } finally {
                            windowsSecret.fill(0)
                            androidSecret.fill(0)
                        }
                    }
                }
            }
        }
    }

    @Test
    fun androidRejectingSasDoesNotCreateConfirm() {
        AndroidFirstPairingSession(
            PeerId.createRandom(),
            allCapabilities,
        ).use { android ->
            WindowsFirstPairingSession(
                PeerId.createRandom(),
                allCapabilities,
            ).use { windows ->
                val response =
                    windows.acceptHelloFrame(
                        android.createHelloFrame(100uL),
                        200uL,
                    )

                android.acceptResponseFrame(
                    response.frameBytes,
                )

                assertThrows(
                    PairingRejectedException::class.java,
                ) {
                    android.createConfirmFrame(
                        userConfirmedSas = false,
                        monotonicTimestampMicros = 300uL,
                    )
                }
            }
        }
    }

    @Test
    fun windowsRejectingSasDoesNotCompletePairing() {
        AndroidFirstPairingSession(
            PeerId.createRandom(),
            allCapabilities,
        ).use { android ->
            WindowsFirstPairingSession(
                PeerId.createRandom(),
                allCapabilities,
            ).use { windows ->
                val response =
                    windows.acceptHelloFrame(
                        android.createHelloFrame(100uL),
                        200uL,
                    )

                android.acceptResponseFrame(
                    response.frameBytes,
                )

                val confirm =
                    android.createConfirmFrame(
                        userConfirmedSas = true,
                        monotonicTimestampMicros = 300uL,
                    )

                assertThrows(
                    PairingRejectedException::class.java,
                ) {
                    windows.acceptConfirmFrame(
                        confirm,
                        userConfirmedSas = false,
                        monotonicTimestampMicros = 400uL,
                    )
                }
            }
        }
    }
}
