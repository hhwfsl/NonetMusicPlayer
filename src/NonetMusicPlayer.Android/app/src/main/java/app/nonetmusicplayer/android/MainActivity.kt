package app.nonetmusicplayer.android

import android.Manifest
import android.content.Intent
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.playback.PlayerViewModel
import app.nonetmusicplayer.android.ui.MusicApp

@UnstableApi
class MainActivity : ComponentActivity() {
    private val model: PlayerViewModel by viewModels()
    private val notificationPermission=registerForActivityResult(ActivityResultContracts.RequestPermission()) { }
    private val unknownSources=registerForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        if(packageManager.canRequestPackageInstalls()) openUpdateInstaller()
        else (application as MusicApplication).updates.installationFailed(R.string.update_install_permission)
    }
    private val installer=registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState);(application as MusicApplication).sleepTimer.onNewTask();enableEdgeToEdge()
        setContent { MusicApp(model) }
        if(savedInstanceState==null) consume(intent)
        // 旋转和系统恢复沿用同一次启动；从启动器再次进入现有进程时在 onNewIntent 检查。
        (application as MusicApplication).updates.checkAfterCreate(savedInstanceState!=null)
    }
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        // 更新通知是一次性动作，不替换正常启动 Intent，避免后续恢复重放安装请求。
        if(intent.action !in setOf(app.nonetmusicplayer.android.updates.UpdateDownloadService.SHOW,app.nonetmusicplayer.android.updates.UpdateDownloadService.INSTALL)) setIntent(intent)
        consume(intent)
        if(intent.action==Intent.ACTION_MAIN && intent.hasCategory(Intent.CATEGORY_LAUNCHER)) (application as MusicApplication).updates.checkOnLaunch()
        if(intent.action==app.nonetmusicplayer.android.updates.UpdateDownloadService.INSTALL && lifecycle.currentState.isAtLeast(androidx.lifecycle.Lifecycle.State.RESUMED)) resumeUpdateInstall()
    }
    override fun onResume() {super.onResume();if((application as MusicApplication).sleepTimer.closing) finishAndRemoveTask() else resumeUpdateInstall()}
    /** 通知返回的安装请求不依赖 Compose 下一帧；先消费标志，避免重复弹出安装器。 */
    private fun resumeUpdateInstall() {
        val state=(application as MusicApplication).updates.state.value
        if(state.phase==app.nonetmusicplayer.android.updates.UpdatePhase.Ready && state.install) installDownloadedUpdate()
    }
    private fun consume(intent: Intent) {
        if(intent.action in setOf(app.nonetmusicplayer.android.updates.UpdateDownloadService.SHOW,app.nonetmusicplayer.android.updates.UpdateDownloadService.INSTALL)) {
            (application as MusicApplication).updates.present(intent.action==app.nonetmusicplayer.android.updates.UpdateDownloadService.INSTALL)
            return
        }
        val uris=when(intent.action) {
            Intent.ACTION_VIEW -> listOfNotNull(intent.data)
            Intent.ACTION_SEND -> listOfNotNull(androidx.core.content.IntentCompat.getParcelableExtra(intent,Intent.EXTRA_STREAM,android.net.Uri::class.java))
            Intent.ACTION_SEND_MULTIPLE -> androidx.core.content.IntentCompat.getParcelableArrayListExtra(intent,Intent.EXTRA_STREAM,android.net.Uri::class.java).orEmpty()
            else -> emptyList()
        }.filter {it.scheme in setOf("content","file")}
        if(uris.isNotEmpty()) {requestNotificationPermission();model.openExternalQueue(uris)}
    }
    fun requestNotificationPermission() {
        val prefs=getSharedPreferences("permissions",MODE_PRIVATE)
        if(Build.VERSION.SDK_INT>=33 && !prefs.getBoolean("notificationRequested",false)) {
            prefs.edit().putBoolean("notificationRequested",true).apply();notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }
    internal fun installDownloadedUpdate() {
        val updates=(application as MusicApplication).updates
        updates.consumeInstall()
        try {
            if(packageManager.canRequestPackageInstalls()) openUpdateInstaller()
            else unknownSources.launch(Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,android.net.Uri.parse("package:$packageName")))
        } catch(_: Exception) {updates.installationFailed(R.string.update_install_failed)}
    }
    private fun openUpdateInstaller() {
        val updates=(application as MusicApplication).updates
        try {installer.launch(updates.installIntent());updates.dismiss();app.nonetmusicplayer.android.updates.UpdateNotifications.clear(this)}
        catch(_: Exception) {updates.installationFailed(R.string.update_install_failed)}
    }
}
