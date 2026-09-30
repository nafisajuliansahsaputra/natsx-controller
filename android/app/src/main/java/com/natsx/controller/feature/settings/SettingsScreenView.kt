package com.natsx.controller.feature.settings

import android.content.Context
import android.graphics.Color
import android.graphics.Typeface
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import com.natsx.controller.core.connection.ControllerConnectionStatus
import com.natsx.controller.haptics.HapticStrength
import com.natsx.controller.security.TrustedReceiverMetadata

class SettingsScreenView(
    context: Context,
) : ScrollView(context) {
    var onBackRequested: (() -> Unit)? = null
    var onPairNewRequested: (() -> Unit)? = null
    var onHapticStrengthChanged: ((HapticStrength) -> Unit)? = null
    var onForgetRequested: ((TrustedReceiverMetadata) -> Unit)? = null

    private val content = LinearLayout(context).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(28), dp(20), dp(28), dp(28))
        setBackgroundColor(Color.rgb(9, 9, 11))
    }

    private val connectionText = bodyText()
    private val hapticButtons = linkedMapOf<HapticStrength, Button>()
    private val trustedContainer = LinearLayout(context).apply {
        orientation = LinearLayout.VERTICAL
    }

    init {
        isFillViewport = true
        addView(
            content,
            LayoutParams(
                LayoutParams.MATCH_PARENT,
                LayoutParams.WRAP_CONTENT,
            ),
        )

        val header = LinearLayout(context).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        header.addView(
            Button(context).apply {
                text = "Back"
                setOnClickListener {
                    onBackRequested?.invoke()
                }
            },
            LinearLayout.LayoutParams(
                dp(90),
                dp(44),
            ),
        )

        header.addView(
            TextView(context).apply {
                text = "Controller settings"
                setTextColor(Color.WHITE)
                textSize = 24f
                typeface = Typeface.DEFAULT_BOLD
                setPadding(dp(16), 0, 0, 0)
            },
            LinearLayout.LayoutParams(
                0,
                LinearLayout.LayoutParams.WRAP_CONTENT,
                1f,
            ),
        )

        content.addView(header)

        content.addView(sectionTitle("Connection"))
        content.addView(connectionText)

        content.addView(sectionTitle("Haptic strength"))

        val hapticRow = LinearLayout(context).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }

        HapticStrength.entries.forEach { strength ->
            val button = Button(context).apply {
                text =
                    strength.name
                        .lowercase()
                        .replaceFirstChar {
                            it.uppercase()
                        }

                setOnClickListener {
                    onHapticStrengthChanged?.invoke(
                        strength,
                    )
                }
            }

            hapticButtons[strength] = button

            hapticRow.addView(
                button,
                LinearLayout.LayoutParams(
                    0,
                    dp(46),
                    1f,
                ).apply {
                    marginEnd = dp(6)
                },
            )
        }

        content.addView(hapticRow)

        content.addView(sectionTitle("Trusted PCs"))
        content.addView(trustedContainer)

        content.addView(
            Button(context).apply {
                text = "Pair another Windows PC"
                setOnClickListener {
                    onPairNewRequested?.invoke()
                }
            },
            LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(50),
            ).apply {
                topMargin = dp(12)
            },
        )

        content.addView(
            TextView(context).apply {
                text =
                    "Forgetting a PC deletes this phone's encrypted trust record. " +
                        "The controller will stop using that PC until it is paired again."
                setTextColor(Color.rgb(150, 150, 158))
                textSize = 12f
                setPadding(0, dp(12), 0, 0)
            },
        )
    }

    fun bind(
        hapticStrength: HapticStrength,
        trustedReceivers: List<TrustedReceiverMetadata>,
        connectionStatus: ControllerConnectionStatus,
    ) {
        connectionText.text =
            connectionStatus.message +
                buildString {
                    connectionStatus.receiverName
                        ?.takeIf { it.isNotBlank() }
                        ?.let {
                            append("\nReceiver: ")
                            append(it)
                        }

                    connectionStatus.hostAddress
                        ?.takeIf { it.isNotBlank() }
                        ?.let {
                            append("\nAddress: ")
                            append(it)
                        }
                }

        hapticButtons.forEach { (strength, button) ->
            button.alpha =
                if (strength == hapticStrength) {
                    1f
                } else {
                    0.58f
                }

            button.isAllCaps = false
        }

        trustedContainer.removeAllViews()

        if (trustedReceivers.isEmpty()) {
            trustedContainer.addView(
                bodyText().apply {
                    text = "No trusted Windows PCs."
                },
            )
            return
        }

        trustedReceivers.forEach { receiver ->
            trustedContainer.addView(
                trustedReceiverRow(receiver),
                LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT,
                ).apply {
                    bottomMargin = dp(8)
                },
            )
        }
    }

    private fun trustedReceiverRow(
        receiver: TrustedReceiverMetadata,
    ): View {
        val row = LinearLayout(context).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setPadding(
                dp(14),
                dp(10),
                dp(10),
                dp(10),
            )
            setBackgroundColor(Color.rgb(24, 24, 28))
        }

        val identitySuffix =
            receiver.windowsDeviceId
                .toString()
                .takeLast(8)

        val title =
            receiver.displayName
                ?.takeIf { it.isNotBlank() }
                ?: "Windows PC"

        val details = buildString {
            append(title)
            append("\nID …")
            append(identitySuffix)

            receiver.lastHostAddress
                ?.takeIf { it.isNotBlank() }
                ?.let {
                    append("  •  ")
                    append(it)
                    append(":")
                    append(receiver.lastPort)
                }
        }

        row.addView(
            TextView(context).apply {
                text = details
                setTextColor(Color.WHITE)
                textSize = 14f
            },
            LinearLayout.LayoutParams(
                0,
                LinearLayout.LayoutParams.WRAP_CONTENT,
                1f,
            ),
        )

        row.addView(
            Button(context).apply {
                text = "Forget"
                isAllCaps = false
                setOnClickListener {
                    onForgetRequested?.invoke(receiver)
                }
            },
            LinearLayout.LayoutParams(
                dp(96),
                dp(42),
            ),
        )

        return row
    }

    private fun sectionTitle(textValue: String): TextView =
        TextView(context).apply {
            text = textValue
            setTextColor(Color.WHITE)
            textSize = 17f
            typeface = Typeface.DEFAULT_BOLD
            setPadding(0, dp(22), 0, dp(9))
        }

    private fun bodyText(): TextView =
        TextView(context).apply {
            setTextColor(Color.rgb(195, 195, 202))
            textSize = 14f
        }

    private fun dp(value: Int): Int =
        (value * resources.displayMetrics.density)
            .toInt()
}
