package com.inkboard.ui

import android.content.Context

/** Small persisted settings. */
class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("inkboard", Context.MODE_PRIVATE)

    var onboarded: Boolean
        get() = sp.getBoolean("onboarded", false)
        set(v) = sp.edit().putBoolean("onboarded", v).apply()

    var fingerDraws: Boolean
        get() = sp.getBoolean("fingerDraws", false)
        set(v) = sp.edit().putBoolean("fingerDraws", v).apply()

    var autoShape: Boolean
        get() = sp.getBoolean("autoShape", true)
        set(v) = sp.edit().putBoolean("autoShape", v).apply()

    var recogLang: String
        get() = sp.getString("recogLang", "vi") ?: "vi"
        set(v) = sp.edit().putString("recogLang", v).apply()

    var penColor: Int
        get() = sp.getInt("penColor", 0xFF202124.toInt())
        set(v) = sp.edit().putInt("penColor", v).apply()

    var penWidth: Float
        get() = sp.getFloat("penWidth", 2.5f)
        set(v) = sp.edit().putFloat("penWidth", v).apply()

    var penType: Int
        get() = sp.getInt("penType", 0)
        set(v) = sp.edit().putInt("penType", v).apply()
}
