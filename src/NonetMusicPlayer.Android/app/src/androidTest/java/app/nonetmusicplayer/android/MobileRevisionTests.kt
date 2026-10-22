package app.nonetmusicplayer.android

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.*
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.ui.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import kotlin.math.abs

/** 使用独立 Compose 状态验证本次手势、首次歌词定位和自绘控件，不写入用户资料库。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class MobileRevisionTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private val preferences=Preferences(language="en",theme="light")
    private fun capture(name: String) {
        val dir=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        val bitmap=compose.onRoot().captureToImage().asAndroidBitmap()
        File(dir,name+".png").outputStream().use {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)}
    }
    @Test fun swipeDrawerAndFullSourceIcon() {
        compose.activity.setContent {MusicTheme(preferences) {LibraryShell(LibraryState(),PlaybackState(),"music","Home",false,{},{},{},{},{},{},{}) {Box(Modifier.fillMaxSize())}}}
        compose.onNodeWithTag("library_swipe_area").performTouchInput {swipe(Offset(width*.2f,height*.4f),Offset(width*.8f,height*.4f),400)}
        compose.onNodeWithTag("drawer_app_icon").assertIsDisplayed();compose.onAllNodesWithText("View all").assertCountEquals(0);capture("beta2-drawer")
        compose.onNodeWithTag("library_swipe_area").performTouchInput {swipe(Offset(width*.6f,height*.4f),Offset(width*.15f,height*.4f),400)}
        compose.waitUntil(5000) {compose.onAllNodesWithTag("drawer_app_icon").fetchSemanticsNodes().isEmpty()}
        compose.onNodeWithTag("navigation_toggle").assertIsDisplayed()
        val options=BitmapFactory.Options().apply {inJustDecodeBounds=true}
        BitmapFactory.decodeResource(compose.activity.resources,R.drawable.app_icon,options)
        assertTrue(options.outWidth>=1024 && options.outWidth==options.outHeight)
        val drawable=compose.activity.packageManager.getApplicationIcon(compose.activity.packageName)
        val bitmap=Bitmap.createBitmap(432,432,Bitmap.Config.ARGB_8888);drawable.setBounds(0,0,432,432);drawable.draw(Canvas(bitmap))
        val dir=File(compose.activity.getExternalFilesDir(null),"qa");File(dir,"beta2-launcher-icon.png").outputStream().use {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)};bitmap.recycle()
    }
    @Test fun playlistHasOneFooterButton() {
        val state=LibraryState(playlists=listOf(Playlist("liked",""),Playlist("work","Evening playlist")),preferences=preferences)
        compose.activity.setContent {MusicTheme(preferences) {PlaylistGrid(state,{},{},{},{_,_->})}}
        val button=compose.onNodeWithTag("playlist_more_liked");button.assertIsDisplayed()
        compose.onAllNodesWithContentDescription("Reorder").assertCountEquals(0)
        assertTrue(button.fetchSemanticsNode().boundsInRoot.bottom>compose.onNodeWithText("Liked").fetchSemanticsNode().boundsInRoot.top)
        capture("beta2-playlist-cards")
    }
    @Test fun lyricsCenterBeforeDisplayAndOnReentry() {
        val lines=List(160) {i ->LyricLine(i*3000L,"Line $i "+if(i%4==0) "A long lyric that wraps across multiple visual lines without changing its timestamp" else "music",translation=if(i%2==0) "Translation $i" else "")}
        var show by mutableStateOf(true);var position by mutableLongStateOf(144000)
        compose.activity.setContent {MusicTheme(preferences) {if(show) LyricView(lines,position,{},Modifier.fillMaxSize()) {show=false} else Box(Modifier.fillMaxSize().testTag("artwork_returned"))}}
        fun centered(index: Int) {
            compose.waitUntil(5000) {
                val item=compose.onAllNodesWithTag("lyric_line_$index").fetchSemanticsNodes().singleOrNull()
                val view=compose.onAllNodesWithTag("lyrics_view").fetchSemanticsNodes().singleOrNull()
                item!=null && view!=null && abs(item.boundsInRoot.center.y-view.boundsInRoot.center.y)<3f
            }
        }
        centered(48);capture("beta2-lyrics-centered")
        repeat(3) {compose.runOnUiThread {show=false};compose.waitForIdle();compose.runOnUiThread {show=true};centered(48)}
        compose.runOnUiThread {position=147000};centered(49)
        compose.onNodeWithTag("lyrics_view").performTouchInput {swipeUp()}
        assertEquals(147000L,position)
        assertTrue(compose.onAllNodes(isSelected()).fetchSemanticsNodes().all {it.config.getOrNull(SemanticsProperties.TestTag)=="lyric_line_49"})
        compose.onNodeWithTag("lyrics_view").performClick();compose.onNodeWithTag("artwork_returned").assertExists()
    }
    @Test fun roundSliderHasContinuousTrackAndSeeks() {
        var value by mutableFloatStateOf(.35f);var finished=false
        compose.activity.setContent {MusicTheme(preferences) {Surface(Modifier.fillMaxSize()) {RoundSlider(value,{value=it},Modifier.fillMaxWidth().testTag("round-slider"),onValueChangeFinished={finished=true})}}}
        val slider=compose.onNodeWithTag("round-slider");val bitmap=slider.captureToImage().asAndroidBitmap()
        val middle=bitmap.height/2;val background=bitmap.getPixel(bitmap.width/2,0)
        for(x in bitmap.width/10 until bitmap.width*9/10) assertNotEquals("Gap at $x",background,bitmap.getPixel(x,middle))
        slider.performSemanticsAction(SemanticsActions.SetProgress) {it(.7f)}
        assertEquals(.7f,value,.001f);assertTrue(finished)
        slider.performTouchInput {swipe(Offset(width*.7f,height/2f),Offset(width*.2f,height/2f),400)}
        assertTrue(value<.3f);capture("beta2-slider")
    }
    @Test fun playerArtworkAndLyricsToggleBothWays() {
        val track=Track("cover-test","file:///fixture.wav","Artwork and lyrics")
        val state=PlaybackState(track=track,positionMs=12000,durationMs=60000)
        compose.activity.setContent {MusicTheme(preferences) {PlayerScreen(state,"repeat-all",false,List(20) {LyricLine(it*1000L,"Text $it")},{},{},{},{},{},{},{},{})}}
        compose.onNodeWithTag("player_artwork").performClick();compose.onNodeWithTag("lyrics_view").assertExists()
        compose.onNodeWithTag("lyrics_view").performClick();compose.onNodeWithTag("player_artwork").assertIsDisplayed()
    }
    @Test fun artworkStaysCenteredThroughoutCrossfade() {
        val track=Track("animation-test","file:///fixture.wav","Transition")
        compose.activity.setContent {MusicTheme(preferences) {PlayerScreen(PlaybackState(track=track,positionMs=9000,durationMs=60000),"repeat-all",false,List(20) {LyricLine(it*1000L,"Line $it")},{},{},{},{},{},{},{},{})}}
        val before=compose.onNodeWithTag("player_artwork").fetchSemanticsNode().boundsInRoot
        compose.mainClock.autoAdvance=false
        compose.onNodeWithTag("player_artwork").performClick()
        repeat(5) {
            compose.mainClock.advanceTimeBy(32);compose.waitForIdle()
            val frame=compose.onNodeWithTag("player_artwork").fetchSemanticsNode().boundsInRoot
            assertEquals(before.left,frame.left,.5f);assertEquals(before.top,frame.top,.5f)
            assertEquals(before.width,frame.width,.5f);assertEquals(before.height,frame.height,.5f)
        }
        compose.mainClock.advanceTimeBy(350);compose.waitForIdle();compose.onNodeWithTag("player_artwork").assertDoesNotExist()
        compose.onNodeWithTag("lyrics_view").performClick();compose.mainClock.advanceTimeBy(32);compose.waitForIdle()
        val returning=compose.onNodeWithTag("player_artwork").fetchSemanticsNode().boundsInRoot
        assertEquals(before.left,returning.left,.5f);assertEquals(before.top,returning.top,.5f)
        compose.mainClock.autoAdvance=true
    }
    @Test fun longPressWholeSongReordersAndActionsReachRightEdge() {
        val tracks=List(18) {Track("drag-$it","file:///fixture.wav","Song $it",artist="Artist")}
        var order by mutableStateOf(tracks);var moved=0;var plays=0
        compose.activity.setContent {MusicTheme(preferences) {
            TrackListPage("Playlist","",order,LibraryState(),"", "test",{_,_->plays++},{},{},{},{},{},onMove={track,delta ->val changed=order.toMutableList();val from=changed.indexOf(track);changed.add((from+delta).coerceIn(0,changed.lastIndex),changed.removeAt(from));order=changed;moved++})
        }}
        compose.onNodeWithTag("track_list").performScrollToNode(hasTestTag("track_drag-2"))
        compose.onAllNodesWithContentDescription("Reorder").assertCountEquals(0)
        val row=compose.onNodeWithTag("track_drag-2");val rect=row.fetchSemanticsNode().boundsInRoot
        // 使用未合并树定位实际行尾按钮，保留不少于 48dp 的可触摸区域。
        val buttons=compose.onAllNodes(hasContentDescription("More"),useUnmergedTree=true).fetchSemanticsNodes().filter {it.boundsInRoot.center.y in rect.top..rect.bottom}
        assertTrue(buttons.isNotEmpty());assertTrue(abs(buttons.first().boundsInRoot.right-rect.right)<80)
        val host=compose.onNodeWithTag("song_reorder_host");val hostRect=host.fetchSemanticsNode().boundsInRoot
        host.performTouchInput {down(Offset(rect.left-hostRect.left+rect.width*.4f,rect.center.y-hostRect.top))}
        // 长按协程按 Compose 时钟运行；只改变触摸事件时间戳不能代替真实经过的长按时间。
        compose.mainClock.advanceTimeBy(750)
        host.performTouchInput {moveBy(Offset(0f,rect.height*2.2f),200);up()}
        try {compose.waitUntil(5000) {moved==1}} catch(error: Throwable) {throw AssertionError("moved=$moved plays=$plays order=${order.take(7).map {it.id}} row=$rect host=$hostRect",error)}
        assertEquals(0,plays);assertTrue(order.indexOf(tracks[2])>2)
        capture("beta3-song-drag")
    }
}
