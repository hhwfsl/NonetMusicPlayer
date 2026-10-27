package app.nonetmusicplayer.android

import android.content.Intent
import android.app.NotificationManager
import android.os.ParcelFileDescriptor
import androidx.activity.compose.setContent
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.playback.PlayerViewModel
import app.nonetmusicplayer.android.ui.MusicApp
import app.nonetmusicplayer.android.updates.*
import kotlinx.coroutines.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicInteger

/** 使用同签名、更高版本的隔离 QA APK；验证系统安装界面但不执行安装。 */
@RunWith(AndroidJUnit4::class)
@androidx.media3.common.util.UnstableApi
class UpdateFlowTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private fun shell(command: String)=ParcelFileDescriptor.AutoCloseInputStream(InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(command)).bufferedReader().use {it.readText()}
    @Test fun oneStartupCheckAndManualCheckAreIndependent()=runBlocking {
        val app=compose.activity.application as MusicApplication;val checks=AtomicInteger()
        val updates=AppUpdates(app,UpdateSource {checks.incrementAndGet();null})
        compose.runOnUiThread {updates.checkAfterCreate(true)}
        compose.waitUntil(10000){updates.state.value.phase==UpdatePhase.Current}
        assertEquals(1,checks.get())
        compose.runOnUiThread {updates.checkAfterCreate(true)}
        assertEquals(1,checks.get())
        compose.runOnUiThread {updates.checkOnLaunch()};compose.waitUntil(5000){checks.get()==2 && updates.state.value.phase==UpdatePhase.Current}
        assertFalse(updates.state.value.dialog)
        compose.runOnUiThread {updates.check()};compose.waitUntil(5000){checks.get()==3 && updates.state.value.phase==UpdatePhase.Current}
        assertTrue(updates.state.value.dialog)
    }
    @Test fun foregroundBackgroundNotificationCancelAndInstaller(): Unit=runBlocking {
        val activity=compose.activity;val initialIntent=Intent(activity.intent)
        val app=activity.application as MusicApplication
        val file=File(app.getExternalFilesDir(null),"qa/update-fixture.apk")
        Assume.assumeTrue("Provide a same-signer higher-version QA APK at qa/update-fixture.apk",file.isFile)
        Assume.assumeTrue("Allow REQUEST_INSTALL_PACKAGES before starting instrumentation",app.packageManager.canRequestPackageInstalls())
        val info=requireNotNull(app.packageManager.getPackageArchiveInfo(file.path,0))
        val release=AndroidRelease(requireNotNull(info.versionName),"QA release notes · selectable and read-only","https://github.com/hhwfsl/NonetMusicPlayer/releases/download/android-v${info.versionName}/Nonet-${info.versionName}.apk",file.length(),file.inputStream().use {MessageDigest.getInstance("SHA-256").digest(it.readBytes()).joinToString(""){b->"%02x".format(b)}})
        val transfer=PausedTransfer(file);val updates=AppUpdates(app,UpdateSource {release},transfer)
        val field=MusicApplication::class.java.getDeclaredField("updates").apply {isAccessible=true};val original=field.get(app)
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        field.set(app,updates)
        try {
            shell("pm grant ${app.packageName} android.permission.POST_NOTIFICATIONS")
            compose.runOnUiThread {compose.activity.setContent {MusicApp(vm)};updates.check()}
            compose.waitUntil(10000){updates.state.value.phase==UpdatePhase.Available}
            compose.onNodeWithTag("update_notes").assertTextContains("QA release notes",substring=true)
            compose.onNodeWithTag("update_download").performClick()
            compose.waitUntil(10000){updates.state.value.received>0}
            compose.onNodeWithTag("update_progress").assertExists()
            compose.onNodeWithTag("update_background").performClick()
            compose.waitUntil(3000){!updates.state.value.dialog}
            val notifications=app.getSystemService(NotificationManager::class.java)
            compose.waitUntil(5000){notifications.activeNotifications.any {it.id==UpdateNotifications.ID && it.notification.extras.getString("nonet.update.transfer")==updates.state.value.transferId && it.notification.extras.getInt("android.progress")>0}}
            val progress=notifications.activeNotifications.single {it.id==UpdateNotifications.ID}.notification
            progress.actions.single().actionIntent.send()
            compose.waitUntil(5000){updates.state.value.phase==UpdatePhase.Idle}
            assertFalse(File(app.cacheDir,"app-updates/pending.part").exists());assertFalse(updates.apk.exists())
            compose.runOnUiThread {updates.check()};compose.waitUntil(5000){updates.state.value.phase==UpdatePhase.Available}
            compose.runOnUiThread {updates.begin(true)};compose.waitUntil(5000){updates.state.value.received>0}
            compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
            transfer.release.complete(Unit)
            withTimeout(15000){while(updates.state.value.phase!=UpdatePhase.Ready) delay(50)}
            assertTrue(updates.apk.isFile)
            assertEquals(app.packageName,app.packageManager.getPackageArchiveInfo(updates.apk.path,0)?.packageName)
            val install=updates.installIntent();assertEquals("content",install.data?.scheme);assertTrue(install.flags and Intent.FLAG_GRANT_READ_URI_PERMISSION!=0)
            compose.waitUntil(5000){notifications.activeNotifications.any {it.id==UpdateNotifications.ID && !it.isOngoing}}
            shell("am start -n ${app.packageName}/app.nonetmusicplayer.android.MainActivity -a ${UpdateDownloadService.INSTALL}")
            withTimeout(15000) {while(!shell("dumpsys activity activities").lineSequence().any {(it.contains("mResumedActivity") || it.contains("topResumedActivity")) && it.contains("packageinstaller.PackageInstallerActivity",true)}) delay(200)}
            shell("input keyevent KEYCODE_BACK")
        } finally {
            // ActivityScenario 依照最初的 Intent 追踪生命周期，通知 Intent 不应污染夹具清理。
            compose.runOnUiThread {
                updates.consumeInstall();updates.cancel();activity.intent=initialIntent
                field.set(app,original);activity.setContent {MusicApp(vm)}
                activity.startActivity(Intent(initialIntent).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            }
        }
    }
    private class PausedTransfer(private val source: File): ApkTransfer {
        val release=CompletableDeferred<Unit>()
        override fun cancel() { }
        override suspend fun fetch(release: AndroidRelease,file: File,progress: (Long)->Unit) {
            withContext(Dispatchers.IO) {source.inputStream().use {input ->file.outputStream().use {output ->
                val buffer=ByteArray(65536);var total=0L
                while(true) {ensureActive();val n=input.read(buffer);if(n<0) break;output.write(buffer,0,n);total+=n;progress(total)
                    if(total==65536L) this@PausedTransfer.release.await()
                }
            }}}
        }
    }
}
