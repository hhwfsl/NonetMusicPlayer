package app.nonetmusicplayer.android

import android.content.pm.ActivityInfo
import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.net.Uri
import androidx.activity.compose.setContent
import androidx.compose.runtime.*
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.Density
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.data.MusicRepository
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.*
import app.nonetmusicplayer.android.ui.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.first
import org.junit.*
import org.junit.Assert.*
import java.io.File
import java.util.UUID
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.abs

/** 独立测试设备上操作真实裁剪手势、保存与连接歌单；使用自产图片和隔离资料库。 */
@UnstableApi
class MobileCropAndLanTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private val saved=AtomicReference<String?>(null)
    private fun source(): File {
        val file=File(compose.activity.cacheDir,"crop-${UUID.randomUUID()}.png")
        val bitmap=Bitmap.createBitmap(800,400,Bitmap.Config.ARGB_8888)
        for(y in 0 until bitmap.height) for(x in 0 until bitmap.width) bitmap.setPixel(x,y,Color.rgb(30+x*210/800,60+y*120/400,130))
        file.outputStream().use {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)};bitmap.recycle();return file
    }
    private fun showCrop(file: File,ratio: Float,fontScale: Float=1f) {
        saved.set(null)
        compose.activity.setContent {MusicTheme(Preferences(language="en")) {
            val density=LocalDensity.current
            CompositionLocalProvider(LocalDensity provides Density(density.density,fontScale)) {
                var shown by remember {mutableStateOf(true)}
                if(shown) ImageCropper(Uri.fromFile(file),ratio,{shown=false},{saved.set(it)},{throw AssertionError("Image error")})
            }
        }}
        compose.waitUntil(10000) {compose.onAllNodes(hasTestTag("crop_save") and isEnabled()).fetchSemanticsNodes().isNotEmpty()}
    }
    private fun screenshot(tag: String,name: String): Bitmap {
        val bitmap=compose.onNodeWithTag(tag).captureToImage().asAndroidBitmap()
        val folder=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        File(folder,name).outputStream().use {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)};return bitmap
    }
    @Test fun fixedFrameClipsExtremeGesturesAndExportMatchesPreview() {
        val file=source();val original=file.readBytes();showCrop(file,1f)
        val image=compose.onNodeWithTag("crop_image")
        repeat(5) {image.performTouchInput {swipe(centerRight-Offset(6f,0f),centerLeft+Offset(6f,0f),180)}}
        image.performTouchInput {pinch(start0=center-Offset(25f,0f),end0=centerLeft+Offset(8f,0f),start1=center+Offset(25f,0f),end1=centerRight-Offset(8f,0f),durationMillis=400)}
        assertTrue("Pinch must enlarge the image",compose.onNodeWithTag("crop_zoom").fetchSemanticsNode().config[SemanticsProperties.ProgressBarRangeInfo].current>1.1f)
        compose.onNodeWithTag("crop_zoom").performSemanticsAction(SemanticsActions.SetProgress) {it(4f)}
        repeat(5) {image.performTouchInput {swipe(topCenter+Offset(0f,5f),bottomCenter-Offset(0f,5f),180)}}
        image.performTouchInput {pinch(start0=centerLeft+Offset(8f,0f),end0=center-Offset(3f,0f),start1=centerRight-Offset(8f,0f),end1=center+Offset(3f,0f),durationMillis=400)}
        assertEquals("Pinch must shrink back to the cover limit",1f,compose.onNodeWithTag("crop_zoom").fetchSemanticsNode().config[SemanticsProperties.ProgressBarRangeInfo].current,.05f)
        compose.waitForIdle()
        val stage=screenshot("crop_stage","crop-stage.png");assertEquals(Color.BLACK,stage.getPixel(2,stage.height/2));stage.recycle()
        val preview=screenshot("crop_image","crop-selected.png");screenshot("crop_dialog","crop-portrait.png").recycle()
        compose.onNodeWithTag("crop_save").assertIsDisplayed().performClick();compose.waitUntil(10000) {saved.get()!=null}
        val result=BitmapFactory.decodeFile(saved.get());assertEquals(1024,result.width);assertEquals(1024,result.height)
        for((x,y) in listOf(.17f to .22f,.44f to .54f,.79f to .81f)) {
            val a=preview.getPixel((x*preview.width).toInt(),(y*preview.height).toInt());val b=result.getPixel((x*result.width).toInt(),(y*result.height).toInt())
            assertTrue(abs(Color.red(a)-Color.red(b))<=8 && abs(Color.green(a)-Color.green(b))<=8)
            assertEquals(255,Color.alpha(a));assertTrue(Color.red(b)>10)
        }
        assertArrayEquals(original,file.readBytes());preview.recycle();result.recycle()
    }
    @Test fun landscapeLargeFontKeepsFrameAndButtonsInsideDialog() {
        compose.activity.requestedOrientation=ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
        compose.waitUntil(10000) {compose.activity.resources.configuration.orientation==Configuration.ORIENTATION_LANDSCAPE}
        showCrop(source(),2.4f,2f)
        compose.onNodeWithTag("crop_save").assertIsDisplayed();compose.onNodeWithTag("crop_cancel").assertIsDisplayed()
        val frame=compose.onNodeWithTag("crop_frame").fetchSemanticsNode().boundsInRoot
        val stage=compose.onNodeWithTag("crop_stage").fetchSemanticsNode().boundsInRoot
        assertTrue(frame.left>=stage.left && frame.right<=stage.right && frame.top>=stage.top && frame.bottom<=stage.bottom)
        assertEquals(2.4f,frame.width/frame.height,.02f)
        screenshot("crop_dialog","crop-landscape-large-font.png").recycle()
        compose.onNodeWithTag("crop_save").performClick();compose.waitUntil(10000) {saved.get()!=null}
        val result=BitmapFactory.decodeFile(saved.get());assertEquals(2.4f,result.width.toFloat()/result.height,.005f);result.recycle()
    }
    @Test fun remotePlaylistsAndGroupsFollowConnectionState() {
        var peers by mutableStateOf(listOf(NearbyDevice("pc","Office PC",listOf(NearbyPlaylist("p","Remote list","Description",listOf("remote"))),listOf("remote"),"desktop")))
        var route="";var groups=false
        val state=LibraryState(tracks=listOf(Track("remote","nonet-lan://pc/r","Remote song","Remote artist","Remote album",providerId="lan:pc",inLibrary=false)))
        compose.activity.setContent {MusicTheme(Preferences(language="en")) {PlaylistGrid(state,{},{},{},{_,_->},peers,{route=it})}}
        compose.onNodeWithTag("playlist_grid").performScrollToNode(hasTestTag("playlist_card_peer:pc:p"))
        compose.onNodeWithTag("playlist_card_peer:pc:p").performClick();assertEquals("peer:pc:p",route)
        screenshot("playlist_grid","remote-playlist-grid.png").recycle()
        compose.runOnIdle {peers=emptyList()};compose.onNodeWithTag("playlist_card_peer:pc:p").assertDoesNotExist()
        var ids by mutableStateOf(setOf("remote"))
        compose.activity.setContent {MusicTheme(Preferences(language="en")) {GroupGrid(state,false,ids) {groups=true}}}
        compose.onNodeWithText("Remote album").assertIsDisplayed().performClick();assertTrue(groups)
        compose.runOnIdle {ids=emptySet()};compose.onNodeWithText("Remote album").assertDoesNotExist()
    }
    @Test fun unfavoritedRemoteListeningSurvivesDisconnectAndRestart()=runBlocking {
        val app=compose.activity.application as MusicApplication;val name="remote-stats-${UUID.randomUUID()}.db"
        val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO);var repository=MusicRepository(app,scope,name)
        try {
            val remote=Track("remote","nonet-lan://pc/r","Remote song",providerId="lan:pc",inLibrary=false)
            val unseen=remote.copy(id="unseen");val temporary=remote.copy(id="temporary",uri="file://test",providerId="")
            repository.storeTracks(listOf(remote,unseen,temporary));repository.recordStarted(remote.id)
            repository.recordDuration(remote.id,5000);repository.recordStarted(temporary.id);repository.recordDuration(temporary.id,5000)
            assertEquals(5000L,repository.state.value.statistics.sumOf {it.durationMs});assertEquals(1,repository.state.value.statistics.sumOf {it.plays})
            repository.removePeerEphemeral("pc")
            assertTrue(repository.state.value.tracks.any {it.id==remote.id && !it.inLibrary});assertFalse(repository.state.value.tracks.any {it.id==unseen.id})
            assertTrue(repository.state.value.playlists.all {remote.id !in it.trackIds})
            repository.close();repository=MusicRepository(app,scope,name)
            val restored=withTimeout(5000) {repository.state.first {it.tracks.isNotEmpty()}}
            assertEquals(5000L,restored.statistics.sumOf {it.durationMs});assertEquals(5000L,restored.hourlyListening.sumOf {it.durationMs})
        } finally {scope.cancel();repository.close();app.deleteDatabase(name)}
    }
}
