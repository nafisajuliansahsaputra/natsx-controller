package com.natsx.controller.core.connection

import android.content.SharedPreferences
import com.natsx.controller.core.protocol.TransportPreferenceMode
import com.natsx.controller.core.protocol.TransportPreferencePayload

enum class AndroidTransportPreference(
    val displayName: String,
    val protocolMode: TransportPreferenceMode,
) {
    AUTO("Smart Auto", TransportPreferenceMode.AUTO),
    WIFI("Prefer Wi-Fi", TransportPreferenceMode.WIFI),
    BLUETOOTH("Prefer Bluetooth", TransportPreferenceMode.BLUETOOTH),
    USB("Prefer USB", TransportPreferenceMode.USB_DIRECT);

    fun toPayload(): TransportPreferencePayload =
        TransportPreferencePayload(protocolMode)

    companion object {
        fun fromStoredValue(
            value: String?,
        ): AndroidTransportPreference =
            entries.firstOrNull {
                it.name == value
            } ?: AUTO
    }
}

class TransportPreferenceSettings(
    private val preferences: SharedPreferences,
) {
    var preference: AndroidTransportPreference
        get() =
            AndroidTransportPreference
                .fromStoredValue(
                    preferences.getString(
                        KEY_PREFERENCE,
                        AndroidTransportPreference.AUTO.name,
                    ),
                )
        set(value) {
            preferences
                .edit()
                .putString(
                    KEY_PREFERENCE,
                    value.name,
                )
                .apply()
        }

    private companion object {
        const val KEY_PREFERENCE =
            "transport_preference"
    }
}
