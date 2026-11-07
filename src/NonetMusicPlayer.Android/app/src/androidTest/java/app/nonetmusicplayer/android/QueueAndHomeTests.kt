package app.nonetmusicplayer.android

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.semantics.*
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.NearbyDevice
import app.nonetmusicplayer.android.network.NearbyPlaylist
import app.nonetmusicplayer.android.ui.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File

/** 手指不松开时检查实际行位置，防止只有浮动副本、松手后才换位的假预览。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class QueueAndHomeTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private val preferences=Preferences(language="en",theme="light")
    private val tracks=List(24) {Track("preview-$it","file:///fixture.wav","Song $it",artist="Artist")}
    private fun capture(name: String) {
        val dir=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        File(dir,"queue-home-$name.png").outputStream().use {compose.onRoot().captureToImage().asAndroidBitmap().compress(android.graphics.Bitmap.CompressFormat.PNG,100,it)}
    }
    private fun previewDrag(queue: Boolean,cancelled: Boolean) {
        var order by mutableStateOf(tracks);var moves=0;var plays=0
        val onMove: (Track,Int)->Unit={track,delta ->order=PlaybackOrder.move(order.map {it.id},track.id,delta).map {id ->tracks.first {it.id==id}};moves++}
        compose.activity.setContent {MusicTheme(preferences) {
            if(queue) PlaybackQueueList(order,tracks[0].id,emptyList(),{plays++},{},{},onMove)
            else TrackListPage("Preview","",order,LibraryState(),"","test",{_,_->plays++},{},{},{},{},{},onMove=onMove)
        }}
        val list=compose.onNodeWithTag(if(queue) "playback_queue" else "track_list")
        list.performScrollToNode(hasTestTag("track_preview-1"))
        val row=compose.onNodeWithTag("track_preview-1").fetchSemanticsNode().boundsInRoot
        val neighbor=compose.onNodeWithTag("track_preview-2").fetchSemanticsNode().boundsInRoot
        val hostTag=if(queue) "queue_reorder_host" else "song_reorder_host"
        val host=compose.onNodeWithTag(hostTag);val bounds=host.fetchSemanticsNode().boundsInRoot
        host.performTouchInput {down(Offset(row.left-bounds.left+row.width*.4f,row.center.y-bounds.top))}
        compose.mainClock.advanceTimeBy(750)
        host.performTouchInput {moveBy(Offset(0f,row.height*1.6f),200)}
        compose.mainClock.advanceTimeBy(450);compose.waitForIdle()
        assertEquals("预览不能提前保存",0,moves);assertEquals(tracks,order)
        val shifted=compose.onNodeWithTag("track_preview-2").fetchSemanticsNode().boundsInRoot
        assertTrue("相邻行应在松手前向上让位: $neighbor -> $shifted",shifted.top<neighbor.top-row.height*.6f)
        compose.onNodeWithTag("${hostTag}_preview").assertExists();capture(if(queue) "queue-held" else "playlist-held")
        if(cancelled) host.performTouchInput {cancel()} else host.performTouchInput {up()}
        compose.waitForIdle()
        if(cancelled) {assertEquals(0,moves);assertEquals(tracks,order)}
        else {assertEquals(1,moves);assertTrue(order.indexOf(tracks[1])>1)}
        assertEquals(0,plays)
    }
    @Test fun queuePreviewsBeforeDrop() = previewDrag(true,false)
    @Test fun playlistPreviewsBeforeDrop() = previewDrag(false,false)
    @Test fun cancelRestoresOrder() = previewDrag(true,true)
    @Test fun drawerExpandsAndReordersWithPreview() {
        val original=List(12) {Playlist("drawer-$it","Playlist $it")}
        var playlists by mutableStateOf(original);var moves=0
        compose.activity.setContent {MusicTheme(preferences) {
            LibraryShell(LibraryState(playlists=playlists),PlaybackState(),"music","Home",false,{},{},{},{},{},{},{},
                onMovePlaylist={id,delta ->playlists=PlaybackOrder.move(playlists.map {it.id},id,delta).map {key ->original.first {it.id==key}};moves++}) {Box(Modifier.fillMaxSize())}
        }}
        compose.onNodeWithTag("navigation_toggle").performClick()
        val list=compose.onNodeWithTag("drawer_list")
        list.performScrollToNode(hasTestTag("drawer_expand_playlists"));compose.onNodeWithTag("drawer_playlist:drawer-5").assertDoesNotExist()
        compose.onNodeWithTag("drawer_expand_playlists").performClick()
        list.performScrollToNode(hasTestTag("drawer_expand_playlists"))
        compose.onNodeWithTag("drawer_playlist:drawer-10").assertDoesNotExist()
        list.performScrollToNode(hasTestTag("drawer_playlist:drawer-9"));compose.onNodeWithTag("drawer_playlist:drawer-9").assertIsDisplayed()
        list.performScrollToNode(hasTestTag("drawer_expand_playlists"))
        compose.onNodeWithTag("drawer_expand_playlists").performClick()
        list.performScrollToNode(hasTestTag("drawer_playlist:drawer-10"));compose.onNodeWithTag("drawer_playlist:drawer-10").assertIsDisplayed()
        list.performScrollToNode(hasTestTag("drawer_playlist:drawer-0"))
        val row=compose.onNodeWithTag("drawer_playlist:drawer-0").fetchSemanticsNode().boundsInRoot
        val neighbor=compose.onNodeWithTag("drawer_playlist:drawer-1").fetchSemanticsNode().boundsInRoot
        val host=compose.onNodeWithTag("drawer_reorder_host");val bounds=host.fetchSemanticsNode().boundsInRoot
        host.performTouchInput {down(Offset(row.center.x-bounds.left,row.center.y-bounds.top))}
        compose.mainClock.advanceTimeBy(750)
        host.performTouchInput {moveBy(Offset(0f,row.height*1.7f),200)}
        compose.mainClock.advanceTimeBy(450);compose.waitForIdle()
        assertEquals(0,moves);assertEquals(original,playlists)
        val shifted=compose.onNodeWithTag("drawer_playlist:drawer-1").fetchSemanticsNode().boundsInRoot
        assertTrue("侧栏项目应在松手前让位: $neighbor -> $shifted row=$row",shifted.top<neighbor.top-row.height*.6f)
        capture("drawer-held");host.performTouchInput {up()};compose.waitForIdle()
        assertEquals(1,moves);assertTrue(playlists.indexOf(original[0])>0)
        list.performScrollToNode(hasTestTag("drawer_toggle_playlists"));compose.onNodeWithTag("drawer_toggle_playlists").performClick()
        compose.onNodeWithTag("drawer_playlist:drawer-0").assertDoesNotExist()
        compose.onNodeWithTag("drawer_playlist:drawer-10").assertDoesNotExist()
        compose.onNodeWithTag("drawer_collapse_playlists").assertDoesNotExist()
        compose.onNodeWithTag("drawer_toggle_playlists").performClick()
        list.performScrollToNode(hasTestTag("drawer_playlist:drawer-10"));compose.onNodeWithTag("drawer_playlist:drawer-10").assertIsDisplayed()
    }
    @Test fun homeFitsWithoutVerticalScrolling() {
        val state=LibraryState(tracks=tracks,playlists=List(30) {Playlist("home-$it","Playlist $it",trackIds=listOf(tracks[0].id))},recentIds=tracks.map {it.id},preferences=preferences)
        var available by mutableStateOf(260.dp);var large by mutableStateOf(false);var route=""
        compose.activity.setContent {MusicTheme(preferences.copy(fontScale=if(large) 1.3f else 1f)) {
            Box(Modifier.width(320.dp).height(available)) {MusicHome(state,{route=it},{})}
        }}
        val normalHeights=mutableMapOf<Boolean,Float>()
        for(height in listOf(260,380,520,700)) for(font in listOf(false,true)) {
            compose.runOnUiThread {available=height.dp;large=font};compose.waitForIdle()
            val home=compose.onNodeWithTag("music_overview").fetchSemanticsNode()
            assertNull(home.config.getOrNull(SemanticsActions.ScrollBy))
            compose.onNodeWithTag("home_browse").assertIsDisplayed();compose.onNodeWithTag("home_playlists").assertIsDisplayed()
            val bottom=compose.onNodeWithTag("home_statistics").fetchSemanticsNode().boundsInRoot.bottom
            assertTrue("首页入口被裁剪 height=$height large=$font",bottom<=home.boundsInRoot.bottom)
            val chart=compose.onNodeWithTag("hourly_chart").getUnclippedBoundsInRoot()
            val chartHeight=chart.bottom.value-chart.top.value
            if(height==520) normalHeights[font]=chartHeight
            if(height==700) assertEquals("增加可用高度不能拉伸图表",normalHeights.getValue(font),chartHeight,.5f)
            capture("home-$height-${if(font) "large" else "normal"}")
        }
        compose.onNodeWithTag("home_playlists").performClick();assertEquals("playlists",route)
    }
    @Test fun playlistSectionKeepsPagedCountWhenCollapsedAndDrawerReopens() {
        val data=LibraryState(playlists=List(23) {Playlist("section-$it","Playlist $it")})
        val peers=listOf(NearbyDevice("section-peer","Test PC",listOf(NearbyPlaylist("mix","Remote mix","",emptyList())),emptyList(),"desktop"))
        var creates=0
        compose.activity.setContent {MusicTheme(preferences) {
            LibraryShell(data,PlaybackState(),"music","Home",false,{},{},{creates++},{},{},{},{},peers) {Box(Modifier.fillMaxSize())}
        }}
        compose.onNodeWithTag("navigation_toggle").performClick()
        val list=compose.onNodeWithTag("drawer_list")
        repeat(2) {
            list.performScrollToNode(hasTestTag("drawer_expand_playlists"));compose.onNodeWithTag("drawer_expand_playlists").performClick()
        }
        list.performScrollToNode(hasTestTag("drawer_playlist:section-14"));compose.onNodeWithTag("drawer_playlist:section-14").assertIsDisplayed()
        list.performScrollToNode(hasTestTag("drawer_peer:section-peer:mix"));compose.onNodeWithTag("drawer_peer:section-peer:mix").assertIsDisplayed()
        list.performScrollToNode(hasTestTag("drawer_toggle_playlists"))
        val add=compose.onNodeWithTag("drawer_add_playlist").getUnclippedBoundsInRoot()
        val toggle=compose.onNodeWithTag("drawer_toggle_playlists").getUnclippedBoundsInRoot()
        assertTrue("整组开合按钮应位于添加按钮右侧",toggle.left>=add.right)
        compose.onNodeWithTag("drawer_toggle_playlists").performClick()
        compose.onNodeWithTag("drawer_playlists").assertIsDisplayed()
        compose.onNodeWithTag("drawer_playlist:section-0").assertDoesNotExist()
        compose.onNodeWithTag("drawer_peer:section-peer:mix").assertDoesNotExist()
        compose.onNodeWithTag("drawer_expand_playlists").assertDoesNotExist()
        compose.onNodeWithTag("drawer_collapse_playlists").assertDoesNotExist()
        compose.onNodeWithTag("drawer_add_playlist").performClick();assertEquals(1,creates)
        // 整个侧栏关闭/重开后仍保持分组收起；随后展开恢复此前显示的 15 项。
        repeat(2) {compose.onNodeWithTag("navigation_toggle").performSemanticsAction(SemanticsActions.OnClick) {it()};compose.waitForIdle()}
        compose.onNodeWithTag("drawer_playlist:section-0").assertDoesNotExist()
        compose.onNodeWithTag("drawer_toggle_playlists").performClick()
        list.performScrollToNode(hasTestTag("drawer_playlist:section-14"));compose.onNodeWithTag("drawer_playlist:section-14").assertIsDisplayed()
        list.performScrollToNode(hasTestTag("drawer_expand_playlists"));compose.onNodeWithTag("drawer_expand_playlists").performClick()
        list.performScrollToNode(hasTestTag("drawer_playlist:section-19"));compose.onNodeWithTag("drawer_playlist:section-19").assertIsDisplayed()
        compose.onNodeWithTag("drawer_collapse_playlists").assertDoesNotExist()
        capture("section-restored")
    }
    @Test fun reopeningDrawerDoesNotAnimatePlaylistVertically() {
        val data=LibraryState(playlists=List(12) {Playlist("fixed-$it","Playlist $it")})
        compose.activity.setContent {MusicTheme(preferences.copy(fontScale=1.3f)) {
            LibraryShell(data,PlaybackState(),"music","Home",false,{},{},{},{},{},{},{}) {Box(Modifier.fillMaxSize())}
        }}
        compose.onNodeWithTag("navigation_toggle").performClick()
        compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_playlist:fixed-0"))
        val expected=compose.onNodeWithTag("drawer_playlist:fixed-0").getUnclippedBoundsInRoot()
        compose.mainClock.autoAdvance=false
        try {
            repeat(3) {
                compose.onNodeWithTag("navigation_toggle").performClick();compose.mainClock.advanceTimeBy(320);compose.waitForIdle()
                compose.onNodeWithTag("navigation_toggle").performClick()
                // 首个时钟帧用于启动宽度动画，此时宽度仍为零；从首个可见帧开始检查纵向位置。
                compose.mainClock.advanceTimeBy(32);compose.waitForIdle()
                repeat(8) {
                    compose.mainClock.advanceTimeBy(32);compose.waitForIdle()
                    val frame=compose.onNodeWithTag("drawer_playlist:fixed-0").getUnclippedBoundsInRoot()
                    assertEquals("侧栏打开时歌单纵向位置应稳定",expected.top.value,frame.top.value,.5f)
                    assertEquals(expected.bottom.value,frame.bottom.value,.5f)
                }
            }
        } finally {compose.mainClock.autoAdvance=true}
    }
}
