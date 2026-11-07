package app.nonetmusicplayer.android

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.ui.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File

/** 在实际模拟器分辨率/密度下运行，包含系统安全区、导航栏和迷你播放器所占空间。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class ResponsiveHomeQueueTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private val songs=List(100) {Track("screen-$it","file:///fixture.wav","Long title $it · 音乐テスト",artist="Artist")}
    private val state=LibraryState(tracks=songs,playlists=List(20) {Playlist("screen-$it","歌单 · Playlist $it",trackIds=listOf(songs[it].id))},recentIds=songs.map {it.id},
        hourlyListening=List(24) {HourlyListening(System.currentTimeMillis()/3_600_000-it,(it%6+1)*30000L,1)})
    private fun capture(name: String) {
        val config=compose.activity.resources.configuration
        val folder=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        val tag=when(name) {"queue-last"->"playback_queue";"playlist-toolbar"->"track_list";else->"music_overview"}
        // Dialog 使用独立原生窗口，Activity 的 PixelCopy 会只得到背景；系统截图包含真实弹层。
        val bitmap=if(name=="queue-last") requireNotNull(InstrumentationRegistry.getInstrumentation().uiAutomation.takeScreenshot())
            else compose.onNode(isRoot() and hasAnyDescendant(hasTestTag(tag))).captureToImage().asAndroidBitmap()
        File(folder,"responsive-${config.screenWidthDp}x${config.screenHeightDp}-$name.png").outputStream().use {
            bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG,100,it)
        }
    }
    @Test fun homeAndDrawerFitRealAvailableSpaceInThreeLanguages() {
        var preferences by mutableStateOf(Preferences(language="en",theme="light"))
        var route=""
        compose.activity.setContent {MusicTheme(preferences) {
            Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
                LibraryShell(state,PlaybackState(track=songs[0],durationMs=180000),"music","Nonet",false,{route=it},{},{},{},{},{},{}) {
                    MusicHome(state,{route=it},{})
                }
            }
        }}
        for(language in listOf("en","zh","ja")) for(scale in listOf(1f,1.3f)) {
            compose.runOnUiThread {preferences=preferences.copy(language=language,fontScale=scale)};compose.waitForIdle()
            val home=compose.onNodeWithTag("music_overview").getUnclippedBoundsInRoot()
            val content=compose.onNodeWithTag("home_content").getUnclippedBoundsInRoot()
            assertTrue("首页超出可用高度 $language $scale: $content / $home",content.bottom<=home.bottom+1.dp)
            compose.onNodeWithTag("home_recent").assertDoesNotExist()
            compose.onNodeWithTag("hourly_chart").assertIsDisplayed()
            val chart=compose.onNodeWithTag("hourly_chart").getUnclippedBoundsInRoot()
            assertTrue("主页图表不能拉伸成大面积填充: $chart",chart.bottom-chart.top<=260.dp)
            val canvas=compose.onNodeWithTag("listening_chart_canvas").getUnclippedBoundsInRoot()
            assertTrue("24 小时图表画布缺少空间: $canvas",canvas.bottom-canvas.top>=12.dp)
            for(tag in listOf("home_browse","home_playlists","home_recent_link","home_statistics")) {
                val button=compose.onNodeWithTag(tag);button.assertIsDisplayed()
                val rect=button.getUnclippedBoundsInRoot()
                assertTrue("快捷入口触摸区不足: $tag $rect",rect.bottom-rect.top>=48.dp)
                assertTrue("快捷入口越界: $tag $rect / $home",rect.bottom<=home.bottom+1.dp && rect.right<=home.right+1.dp)
            }
            capture("home-$language-$scale")
        }
        compose.onNodeWithTag("home_playlists").performClick();assertEquals("playlists",route)
        compose.onNodeWithTag("navigation_toggle").performClick()
        repeat(3) {
            compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_expand_playlists"))
            compose.onNodeWithTag("drawer_expand_playlists").performClick()
        }
        compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_playlist:screen-19"))
        compose.onNodeWithTag("drawer_playlist:screen-19").assertIsDisplayed();capture("drawer-expanded")
    }
    @Test fun queueSheetKeepsControlsAndLastSongReachable() {
        compose.activity.setContent {MusicTheme(Preferences(language="zh",fontScale=1.3f)) {
            ModalBottomSheet({}) {
                Row(Modifier.fillMaxWidth()) {Text("播放队列",Modifier.weight(1f).padding(12.dp));TextButton({}) {Text("关闭")}}
                PlaybackQueueList(songs,songs[0].id,emptyList(),{},{},{},{_,_->})
            }
        }}
        compose.onNodeWithTag("playback_queue").performScrollToNode(hasTestTag("track_screen-99"))
        compose.onNodeWithTag("track_screen-99").assertIsDisplayed();compose.onNodeWithText("关闭").assertIsDisplayed()
        capture("queue-last")
    }
    @Test fun playlistToolbarKeepsSearchAndTouchTargetsReadable() {
        compose.activity.setContent {MusicTheme(Preferences(language="zh",fontScale=1.3f)) {
            Box(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
                TrackListPage("歌单","",songs,state,"","test",{_,_->},{},{},{},{},{})
            }
        }}
        compose.onNodeWithTag("track_list").performScrollToIndex(1)
        // 非整数密度下的像素取整和 dp 浮点换算允许一个物理像素的容差。
        val pixel=(1f/compose.activity.resources.displayMetrics.density).dp
        val viewport=compose.onNodeWithTag("track_list").getUnclippedBoundsInRoot()
        val input=compose.onNodeWithTag("search_input").getUnclippedBoundsInRoot()
        assertTrue("搜索文本区被按钮挤压: $input",input.right-input.left>=120.dp)
        for(tag in listOf("playlist_add_files","playlist_add_folder","playlist_page_more","playlist_selection")) {
            val node=compose.onNodeWithTag(tag);node.assertIsDisplayed()
            val rect=node.getUnclippedBoundsInRoot()
            assertTrue("工具按钮越界: $tag $rect / $viewport",rect.left>=viewport.left && rect.right<=viewport.right && rect.bottom<=viewport.bottom)
            assertTrue("工具触摸区不足: $tag $rect",rect.right-rect.left>=48.dp-pixel && rect.bottom-rect.top>=48.dp-pixel)
        }
        capture("playlist-toolbar")
    }
}
