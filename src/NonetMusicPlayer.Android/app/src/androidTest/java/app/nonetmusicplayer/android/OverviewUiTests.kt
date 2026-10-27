package app.nonetmusicplayer.android

import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.playback.PlayerViewModel
import app.nonetmusicplayer.android.ui.*
import kotlinx.coroutines.*
import org.json.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import java.util.concurrent.CopyOnWriteArrayList

/** 页面测试验证真实输入、虚拟列表分页和尺寸，不替代真实账号授权验收。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class OverviewUiTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private val preferences=Preferences(language="en")
    private fun screenshot(name: String) {
        val dir=File(compose.activity.getExternalFilesDir(null),"qa").apply {mkdirs()}
        File(dir,"overview-$name.png").outputStream().use {compose.onRoot().captureToImage().asAndroidBitmap().compress(android.graphics.Bitmap.CompressFormat.PNG,100,it)}
    }
    @Test fun overviewHasHourlyChartAndPlaylistNavigation() {
        val hour=System.currentTimeMillis()/3_600_000
        val state=LibraryState(tracks=listOf(Track("one","content://fixture","First song")),playlists=listOf(Playlist("liked","",trackIds=listOf("one"))),hourlyListening=listOf(HourlyListening(hour,180000,1)),preferences=preferences)
        var route=""
        compose.activity.setContent {MusicTheme(preferences) {MusicHome(state,{route=it},{})}}
        compose.onNodeWithTag("hourly_chart").assertIsDisplayed()
        compose.onNodeWithText("Overview").assertExists()
        compose.onAllNodesWithText("View all").onFirst().performScrollTo().performClick();assertEquals("playlists",route)
        screenshot("home")
        compose.activity.setContent {MusicTheme(preferences) {StatisticsScreen(state)}}
        compose.onNodeWithTag("weekly_chart").assertExists();compose.onNodeWithTag("hourly_chart").assertExists();screenshot("statistics")
    }
}
