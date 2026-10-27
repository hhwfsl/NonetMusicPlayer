package app.nonetmusicplayer.android.domain

import java.util.Locale

/** Android 颜色顺序统一为 AARRGGBB；六位输入表示完全不透明，绝不交换 ARGB 与 RGBA。 */
object AccentColor {
    fun parse(raw: String): Long? {
        val text=raw.trim()
        if(!Regex("#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?").matches(text)) return null
        val value=text.substring(1).toLongOrNull(16) ?: return null
        return if(text.length==7) value or 0xFF000000L else value
    }
    fun format(value: Long): String=String.format(Locale.ROOT,"#%08X",value and 0xFFFFFFFFL)
    fun channels(alpha: Int,red: Int,green: Int,blue: Int): Long? {
        if(listOf(alpha,red,green,blue).any {it !in 0..255}) return null
        return (alpha.toLong() shl 24) or (red.toLong() shl 16) or (green.toLong() shl 8) or blue.toLong()
    }
}
