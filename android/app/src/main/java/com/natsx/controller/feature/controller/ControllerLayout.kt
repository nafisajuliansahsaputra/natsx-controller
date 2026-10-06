package com.natsx.controller.feature.controller

data class NormalizedControlPosition(
    val x: Float,
    val y: Float,
) {
    init {
        require(x in 0f..1f)
        require(y in 0f..1f)
    }
}

data class ControllerLayout(
    val positions:
        Map<String, NormalizedControlPosition> =
        emptyMap(),
) {
    fun positionFor(
        controlId: String,
        defaultX: Float,
        defaultY: Float,
    ): NormalizedControlPosition =
        positions[controlId]
            ?: NormalizedControlPosition(
                defaultX,
                defaultY,
            )

    fun withPosition(
        controlId: String,
        x: Float,
        y: Float,
    ): ControllerLayout =
        copy(
            positions =
                positions +
                    (
                        controlId to
                            NormalizedControlPosition(
                                x.coerceIn(0f, 1f),
                                y.coerceIn(0f, 1f),
                            )
                    ),
        )

    companion object {
        val Default = ControllerLayout()
    }
}

object ControllerLayoutCodec {
    fun encode(
        layout: ControllerLayout,
    ): String =
        layout.positions
            .toSortedMap()
            .entries
            .joinToString(
                separator = ";",
            ) { (id, position) ->
                id + "," +
                    position.x + "," +
                    position.y
            }

    fun decode(
        encoded: String?,
    ): ControllerLayout {
        if (encoded.isNullOrBlank()) {
            return ControllerLayout.Default
        }

        val positions =
            buildMap {
                encoded
                    .split(';')
                    .forEach { item ->
                        val parts =
                            item.split(',')

                        if (parts.size != 3) {
                            return@forEach
                        }

                        val id =
                            parts[0]
                                .takeIf {
                                    it.matches(
                                        Regex(
                                            "[A-Z0-9_]+",
                                        ),
                                    )
                                }
                                ?: return@forEach

                        val x =
                            parts[1]
                                .toFloatOrNull()
                                ?: return@forEach
                        val y =
                            parts[2]
                                .toFloatOrNull()
                                ?: return@forEach

                        if (
                            x !in 0f..1f ||
                            y !in 0f..1f
                        ) {
                            return@forEach
                        }

                        put(
                            id,
                            NormalizedControlPosition(
                                x,
                                y,
                            ),
                        )
                    }
            }

        return ControllerLayout(
            positions,
        )
    }
}
