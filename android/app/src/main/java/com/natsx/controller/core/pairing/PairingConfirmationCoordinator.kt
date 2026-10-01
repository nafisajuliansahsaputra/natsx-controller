package com.natsx.controller.core.pairing

import com.natsx.controller.core.protocol.PeerId
import java.util.concurrent.CompletableFuture
import java.util.concurrent.CopyOnWriteArraySet
import java.util.concurrent.TimeUnit
import java.util.concurrent.TimeoutException

data class PairingPrompt(
    val comparisonCode: String,
    val remotePeerId: PeerId,
)

class PairingConfirmationCoordinator {
    private val gate = Any()
    private val listeners =
        CopyOnWriteArraySet<(PairingPrompt?) -> Unit>()

    @Volatile
    private var prompt: PairingPrompt? = null

    private var pending:
        CompletableFuture<Boolean>? = null

    fun currentPrompt(): PairingPrompt? =
        prompt

    fun addListener(
        listener: (PairingPrompt?) -> Unit,
    ) {
        listeners += listener
        listener(prompt)
    }

    fun removeListener(
        listener: (PairingPrompt?) -> Unit,
    ) {
        listeners -= listener
    }

    fun requestConfirmation(
        pairingPrompt: PairingPrompt,
        timeoutSeconds: Long = 120,
    ): Boolean {
        require(timeoutSeconds in 1..600)

        val future =
            CompletableFuture<Boolean>()

        synchronized(gate) {
            check(pending == null) {
                "Another pairing confirmation is already pending."
            }

            pending = future
            prompt = pairingPrompt
        }

        notifyListeners(pairingPrompt)

        return try {
            future.get(
                timeoutSeconds,
                TimeUnit.SECONDS,
            )
        } catch (exception: TimeoutException) {
            false
        } finally {
            synchronized(gate) {
                if (pending === future) {
                    pending = null
                    prompt = null
                }
            }

            notifyListeners(null)
        }
    }

    fun resolve(
        approved: Boolean,
    ): Boolean {
        val future =
            synchronized(gate) {
                pending
            } ?: return false

        return future.complete(approved)
    }

    private fun notifyListeners(
        value: PairingPrompt?,
    ) {
        listeners.forEach { listener ->
            runCatching {
                listener(value)
            }
        }
    }
}
