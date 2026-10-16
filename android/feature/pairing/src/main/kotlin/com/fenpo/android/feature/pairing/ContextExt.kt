package com.fenpo.android.feature.pairing

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper

/** The activity behind a Compose context, for APIs that need one (permission rationale, Android 12 CDM). */
internal tailrec fun Context.findActivity(): Activity? =
    when (this) {
        is Activity -> this
        is ContextWrapper -> baseContext.findActivity()
        else -> null
    }
