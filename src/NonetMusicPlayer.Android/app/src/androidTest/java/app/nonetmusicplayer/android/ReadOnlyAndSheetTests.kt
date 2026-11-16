package app.nonetmusicplayer.android

import android.graphics.Color
import android.view.Window
import android.view.WindowManager
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.DialogWindowProvider
import androidx.lifecycle.ViewModelProvider
import androidx.media3.common.util.UnstableApi
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.data.MusicRepository
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.*
import app.nonetmusicplayer.android.playback.PlayerViewModel
import app.nonetmusicplayer.android.ui.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import org.json.JSONArray
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import java.io.ByteArrayInputStream
import java.io.File
import java.util.UUID
import kotlin.math.abs

/** 在独立测试模拟器验证真实菜单权限、原生窗口和遮罩像素，不访问用户设备。 */
@UnstableApi
class ReadOnlyAndSheetTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private fun sample(): Int {
        val bitmap=requireNotNull(InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot())
        return try {bitmap.getPixel(bitmap.width/2,bitmap.height/5)} finally {bitmap.recycle()}
    }
    private fun distance(a: Int,b: Int)=maxOf(abs(Color.red(a)-Color.red(b)),abs(Color.green(a)-Color.green(b)),abs(Color.blue(a)-Color.blue(b)))
    @Test fun outsidePressDoesNotFlashAndDismissesOnce() {
        var shown by mutableStateOf(true);var dismissals=0;var nativeWindow: Window?=null
        compose.activity.setContent {MusicTheme(Preferences(language="en")) {
            Box(Modifier.fillMaxSize().background(androidx.compose.ui.graphics.Color.White)) {
                if(shown) MusicSheet({dismissals++;shown=false}) {
                    val window=(LocalView.current.parent as? DialogWindowProvider)?.window
                    SideEffect {nativeWindow=window}
                    Text("Sheet action",Modifier.padding(24.dp));Spacer(Modifier.height(160.dp))
                }
            }
        }}
        compose.waitForIdle();Thread.sleep(300)
        val before=sample()
        compose.onNodeWithTag("bottom_sheet_scrim").performTouchInput {down(Offset(width/2f,height/5f))}
        compose.waitForIdle();val pressed=sample()
        assertTrue("Outside press changed screen brightness: before=$before pressed=$pressed",distance(before,pressed)<=3)
        assertEquals(0,nativeWindow!!.attributes.flags and WindowManager.LayoutParams.FLAG_DIM_BEHIND)
        assertEquals(0,nativeWindow!!.attributes.windowAnimations)
        compose.mainClock.autoAdvance=false
        compose.onNodeWithTag("bottom_sheet_scrim").performTouchInput {up()}
        var last=pressed
        repeat(16) {
            compose.mainClock.advanceTimeByFrame();compose.waitForIdle();val current=sample()
            assertTrue("Closing darkened the background again",Color.red(current)>=Color.red(last)-3);last=current
        }
        compose.mainClock.autoAdvance=true;compose.waitForIdle()
        compose.onNodeWithTag("bottom_sheet").assertDoesNotExist();assertEquals(1,dismissals)
        compose.runOnIdle {shown=true};compose.waitForIdle()
        compose.mainClock.autoAdvance=false
        repeat(2) {compose.onNodeWithTag("bottom_sheet_scrim").performSemanticsAction(SemanticsActions.OnClick) {it()}}
        compose.mainClock.advanceTimeBy(500);compose.mainClock.autoAdvance=true;compose.waitForIdle()
        assertEquals(2,dismissals)
    }
    @Test fun remoteMetadataWritesAreRejectedButOwnPlaylistCoverIsAllowed()=runBlocking {
        val app=compose.activity.application as MusicApplication;val name="readonly-${UUID.randomUUID()}.db"
        val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO);val repo=MusicRepository(app,scope,name)
        try {
            val track=Track("remote","nonet-lan://pc/r","Remote","Artist","Album",providerId="lan:pc",inLibrary=false)
            repo.storeTrack(track)
            suspend fun rejected(block: suspend ()->Unit) {try {block();fail("Remote edit was accepted")} catch(expected: IllegalArgumentException) {}}
            rejected {repo.setCover("peer:pc:p","/local/changed.jpg")}
            rejected {repo.setCover("album:Album","/local/changed.jpg")}
            rejected {repo.setLyrics(track.id,"none:")}
            rejected {repo.removeEverywhere(setOf(track.id))}
            assertEquals("",repo.state.value.tracks.single().lyrics);assertTrue(repo.state.value.covers.isEmpty())
            repo.addTracks("liked",listOf(track));repo.setCover("playlist:liked","/local/own-cover.jpg")
            assertEquals("/local/own-cover.jpg",repo.state.value.covers["playlist:liked"])
        } finally {scope.cancel();repo.close();app.deleteDatabase(name)}
    }
    @Test fun actualRemotePagesAndPlayerMenusExposeNoSourceEditors()=runBlocking {
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java];val app=vm.app
        val previous=app.repository.state.value.preferences
        val peerId=UUID.randomUUID().toString().replace("-","");val title="Readonly "+peerId.take(6)
        val tone=InstrumentationRegistry.getInstrumentation().context.assets.open("tone.mp3").use {it.readBytes()}
        val (ssl,pin)=PeerSecurity.identity()
        val catalog=JSONObject().put("deviceId",peerId).put("name","Read-only PC").put("deviceKind","desktop").put("revision","one").put("total",1)
            .put("tracks",JSONArray().put(JSONObject().put("id","r").put("title",title).put("artist","Readonly artist").put("album","Readonly album").put("durationMs",12000).put("artwork",false).put("lyrics",true)))
            .put("playlists",JSONArray().put(JSONObject().put("id","p").put("name","Remote read-only list").put("description","Description").put("trackIds",JSONArray().put("r"))))
        val server=PeerServer(peerId,"Read-only PC",ssl,pin,{catalog},{kind,id ->when {
            kind=="audio" && id=="r" ->PeerPayload(ByteArrayInputStream(tone),tone.size.toLong(),"audio/mpeg")
            kind=="lyrics" && id=="r" ->"[00:00]Remote lyrics".toByteArray().let {PeerPayload(ByteArrayInputStream(it),it.size.toLong(),"text/plain")}
            else ->null
        }},approve={true},reverse={true})
        try {
            app.repository.updatePreferences(previous.copy(language="en"))
            compose.runOnUiThread {app.lan.connect(server.invitation("127.0.0.1"))}
            compose.waitUntil(15000) {app.lan.state.value.peers.any {it.id==peerId}}
            compose.onNodeWithTag("navigation_toggle").performClick()
            compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_peer:$peerId:p"))
            compose.onNodeWithTag("drawer_peer:$peerId:p").performClick()
            compose.onNodeWithTag("playlist_page_more").assertDoesNotExist()
            compose.onNodeWithTag("playlist_add_files").assertDoesNotExist()
            val remote=app.repository.state.value.tracks.single {it.title==title}
            compose.onNodeWithTag("track_list").performScrollToNode(hasTestTag("track_${remote.id}"))
            compose.onNode(hasContentDescription("More") and hasAnyAncestor(hasTestTag("track_${remote.id}"))).performClick()
            for(label in listOf("Change cover","Restore cover","Import lyrics","Unlink lyrics","Remove from all playlists","Delete")) compose.onNodeWithText(label).assertDoesNotExist()
            compose.onNodeWithText("Track information").assertExists()
            compose.onNodeWithTag("bottom_sheet_scrim").performClick();compose.waitForIdle()
            compose.waitUntil(15000) {vm.playback.value.ready}
            compose.runOnUiThread {vm.play(remote,listOf(remote),"peer:$peerId:p")}
            compose.waitUntil(15000) {vm.playback.value.playing}
            compose.onNodeWithTag("mini_player").performClick();compose.waitForIdle()
            compose.onNodeWithContentDescription("More").performClick()
            compose.onNodeWithText("Import lyrics").assertDoesNotExist();compose.onNodeWithText("Unlink lyrics").assertDoesNotExist()
            compose.onNodeWithText("Export lyrics").assertExists()
        } finally {
            compose.runOnUiThread {if(vm.playback.value.playing) vm.togglePlay();app.lan.disconnect(peerId);app.lan.setSharing(false)}
            server.close();app.repository.updatePreferences(previous)
        }
        Unit
    }
}
