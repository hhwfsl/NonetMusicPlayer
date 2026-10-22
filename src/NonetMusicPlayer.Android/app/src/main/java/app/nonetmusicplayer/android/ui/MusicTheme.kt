package app.nonetmusicplayer.android.ui

import android.content.res.Configuration
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.*
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.Density
import androidx.core.graphics.ColorUtils
import androidx.core.view.WindowCompat
import androidx.activity.compose.LocalActivity
import androidx.activity.compose.LocalActivityResultRegistryOwner
import app.nonetmusicplayer.android.domain.Preferences
import java.util.Locale
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/** 应用自有颜色与排版契约，页面不依赖 Material Theme 或 Material 组件。 */
data class NonetColors(val primary: Color,val onPrimary: Color,val primaryContainer: Color,val background: Color,val surface: Color,val surfaceVariant: Color,val onSurface: Color,val onSurfaceVariant: Color,val outlineVariant: Color)
private val LocalNonetColors=staticCompositionLocalOf {NonetColors(Color(0xFF527EC9),Color.White,Color(0xFFE9EFF8),Color(0xFFF4F4F4),Color(0xFFFCFCFC),Color(0xFFEDEEF0),Color(0xFF202329),Color(0xFF72767D),Color(0xFFE7E8EA))}
class NonetTypography {
    val bodyLarge=TextStyle(fontSize=15.sp,lineHeight=23.sp)
    val bodyMedium=TextStyle(fontSize=14.sp,lineHeight=21.sp)
    val bodySmall=TextStyle(fontSize=12.sp,lineHeight=18.sp)
    val titleLarge=TextStyle(fontSize=21.sp,lineHeight=28.sp,fontWeight=FontWeight.SemiBold)
    val titleMedium=TextStyle(fontSize=17.sp,lineHeight=24.sp,fontWeight=FontWeight.SemiBold)
    val titleSmall=TextStyle(fontSize=14.sp,lineHeight=21.sp,fontWeight=FontWeight.Medium)
    val headlineSmall=TextStyle(fontSize=24.sp,lineHeight=32.sp,fontWeight=FontWeight.Medium)
    val labelLarge=TextStyle(fontSize=13.sp,lineHeight=19.sp,fontWeight=FontWeight.Medium)
    val labelSmall=TextStyle(fontSize=11.sp,lineHeight=16.sp)
}
object NonetTheme {
    val colorScheme: NonetColors @Composable get()=LocalNonetColors.current
    val typography=NonetTypography()
    object shapes {val extraLarge=RoundedCornerShape(18.dp)}
}

/** 保留原生语义与系统字体，视觉采用平面网页式卡片、紧凑字号和低饱和色彩。 */
@Composable fun MusicTheme(preferences: Preferences, content: @Composable () -> Unit) {
    val dark=when(preferences.theme) { "dark" -> true;"light" -> false;else -> isSystemInDarkTheme() }
    val activity=LocalActivity.current
    val registry=LocalActivityResultRegistryOwner.current
    SideEffect {activity?.let {
        WindowCompat.getInsetsController(it.window,it.window.decorView).apply {
            isAppearanceLightStatusBars=!dark
            isAppearanceLightNavigationBars=!dark
        }
    }}
    val requested=runCatching { Color(android.graphics.Color.parseColor(preferences.accent)) }.getOrDefault(Color(0xFFA895FF))
    val background=if(dark) Color(0xFF141414) else Color(0xFFF4F4F4)
    var primary=requested
    // 小文字与操作颜色至少满足 4.5:1；背景仅轻微染色，不直接铺高饱和度主题色。
    repeat(30) { if(ColorUtils.calculateContrast(primary.toArgb(),background.toArgb())<4.5) primary=lerp(primary,if(dark) Color.White else Color.Black,.06f) }
    val onPrimary=if(primary.luminance()>.179) Color.Black else Color.White
    val scheme=if(dark) NonetColors(primary,onPrimary,lerp(background,requested,.12f),background,Color(0xFF1D1D1D),Color(0xFF252628),Color(0xFFF1F1F1),Color(0xFFB3B5BA),Color(0xFF343536))
    else NonetColors(primary,onPrimary,lerp(Color.White,requested,.1f),background,Color(0xFFFCFCFC),Color(0xFFEDEEF0),Color(0xFF202329),Color(0xFF72767D),Color(0xFFE7E8EA))
    val context=LocalContext.current
    val original=LocalConfiguration.current
    val config=remember(original,preferences.language) { Configuration(original).apply {
        if(preferences.language!="system") setLocale(Locale.forLanguageTag(preferences.language))
    } }
    val localized=remember(context,config) { context.createConfigurationContext(config) }
    val density=LocalDensity.current
    CompositionLocalProvider(LocalActivityResultRegistryOwner provides requireNotNull(registry),LocalActivity provides activity,LocalContext provides localized,LocalConfiguration provides config,LocalMusicContext provides localized,
        LocalDensity provides Density(density.density,density.fontScale*preferences.fontScale)) {
        CompositionLocalProvider(LocalNonetColors provides scheme,LocalContentColor provides scheme.onSurface) {content()}
    }
}
