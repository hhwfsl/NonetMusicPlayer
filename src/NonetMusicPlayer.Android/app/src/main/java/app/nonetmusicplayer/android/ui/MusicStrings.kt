package app.nonetmusicplayer.android.ui

import android.content.Context
import androidx.annotation.StringRes
import androidx.compose.runtime.Composable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.platform.LocalContext

/** 弹窗拥有自己的 Android Context，独立保留应用语言，避免回退到系统语言。 */
internal val LocalMusicContext=staticCompositionLocalOf<Context?> {null}

@Composable
fun musicString(@StringRes id: Int,vararg arguments: Any): String {
    val resources=(LocalMusicContext.current ?: LocalContext.current).resources
    return if(arguments.isEmpty()) resources.getString(id) else resources.getString(id,*arguments)
}
