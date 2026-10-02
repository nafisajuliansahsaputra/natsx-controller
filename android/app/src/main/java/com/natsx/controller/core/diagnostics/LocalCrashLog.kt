package com.natsx.controller.core.diagnostics

import android.content.Context
import android.os.Process
import java.io.File
import java.nio.charset.StandardCharsets
import java.time.Instant

object LocalCrashLog {
    private const val MAX_LOG_BYTES =
        256L * 1024L
    private const val MAX_ARCHIVES =
        3
    private const val MAX_MESSAGE_CHARS =
        2048
    private const val MAX_STACK_CHARS =
        32 * 1024

    private val gate = Any()

    @Volatile
    private var installed = false

    fun install(
        context: Context,
    ) {
        synchronized(gate) {
            if (installed) {
                return
            }

            val applicationContext =
                context.applicationContext
            val previous =
                Thread.getDefaultUncaughtExceptionHandler()

            Thread.setDefaultUncaughtExceptionHandler {
                    thread,
                    throwable,
                ->
                write(
                    applicationContext,
                    thread.name,
                    throwable,
                )

                if (previous != null) {
                    previous.uncaughtException(
                        thread,
                        throwable,
                    )
                } else {
                    Process.killProcess(
                        Process.myPid(),
                    )
                }
            }

            installed = true
        }
    }

    private fun write(
        context: Context,
        threadName: String,
        throwable: Throwable,
    ) {
        try {
            synchronized(gate) {
                val directory =
                    File(
                        context.filesDir,
                        "diagnostics",
                    )

                if (
                    !directory.exists() &&
                    !directory.mkdirs()
                ) {
                    return
                }

                val file =
                    File(
                        directory,
                        "android-crash.log",
                    )

                rotateIfNeeded(
                    file,
                )

                val message =
                    (throwable.message ?: "")
                        .replace(
                            '\r',
                            ' ',
                        )
                        .replace(
                            '\n',
                            ' ',
                        )
                        .take(
                            MAX_MESSAGE_CHARS,
                        )

                val stack =
                    throwable
                        .stackTraceToString()
                        .take(
                            MAX_STACK_CHARS,
                        )

                val entry =
                    buildString {
                        append('[')
                        append(
                            Instant.now()
                                .toString(),
                        )
                        append(
                            "] uncaught",
                        )
                        appendLine()
                        append(
                            "Thread: ",
                        )
                        appendLine(
                            threadName.take(128),
                        )
                        append(
                            "Exception: ",
                        )
                        appendLine(
                            throwable.javaClass.name,
                        )
                        append(
                            "Message: ",
                        )
                        appendLine(
                            message,
                        )
                        appendLine(
                            stack,
                        )
                        appendLine(
                            "---",
                        )
                    }

                file.appendText(
                    entry,
                    StandardCharsets.UTF_8,
                )
            }
        } catch (_: Throwable) {
            // Crash diagnostics must never interfere with process termination.
        }
    }

    private fun rotateIfNeeded(
        file: File,
    ) {
        if (
            !file.exists() ||
            file.length() <
            MAX_LOG_BYTES
        ) {
            return
        }

        val oldest =
            File(
                file.parentFile,
                file.name +
                    "." +
                    MAX_ARCHIVES,
            )

        if (oldest.exists()) {
            oldest.delete()
        }

        for (
            index in
                (MAX_ARCHIVES - 1)
                    downTo 1
        ) {
            val source =
                File(
                    file.parentFile,
                    file.name +
                        "." +
                        index,
                )

            if (source.exists()) {
                source.renameTo(
                    File(
                        file.parentFile,
                        file.name +
                            "." +
                            (index + 1),
                    ),
                )
            }
        }

        file.renameTo(
            File(
                file.parentFile,
                file.name +
                    ".1",
            ),
        )
    }
}
