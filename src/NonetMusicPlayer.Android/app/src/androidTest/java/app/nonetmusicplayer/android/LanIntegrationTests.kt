package app.nonetmusicplayer.android

import android.net.Uri
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.runBlocking
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** 一个配对验证两端真正解码；ADB 端口转发模拟可双向访问的同一局域网。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class LanIntegrationTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    @Test fun independentBidirectionalStreaming() {
        val invite=InstrumentationRegistry.getArguments().getString("desktopInvitation") ?: error("desktopInvitation argument is required")
        val app=compose.activity.application as MusicApplication
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        compose.waitUntil(15000) {vm.playback.value.ready}
        val qa=File(app.getExternalFilesDir(null),"qa").apply {mkdirs()}
        val ready=File(qa,"forward-ready");ready.delete()
        val file=File(app.filesDir,"phone-lan-fixture.wav");val count=48000*60*2
        val h=ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        h.put("RIFF".toByteArray()).putInt(count+36).put("WAVEfmt ".toByteArray()).putInt(16).putShort(1).putShort(1).putInt(48000).putInt(96000).putShort(2).putShort(16).put("data".toByteArray()).putInt(count)
        file.outputStream().use {it.write(h.array());it.write(ByteArray(count))}
        val list=runBlocking {
            val id=app.repository.createPlaylist("Phone LAN fixture")
            app.repository.addTracks(id,listOf(Track("phone-lan-fixture",Uri.fromFile(file).toString(),"Phone shared track","Fixture","LAN",60000)));id
        }
        app.lan.setSharing(true)
        try {
            compose.waitUntil(15000) {app.lan.state.value.invitations.isNotEmpty()}
            File(qa,"phone-invitation.txt").writeText(app.lan.state.value.invitations.first())
            compose.waitUntil(60000) {ready.exists()}
            app.lan.connect(invite)
            compose.waitUntil(25000) {app.lan.state.value.peers.isNotEmpty() && !app.lan.state.value.connecting}
            val peer=app.lan.state.value.peers.single();assertEquals("desktop",peer.deviceKind)
            val tracks=app.repository.state.value.tracks.filter {it.providerId=="lan:${peer.id}"}
            assertEquals(2,tracks.size);assertTrue(tracks.all {it.sourceDeviceKind=="desktop"})
            val song=tracks.first {it.title=="Desktop via LAN"}
            // 主机在独立 ADB 进程中强制 Doze 并恢复，不能依赖被休眠的应用线程自行唤醒设备。
            if(InstrumentationRegistry.getArguments().getString("dozeTest")=="true") {
                compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
                File(qa,"ready-for-doze").writeText("READY")
                val complete=File(qa,"doze-complete");val until=android.os.SystemClock.elapsedRealtime()+240000
                while(!complete.exists()) {check(android.os.SystemClock.elapsedRealtime()<until);Thread.sleep(200)}
                assertTrue(app.lan.state.value.peers.any {it.id==peer.id});assertFalse(vm.playback.value.playing)
                app.lan.retryConnections()
                compose.activity.startActivity(android.content.Intent(compose.activity,MainActivity::class.java).setAction(android.content.Intent.ACTION_MAIN).addCategory(android.content.Intent.CATEGORY_LAUNCHER).addFlags(android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
                compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
                compose.waitUntil(25000) {app.lan.state.value.peers.any {it.id==peer.id && it.reachable}}
            }
            // 可选长时间空闲回归：没有播放、界面在后台并熄屏，连接仍须保持到用户明确断开。
            val idleSeconds=InstrumentationRegistry.getArguments().getString("idleSeconds")?.toLongOrNull()?.coerceIn(0,180) ?: 0
            if(idleSeconds>0) {
                val idleAutomation=InstrumentationRegistry.getInstrumentation().uiAutomation
                fun idleShell(command: String) {idleAutomation.executeShellCommand(command).use {android.os.ParcelFileDescriptor.AutoCloseInputStream(it).readBytes()}}
                compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
                try {
                    repeat(idleSeconds.toInt()) {index ->
                        if(index==idleSeconds.toInt()/2) idleShell("input keyevent KEYCODE_SLEEP")
                        Thread.sleep(1000)
                        assertFalse(vm.playback.value.playing)
                        assertTrue(app.lan.state.value.peers.any {it.id==peer.id})
                    }
                } finally {
                    idleShell("input keyevent KEYCODE_WAKEUP");idleShell("wm dismiss-keyguard")
                    compose.activity.startActivity(android.content.Intent(compose.activity,MainActivity::class.java).setAction(android.content.Intent.ACTION_MAIN).addCategory(android.content.Intent.CATEGORY_LAUNCHER).addFlags(android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
                    compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
                }
            }
            compose.runOnUiThread {vm.play(song,tracks,"peer:${peer.id}:test-playlist")}
            compose.waitUntil(20000) {vm.playback.value.playing && vm.playback.value.positionMs>1000}
            assertTrue(runBlocking {vm.lyricsFor(song)}.isNotEmpty())
            // 互听连接和实际 Media3 时钟必须独立于前台界面；桌面仍会定时反向读取手机。
            val before=vm.playback.value.positionMs
            val automation=InstrumentationRegistry.getInstrumentation().uiAutomation
            fun shell(command: String) {automation.executeShellCommand(command).use {android.os.ParcelFileDescriptor.AutoCloseInputStream(it).readBytes()}}
            compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
            try {
                Thread.sleep(7000);shell("input keyevent KEYCODE_SLEEP");Thread.sleep(5000)
                assertTrue(vm.playback.value.playing);assertTrue(vm.playback.value.positionMs>=before+9000)
                assertTrue(app.lan.state.value.peers.any {it.id==peer.id})
            } finally {
                shell("input keyevent KEYCODE_WAKEUP");shell("wm dismiss-keyguard")
                compose.activity.startActivity(android.content.Intent(compose.activity,MainActivity::class.java).setAction(android.content.Intent.ACTION_MAIN).addCategory(android.content.Intent.CATEGORY_LAUNCHER).addFlags(android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
                compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
            }
            runBlocking {app.repository.addTracks("liked",listOf(song))}
            app.lan.disconnect(peer.id)
            compose.waitUntil(10000) {app.lan.state.value.peers.isEmpty() && app.repository.state.value.tracks.none {it.id==tracks.first {t ->t.id!=song.id}.id}}
            compose.waitUntil(5000) {!vm.playback.value.playing}
            assertTrue(app.repository.state.value.playlists.first {it.id=="liked"}.trackIds.contains(song.id))
            File(qa,"phone-disconnected").writeText("PASS")
            // 重连后由桌面主动断开，手机自动移除曲库，不要求返回设备页刷新。
            app.lan.connect(invite);compose.waitUntil(25000) {app.lan.state.value.peers.isNotEmpty() && !app.lan.state.value.connecting}
            File(qa,"request-remote-disconnect").writeText("READY")
            compose.waitUntil(25000) {app.lan.state.value.peers.isEmpty()}
        } finally {
            app.lan.setSharing(false)
            runBlocking {app.repository.deletePlaylist(list)}
        }
    }
}
