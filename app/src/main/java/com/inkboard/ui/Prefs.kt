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

    var showRulers: Boolean
        get() = sp.getBoolean("showRulers", true)
        set(v) = sp.edit().putBoolean("showRulers", v).apply()

    var showMinimap: Boolean
        get() = sp.getBoolean("showMinimap", true)
        set(v) = sp.edit().putBoolean("showMinimap", v).apply()

    var penOpacity: Float
        get() = sp.getFloat("penOpacity", 1f)
        set(v) = sp.edit().putFloat("penOpacity", v).apply()

    var stabilizer: Int
        get() = sp.getInt("stabilizer", 2)
        set(v) = sp.edit().putInt("stabilizer", v).apply()

    var pressureCurve: Int
        get() = sp.getInt("pressureCurve", 1)
        set(v) = sp.edit().putInt("pressureCurve", v).apply()

    var taper: Boolean
        get() = sp.getBoolean("taper", true)
        set(v) = sp.edit().putBoolean("taper", v).apply()

    var rotateCanvas: Boolean
        get() = sp.getBoolean("rotateCanvas", true)
        set(v) = sp.edit().putBoolean("rotateCanvas", v).apply()

    /** Most recent first, opaque ARGB. */
    var recentColors: List<Int>
        get() = (sp.getString("recentColors", "") ?: "").split(',').mapNotNull { it.toLongOrNull()?.toInt() }
        set(v) = sp.edit().putString("recentColors", v.take(12).joinToString(",") { (it.toLong() and 0xFFFFFFFFL).toString() }).apply()

    fun pushRecent(c: Int) { recentColors = (listOf(c) + recentColors.filter { it != c }).take(12) }

    var refImageVisible: Boolean
        get() = sp.getBoolean("refImageVisible", false)
        set(v) = sp.edit().putBoolean("refImageVisible", v).apply()

    /** 0 = pan, 1 = erase, 2 = select while the S Pen side button is held. */
    var penButton: Int
        get() = sp.getInt("penButton", 0)
        set(v) = sp.edit().putInt("penButton", v).apply()

    var smartInk: Boolean
        get() = sp.getBoolean("smartInk", true)
        set(v) = sp.edit().putBoolean("smartInk", v).apply()

    var snapGuides: Boolean
        get() = sp.getBoolean("snapGuides", true)
        set(v) = sp.edit().putBoolean("snapGuides", v).apply()
}
