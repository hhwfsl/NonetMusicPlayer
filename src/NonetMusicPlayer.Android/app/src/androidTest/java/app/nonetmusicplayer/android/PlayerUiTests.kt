package app.nonetmusicplayer.android

import android.content.pm.ActivityInfo
import androidx.activity.compose.setContent
import app.nonetmusicplayer.android.ui.MusicTheme
import app.nonetmusicplayer.android.ui.ImageCropper
import android.graphics.Bitmap
import android.net.Uri
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.data.LibraryBackup
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.runBlocking
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** 实际媒体服务和 Compose 的端到端回归，不以静态示意图替代页面。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class PlayerUiTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private fun screenshot(name: String) {
        val context=compose.activity.application
        val folder=File(context.getExternalFilesDir(null),"qa").apply {mkdirs()}
        // 无窗口模拟器在切换分辨率后偶发 PixelCopy 超时，回退到系统截图，不跳过界面断言。
        val bitmap=runCatching {compose.onRoot().captureToImage().asAndroidBitmap()}.getOrElse {InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot()}
        File(folder,"$name.png").outputStream().use {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)}
    }
    private fun wave(file: File, seconds: Int) {
        val count=48000*seconds*2
        val header=ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN)
        header.put("RIFF".toByteArray()).putInt(36+count).put("WAVEfmt ".toByteArray()).putInt(16).putShort(1).putShort(1)
            .putInt(48000).putInt(96000).putShort(2).putShort(16).put("data".toByteArray()).putInt(count)
        file.outputStream().use {it.write(header.array());it.write(ByteArray(count))}
    }
    private fun windowScreenshot(name: String) {
        val folder=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot().let {bitmap ->
            File(folder,"$name.png").outputStream().use {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)}
        }
    }
    @Test fun nativePlaybackNavigationLocalesAndResponsivePlayer() {
        val app=compose.activity.application as MusicApplication
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        compose.waitUntil(15000) {vm.playback.value.ready}
        val file=File(app.filesDir,"ui-fixture.wav");wave(file,20)
        val first=Track("ui-first",Uri.fromFile(file).toString(),"夜空中最亮的星","逃跑计划","世界",20000)
        val second=Track("ui-second",Uri.fromFile(file).toString(),"オレンジ","7!!","Orange",20000)
        var playlist=""
        runBlocking {
            app.repository.updatePreferences(Preferences(language="en"))
            app.repository.state.value.playlists.filter {it.id!="liked" && it.name=="Android test playlist"}.forEach {app.repository.deletePlaylist(it.id)}
            playlist=app.repository.createPlaylist("Android test playlist")
            app.repository.addTracks(playlist,listOf(second,first))
            app.repository.removeTrack("liked",second.id)
        }
        compose.onNodeWithTag("navigation_toggle").performClick()
        compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_playlists"));compose.onNodeWithTag("drawer_playlists").performClick()
        compose.onNodeWithTag("playlist_grid").performScrollToNode(hasText("Android test playlist"))
        compose.onNodeWithText("Android test playlist").performClick()
        compose.onNodeWithTag("track_ui-first").assertIsDisplayed()
        screenshot("playlist-phone")
        compose.onNodeWithTag("track_ui-first").performClick()
        // 系统权限弹层由设备测试提前授权；播放状态来自真实媒体控制器。
        try {compose.waitUntil(15000) {vm.playback.value.playing && vm.playback.value.positionMs>500}} catch(e: Throwable) {throw AssertionError("Initial playback: ${vm.playback.value}",e)}
        compose.onNodeWithTag("mini_player").performClick()
        compose.onNodeWithContentDescription("Pause").assertIsDisplayed().performClick()
        compose.waitUntil(5000) {!vm.playback.value.playing}
        compose.onNodeWithContentDescription("Play").performClick()
        compose.waitUntil(5000) {vm.playback.value.playing}
        compose.onNodeWithContentDescription("Next track").performClick()
        compose.waitUntil(5000) {vm.playback.value.track?.id==second.id}
        compose.onNodeWithContentDescription("Add to Liked").performClick()
        compose.waitUntil(5000) {app.repository.state.value.playlists.first {it.id=="liked"}.trackIds.contains(second.id)}
        screenshot("player-phone")
        compose.activity.requestedOrientation=ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
        compose.waitUntil(10000) {compose.activity.resources.configuration.orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE}
        compose.waitForIdle();compose.onNodeWithContentDescription("Previous track").assertIsDisplayed();compose.onNodeWithContentDescription("Next track").assertIsDisplayed()
        screenshot("player-landscape")
        compose.activity.requestedOrientation=ActivityInfo.SCREEN_ORIENTATION_PORTRAIT
        compose.waitUntil(10000) {compose.activity.resources.configuration.orientation==android.content.res.Configuration.ORIENTATION_PORTRAIT}
        compose.onNodeWithContentDescription("Back").performClick()
        compose.onNodeWithTag("navigation_toggle").performClick()
        compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_settings"));compose.onNodeWithTag("drawer_settings").performClick()
        screenshot("settings-phone")
        runBlocking {app.repository.updatePreferences(app.repository.state.value.preferences.copy(language="ja"))}
        compose.onAllNodesWithText("設定").onFirst().assertIsDisplayed()
        runBlocking {app.repository.updatePreferences(app.repository.state.value.preferences.copy(language="zh",theme="dark"))}
        compose.onAllNodesWithText("设置").onFirst().assertIsDisplayed();screenshot("settings-dark-zh")
        compose.onNodeWithTag("navigation_toggle").performClick();compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_music"));compose.onNodeWithTag("drawer_music").performClick();screenshot("home-dark-zh")
        compose.onNodeWithText("听歌统计").performClick();screenshot("statistics-phone")
        compose.onNodeWithTag("statistics_list").performScrollToNode(hasClickAction() and hasText(java.time.LocalDate.now().toString()))
        compose.onNode(hasClickAction() and hasText(java.time.LocalDate.now().toString())).performClick()
        compose.waitForIdle();compose.mainClock.advanceTimeBy(500);windowScreenshot("calendar-phone")
        compose.onNodeWithText("取消").assertIsDisplayed().performClick()
        // 原生系统切到后台并灭屏后，音频时钟继续前进；测试结束恢复屏幕。
        val before=vm.playback.value.positionMs
        compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
        val automation=InstrumentationRegistry.getInstrumentation().uiAutomation
        fun shell(command: String) {automation.executeShellCommand(command).use {android.os.ParcelFileDescriptor.AutoCloseInputStream(it).readBytes()}}
        try {
            shell("input keyevent KEYCODE_SLEEP")
            Thread.sleep(1500)
            assertTrue(vm.playback.value.playing);assertTrue(vm.playback.value.positionMs>before || vm.playback.value.positionMs<2000)
        } finally {shell("input keyevent KEYCODE_WAKEUP");shell("wm dismiss-keyguard")}
        compose.runOnUiThread {vm.togglePlay()}
    }
    @Test fun cropImageIsInteractiveAndSavesCopy() {
        val app=compose.activity.application as MusicApplication
        val image=Bitmap.createBitmap(600,400,Bitmap.Config.ARGB_8888).apply {eraseColor(android.graphics.Color.CYAN)}
        val original=File(app.filesDir,"crop-fixture.png")
        original.outputStream().use {image.compress(Bitmap.CompressFormat.PNG,100,it)};image.recycle()
        var saved="";var error=false
        compose.runOnUiThread {
            compose.activity.setContent {
                MusicTheme(Preferences(language="en")) {
                    ImageCropper(Uri.fromFile(original),1f,{},{saved=it},{error=true})
                }
            }
        }
        compose.waitUntil(10000) {runCatching {!compose.onNodeWithText("Save").fetchSemanticsNode().config.contains(androidx.compose.ui.semantics.SemanticsProperties.Disabled)}.getOrDefault(false)}
        compose.onNodeWithText("Save").assertIsDisplayed();windowScreenshot("crop-phone")
        compose.onNodeWithText("Save").performClick()
        compose.waitUntil(10000) {saved.isNotBlank() || error}
        assertFalse(error);assertTrue(original.isFile)
        val bitmap=android.graphics.BitmapFactory.decodeFile(saved);assertEquals(1024,bitmap.width);assertEquals(1024,bitmap.height);bitmap.recycle()
    }
    @Test fun lyricsImportRefreshBackupAndBackgroundStress() {
        val app=compose.activity.application as MusicApplication
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        compose.waitUntil(15000) {vm.playback.value.ready}
        val file=File(app.filesDir,"lyrics-regression.wav");wave(file,90)
        val track=Track("lyrics-regression",Uri.fromFile(file).toString(),"Lyrics test",durationMs=90000)
        var playlist=""
        runBlocking {
            app.repository.updatePreferences(Preferences(language="en"))
            playlist=app.repository.createPlaylist("Lyrics regression")
            app.repository.addTracks(playlist,listOf(track))
        }
        compose.runOnUiThread {vm.play(track,listOf(track),"playlist:$playlist")}
        compose.waitUntil(15000) {vm.playback.value.playing && vm.playback.value.track?.id==track.id}
        val lrc=File(app.filesDir,"manual-import.lrc").apply {writeText("[00:00.000]First lyric\n[00:00.000]Translation\n[00:02.000]Second lyric")}
        val beforeImport=app.repository.state.value.tracks.first {it.id==track.id}.lyrics
        compose.runOnUiThread {vm.importLyrics(track,Uri.fromFile(lrc))}
        compose.waitUntil(5000) {vm.playback.value.track?.lyrics?.isNotBlank()==true && vm.playback.value.track?.lyrics!=beforeImport}
        assertEquals("First lyric",runBlocking {vm.lyricsFor(vm.playback.value.track!!)}.first().text)
        compose.onNodeWithTag("mini_player").performClick()
        compose.onNodeWithText("Lyrics").performClick()
        compose.onNodeWithText("First lyric").assertExists()
        screenshot("lyrics-import")
        val previousPath=vm.playback.value.track!!.lyrics
        lrc.writeText("[00:00.000]Updated lyric")
        compose.runOnUiThread {vm.importLyrics(track,Uri.fromFile(lrc))}
        compose.waitUntil(5000) {vm.playback.value.track?.lyrics!=previousPath}
        compose.waitUntil(5000) {compose.onAllNodesWithText("Updated lyric").fetchSemanticsNodes().isNotEmpty()}
        assertFalse(File(previousPath).exists())
        val before=vm.playback.value.positionMs
        val automation=InstrumentationRegistry.getInstrumentation().uiAutomation
        fun shell(command: String) {automation.executeShellCommand(command).use {android.os.ParcelFileDescriptor.AutoCloseInputStream(it).readBytes()}}
        repeat(6) {
            compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.CREATED)
            compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
            Thread.sleep(500)
            assertTrue("Playback must survive app switching",vm.playback.value.playing)
        }
        shell("input keyevent KEYCODE_SLEEP")
        try {
            Thread.sleep(6000)
            assertTrue(vm.playback.value.playing);assertTrue(vm.playback.value.positionMs>=before+6000)
        } finally {shell("input keyevent KEYCODE_WAKEUP");shell("wm dismiss-keyguard")}
        compose.runOnUiThread {vm.togglePlay()}
        val output=File(app.filesDir,"backup-test.zip")
        runBlocking {
            val backup=LibraryBackup(app,app.repository)
            backup.export(Uri.fromFile(output));app.repository.deletePlaylist(playlist)
            backup.restore(Uri.fromFile(output))
            assertTrue(app.repository.state.value.playlists.any {it.id==playlist})
            assertTrue(File(app.repository.state.value.tracks.first {it.id==track.id}.lyrics).isFile)
            app.repository.removeRecent(setOf(track.id))
            assertTrue(app.repository.state.value.playlists.first {it.id==playlist}.trackIds.contains(track.id))
            val original=app.repository.state.value.tracks
            app.repository.recordDuration(track.id,1000)
            assertSame("Stats must not recreate tracks",original,app.repository.state.value.tracks)
        }
    }
    @Test fun nativeEndSeekSystemControlsAndBrokenQueue() {
        val app=compose.activity.application as MusicApplication
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        compose.waitUntil(15000) {vm.playback.value.ready}
        val file=File(app.filesDir,"playback-fixture.wav");wave(file,20)
        val one=Track("regression-one",Uri.fromFile(file).toString(),"First",durationMs=20000)
        val two=one.copy(id="regression-two",title="Second")
        val broken=one.copy(id="regression-broken",uri=Uri.fromFile(File(app.filesDir,"missing-audio.wav")).toString())
        val brokenTwo=broken.copy(id="regression-broken-two")
        runBlocking {
            app.repository.updatePreferences(app.repository.state.value.preferences.copy(mode="repeat-all"))
            app.repository.storeTracks(listOf(one,two,broken,brokenTwo))
        }
        compose.runOnUiThread {vm.play(one,listOf(one,two))}
        compose.waitUntil(10000) {vm.playback.value.track?.id==one.id && vm.playback.value.playing}
        compose.runOnUiThread {vm.seek(19500)}
        compose.waitUntil(10000) {vm.playback.value.track?.id==two.id && vm.playback.value.playing}
        // 验证 Android 系统控制器，而不只调用应用自定义切歌命令。
        val automation=InstrumentationRegistry.getInstrumentation().uiAutomation
        automation.adoptShellPermissionIdentity("android.permission.MEDIA_CONTENT_CONTROL")
        try {
            val sessions=app.getSystemService(android.media.session.MediaSessionManager::class.java).getActiveSessions(null)
            val system=sessions.first {it.packageName==app.packageName}
            system.transportControls.skipToNext()
            compose.waitUntil(10000) {vm.playback.value.track?.id==one.id}
            system.transportControls.pause()
            compose.waitUntil(5000) {!vm.playback.value.playing}
            system.transportControls.play()
            compose.waitUntil(5000) {vm.playback.value.playing}
        } finally {automation.dropShellPermissionIdentity()}
        compose.runOnUiThread {vm.play(broken,listOf(broken,brokenTwo,two))}
        compose.waitUntil(10000) {vm.playback.value.track?.id==two.id && vm.playback.value.playing}
        compose.runOnUiThread {vm.play(broken,listOf(broken,brokenTwo))}
        compose.waitUntil(10000) {vm.playback.value.track?.id==brokenTwo.id && !vm.playback.value.playing && !vm.playback.value.buffering}
        Thread.sleep(1000)
        assertEquals(brokenTwo.id,vm.playback.value.track?.id);assertFalse(vm.playback.value.playing)
        runBlocking {app.repository.updatePreferences(app.repository.state.value.preferences.copy(mode="repeat-one"))}
        compose.runOnUiThread {vm.play(one,listOf(one,two))}
        compose.waitUntil(10000) {vm.playback.value.playing && vm.playback.value.track?.id==one.id}
        compose.runOnUiThread {vm.seek(19700)}
        compose.waitUntil(10000) {vm.playback.value.track?.id==one.id && vm.playback.value.playing && vm.playback.value.positionMs<2000}
        compose.runOnUiThread {vm.togglePlay()}
    }

    @Test fun tabletNavigationAndSmallPhone() {
        val automation=InstrumentationRegistry.getInstrumentation().uiAutomation
        fun shell(command: String) {automation.executeShellCommand(command).use {android.os.ParcelFileDescriptor.AutoCloseInputStream(it).readBytes()}}
        try {
            shell("wm size 1800x2400");shell("wm density 220")
            compose.waitUntil(10000) {compose.activity.resources.configuration.screenWidthDp>=840}
            compose.onNodeWithTag("navigation_toggle").assertIsDisplayed();screenshot("home-tablet")
            val app=compose.activity.application as MusicApplication
            runBlocking {app.repository.updatePreferences(app.repository.state.value.preferences.copy(language="en"))}
            compose.onNodeWithTag("navigation_toggle").performClick()
            compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_playlists"));compose.onNodeWithTag("drawer_playlists").performClick();screenshot("playlists-tablet")
            shell("wm size 720x1280");shell("wm density 360")
            compose.waitUntil(10000) {compose.activity.resources.configuration.screenWidthDp<=320}
            compose.waitForIdle();windowScreenshot("playlists-small-phone")
            compose.onNodeWithTag("navigation_toggle").performClick()
            compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_settings"));compose.onNodeWithTag("drawer_settings").performClick()
            compose.onAllNodesWithText("Settings").onFirst().assertIsDisplayed();windowScreenshot("settings-small-phone")
        } finally {shell("wm size reset");shell("wm density reset")}
    }
}
