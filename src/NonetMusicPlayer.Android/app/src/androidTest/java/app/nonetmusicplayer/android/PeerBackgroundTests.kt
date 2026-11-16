package app.nonetmusicplayer.android

import android.os.ParcelFileDescriptor
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.domain.Track
import app.nonetmusicplayer.android.network.*
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import java.io.File
import java.util.UUID

/** 使用真实共享服务与 HTTPS 客户端，在独立模拟器验证手机只提供音频时的后台租约。 */
@androidx.media3.common.util.UnstableApi
class PeerBackgroundTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private fun shell(command: String)=ParcelFileDescriptor.AutoCloseInputStream(
        InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(command)).bufferedReader().use {it.readText()}
    private fun hasLease()=shell("dumpsys power").lineSequence().any {it.contains("Nonet:lan-connection") && it.contains("PARTIAL_WAKE_LOCK")}

    @Test fun backgroundSettingsRequestTargetsOnlyThisApp() {
        val app=compose.activity.application as MusicApplication
        val intent=PeerBackgroundAccess.settingsIntent(app)
        assertEquals("package:${app.packageName}",intent.data.toString())
        assertEquals(if(PeerBackgroundAccess.allowed(app)) android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS
            else android.provider.Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,intent.action)
        assertNotNull(intent.resolveActivity(app.packageManager))
        assertEquals(android.content.pm.PackageManager.PERMISSION_GRANTED,app.checkSelfPermission(android.Manifest.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS))
    }

    @Test fun sourceOnlySessionStreamsInBackgroundAndReleasesProtection()=runBlocking {
        val app=compose.activity.application as MusicApplication
        val unique=UUID.randomUUID().toString();val remoteId=unique.replace("-","")
        val audio=InstrumentationRegistry.getInstrumentation().context.assets.open("tone.mp3").use {it.readBytes()}
        val file=File(app.filesDir,"background-$unique.mp3").apply {writeBytes(audio)}
        val track=Track("background-$unique",android.net.Uri.fromFile(file).toString(),"Background test")
        val playlist=app.repository.createPlaylist("Background $unique")
        val args=InstrumentationRegistry.getArguments()
        val idle=args.getString("exerciseIdle")=="true"
        val exempt=app.getSystemService(android.os.PowerManager::class.java).isIgnoringBatteryOptimizations(app.packageName)
        var backgrounded=false
        try {
            app.repository.addTracks(playlist,listOf(track))
            compose.runOnUiThread {app.lan.setSharing(true)}
            withTimeout(15000) {while(app.lan.state.value.invitations.isEmpty()) delay(30)}
            val parsed=PeerSecurity.parse(app.lan.state.value.invitations.first())
            val invitation=parsed.copy(origin=parsed.origin.newBuilder().host("127.0.0.1").build())
            val http=PeerSecurity.client(invitation)
            // 不发送 duplex，覆盖旧版只读取手机歌曲的桌面客户端，而不是仅覆盖完整双向连接。
            val body=JSONObject().put("key",invitation.key).put("deviceId",remoteId).put("name","Background desktop").toString().toRequestBody("application/json".toMediaType())
            val pair=async(Dispatchers.IO) {http.newCall(Request.Builder().url(invitation.origin.resolve("v1/pair")!!).post(body).build()).execute().use {assertEquals(200,it.code);JSONObject(it.body!!.string())}}
            withTimeout(5000) {while(app.lan.state.value.incoming==null) delay(20)}
            compose.runOnUiThread {app.lan.state.value.incoming!!.let {app.lan.answerConnection(it.requestId,true)}}
            val token=pair.await().getString("token")
            withTimeout(5000) {while(app.lan.state.value.incoming!=null) delay(20)}
            delay(250)
            assertTrue("An approved source-only session must retain its CPU lease",hasLease())
            compose.runOnUiThread {compose.activity.moveTaskToBack(true);backgrounded=true}
            if(idle) {
                // 仅测试设备显式模拟用户允许后台运行后的 Doze；结束时恢复原名单与电源状态。
                if(!exempt) shell("dumpsys deviceidle whitelist +${app.packageName}")
                shell("dumpsys battery unplug");shell("input keyevent KEYCODE_SLEEP");shell("dumpsys deviceidle force-idle")
                assertTrue(shell("dumpsys deviceidle get deep").contains("IDLE"))
            }
            val seconds=(args.getString("backgroundHoldSeconds")?.toIntOrNull() ?: 6).coerceIn(1,180)
            repeat(seconds) {index ->
                if(index%5==0) withContext(Dispatchers.IO) {
                    http.newCall(Request.Builder().url(invitation.origin.resolve("v1/audio/${track.id}")!!).header("Authorization","Bearer $token").header("Range","bytes=1024-2047").build()).execute().use {
                        assertEquals(206,it.code);assertArrayEquals(audio.copyOfRange(1024,2048),it.body!!.bytes())
                    }
                }
                delay(1000)
            }
            assertTrue(hasLease())
            withContext(Dispatchers.IO) {
                http.newCall(Request.Builder().url(invitation.origin.resolve("v1/disconnect")!!).header("Authorization","Bearer $token").post(byteArrayOf().toRequestBody()).build()).execute().use {assertEquals(200,it.code)}
            }
            withTimeout(5000) {while(hasLease()) delay(50)}
            assertTrue(app.lan.state.value.sharing)
        } finally {withContext(NonCancellable) {
            if(idle) {
                shell("dumpsys deviceidle unforce");shell("dumpsys battery reset");shell("input keyevent KEYCODE_WAKEUP")
                if(!exempt) shell("dumpsys deviceidle whitelist -${app.packageName}")
            }
            compose.runOnUiThread {app.lan.setSharing(false)}
            if(backgrounded) {
                shell("am start -W -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -n ${app.packageName}/${MainActivity::class.java.name}")
            }
            app.repository.deletePlaylist(playlist);app.repository.removeEverywhere(setOf(track.id));file.delete()
        }}
    }
}
