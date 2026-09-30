package com.natsx.controller.feature.pairing

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import com.natsx.controller.pairing.AndroidPairingPrompt
import com.natsx.controller.transport.wifi.DiscoveredReceiver

class PairingScreenView(
    context: Context,
) : LinearLayout(context) {
    var onSearchRequested: (() -> Unit)? = null
    var onReceiverSelected: ((DiscoveredReceiver) -> Unit)? = null
    var onConfirmRequested: (() -> Unit)? = null
    var onCancelRequested: (() -> Unit)? = null

    private val statusText: TextView
    private val receiversContainer: LinearLayout
    private val searchButton: Button
    private val codeText: TextView
    private val confirmButton: Button
    private val cancelButton: Button

    init {
        orientation = VERTICAL
        gravity = Gravity.CENTER_HORIZONTAL
        setPadding(dp(28), dp(20), dp(28), dp(20))
        setBackgroundColor(Color.rgb(9, 9, 11))

        addView(
            TextView(context).apply {
                text = "NATSX Controller"
                setTextColor(Color.WHITE)
                textSize = 28f
                typeface = Typeface.DEFAULT_BOLD
                gravity = Gravity.CENTER
            },
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                LayoutParams.WRAP_CONTENT,
            ),
        )

        addView(
            TextView(context).apply {
                text =
                    "Pair this phone with your Windows receiver. " +
                        "Both devices must be on the same local network."
                setTextColor(Color.rgb(175, 175, 182))
                textSize = 14f
                gravity = Gravity.CENTER
                setPadding(0, dp(6), 0, dp(14))
            },
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                LayoutParams.WRAP_CONTENT,
            ),
        )

        searchButton = Button(context).apply {
            text = "Find Windows receiver"
            setOnClickListener {
                onSearchRequested?.invoke()
            }
        }

        addView(
            searchButton,
            LayoutParams(
                dp(240),
                dp(48),
            ),
        )

        statusText = TextView(context).apply {
            text = "Open NATSX Controller Receiver on your PC and choose Pair new phone."
            setTextColor(Color.rgb(205, 205, 212))
            textSize = 14f
            gravity = Gravity.CENTER
            setPadding(0, dp(12), 0, dp(8))
        }

        addView(
            statusText,
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                LayoutParams.WRAP_CONTENT,
            ),
        )

        receiversContainer = LinearLayout(context).apply {
            orientation = VERTICAL
        }

        addView(
            ScrollView(context).apply {
                addView(receiversContainer)
            },
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                0,
                1f,
            ),
        )

        codeText = TextView(context).apply {
            text = "------"
            setTextColor(Color.WHITE)
            textSize = 38f
            typeface = Typeface.MONOSPACE
            gravity = Gravity.CENTER
            visibility = View.GONE
            setPadding(0, dp(8), 0, dp(8))
        }

        addView(
            codeText,
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                LayoutParams.WRAP_CONTENT,
            ),
        )

        val actionRow = LinearLayout(context).apply {
            orientation = HORIZONTAL
            gravity = Gravity.CENTER
        }

        confirmButton = Button(context).apply {
            text = "Codes match"
            isEnabled = false
            visibility = View.GONE
            setOnClickListener {
                isEnabled = false
                onConfirmRequested?.invoke()
            }
        }

        cancelButton = Button(context).apply {
            text = "Cancel"
            isEnabled = false
            visibility = View.GONE
            setOnClickListener {
                onCancelRequested?.invoke()
            }
        }

        actionRow.addView(
            confirmButton,
            LayoutParams(
                dp(150),
                dp(48),
            ).apply {
                marginEnd = dp(8)
            },
        )

        actionRow.addView(
            cancelButton,
            LayoutParams(
                dp(110),
                dp(48),
            ),
        )

        addView(
            actionRow,
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                LayoutParams.WRAP_CONTENT,
            ),
        )
    }

    fun showSearching() {
        searchButton.isEnabled = false
        receiversContainer.removeAllViews()
        statusText.text = "Searching for NATSX Receiver on the local network…"
        hideCode()
    }

    fun showReceivers(receivers: List<DiscoveredReceiver>) {
        searchButton.isEnabled = true
        receiversContainer.removeAllViews()
        hideCode()

        if (receivers.isEmpty()) {
            statusText.text =
                "No receiver found. Make sure the PC receiver is open and both devices are on the same network."
            return
        }

        statusText.text =
            "Choose the Windows receiver you want to pair with."

        receivers.forEach { receiver ->
            val address =
                receiver.address.hostAddress ?: "local network"

            val button = Button(context).apply {
                text =
                    receiver.response.receiverName +
                        "  •  " +
                        address

                setOnClickListener {
                    searchButton.isEnabled = false
                    onReceiverSelected?.invoke(receiver)
                }
            }

            receiversContainer.addView(
                button,
                LayoutParams(
                    LayoutParams.MATCH_PARENT,
                    dp(50),
                ).apply {
                    bottomMargin = dp(7)
                },
            )
        }
    }

    fun showPairingStarted(receiverName: String) {
        receiversContainer.removeAllViews()
        searchButton.isEnabled = false
        statusText.text =
            "Connecting securely to " +
                receiverName +
                "…"
        hideCode()
    }

    fun showPrompt(prompt: AndroidPairingPrompt) {
        receiversContainer.removeAllViews()
        searchButton.isEnabled = false

        statusText.text =
            "Compare this code with " +
                prompt.receiverName +
                " on the PC. Confirm only when both codes are identical."

        codeText.text = prompt.sasCode
        codeText.visibility = View.VISIBLE

        confirmButton.visibility = View.VISIBLE
        confirmButton.isEnabled = true

        cancelButton.visibility = View.VISIBLE
        cancelButton.isEnabled = true
    }

    fun showFailure(message: String) {
        searchButton.isEnabled = true
        receiversContainer.removeAllViews()
        statusText.text = message
        hideCode()
    }

    fun showSuccess(receiverName: String) {
        searchButton.isEnabled = false
        receiversContainer.removeAllViews()
        statusText.text =
            "Paired with " +
                receiverName +
                ". Starting controller…"
        hideCode()
    }

    private fun hideCode() {
        codeText.text = "------"
        codeText.visibility = View.GONE
        confirmButton.visibility = View.GONE
        confirmButton.isEnabled = false
        cancelButton.visibility = View.GONE
        cancelButton.isEnabled = false
    }

    private fun dp(value: Int): Int =
        (value * resources.displayMetrics.density)
            .toInt()
}
