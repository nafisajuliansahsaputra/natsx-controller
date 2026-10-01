package com.natsx.controller

import android.app.Activity
import android.content.Intent
import android.os.Build
import android.os.Bundle
import com.natsx.controller.service.ControllerService

class UsbAccessoryAttachActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        val serviceIntent =
            Intent(
                this,
                ControllerService::class.java,
            )

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(
                serviceIntent,
            )
        } else {
            @Suppress("DEPRECATION")
            startService(
                serviceIntent,
            )
        }

        startActivity(
            Intent(
                this,
                MainActivity::class.java,
            ).apply {
                addFlags(
                    Intent.FLAG_ACTIVITY_CLEAR_TOP or
                        Intent.FLAG_ACTIVITY_SINGLE_TOP,
                )
            },
        )

        finish()
    }
}
