package com.natsx.controller.security

import android.content.Context
import android.util.Base64
import com.natsx.controller.core.protocol.SessionId
import java.security.SecureRandom

class AndroidDeviceIdentityStore(
    context: Context,
) {
    private val preferences =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun getOrCreate(): SessionId {
        val encoded = preferences.getString(KEY_DEVICE_ID, null)

        if (encoded != null) {
            val bytes = Base64.decode(encoded, Base64.NO_WRAP)
            require(bytes.size == SessionId.SIZE) {
                "Stored Android Device ID is invalid."
            }
            return SessionId.fromBytes(bytes)
        }

        val bytes = ByteArray(SessionId.SIZE)
        val random = SecureRandom()

        do {
            random.nextBytes(bytes)
        } while (bytes.all { it.toInt() == 0 })

        preferences.edit()
            .putString(
                KEY_DEVICE_ID,
                Base64.encodeToString(bytes, Base64.NO_WRAP),
            )
            .apply()

        return SessionId.fromBytes(bytes)
    }

    private companion object {
        const val PREFERENCES_NAME = "natsx_identity"
        const val KEY_DEVICE_ID = "controller_device_id"
    }
}
