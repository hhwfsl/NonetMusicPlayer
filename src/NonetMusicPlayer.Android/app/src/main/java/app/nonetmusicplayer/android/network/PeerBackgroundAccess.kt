package app.nonetmusicplayer.android.network

import android.content.Context
import android.content.Intent
import android.os.PowerManager
import android.provider.Settings
import androidx.core.net.toUri

/** 实时局域网音频无法由云推送替代；Doze 豁免始终交给系统让用户确认，不自行修改名单。 */
internal object PeerBackgroundAccess {
    fun allowed(context: Context)=context.getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(context.packageName)
    fun settingsIntent(context: Context): Intent {
        val packageUri="package:${context.packageName}".toUri()
        if(!allowed(context)) {
            val request=Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,packageUri)
            if(request.resolveActivity(context.packageManager)!=null) return request
        }
        // 已允许时仍可进入本应用设置处理厂商后台策略；不跳转到难以寻找应用的全局列表。
        return Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,packageUri)
    }
}
