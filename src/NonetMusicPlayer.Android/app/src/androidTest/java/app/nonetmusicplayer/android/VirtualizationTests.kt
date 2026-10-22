package app.nonetmusicplayer.android

import androidx.activity.compose.setContent
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.ui.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith

/** 大型歌单仅组合可见行；不向真实资料库写入测试曲目。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class VirtualizationTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    @Test fun tenThousandTracksStayLazyAndSearchable() {
        val songs=List(10000) {Track("virtual-$it","file:///fixture.wav","Song $it","Artist","Album")}
        val state=LibraryState(tracks=songs,preferences=Preferences(language="en"))
        compose.activity.setContent {MusicTheme(state.preferences) {TrackListPage("Large playlist","",songs,state,"",null,{_,_->},{},{},{},{},{})}}
        compose.onNodeWithTag("track_virtual-0").assertIsDisplayed()
        assertTrue(compose.onAllNodes(hasTestTag("track_virtual-9999")).fetchSemanticsNodes().isEmpty())
        compose.onNodeWithTag("track_list").performScrollToIndex(10001)
        compose.onNodeWithTag("track_virtual-9999").assertIsDisplayed()
        assertTrue(compose.onAllNodes(hasTestTag("track_virtual-0")).fetchSemanticsNodes().isEmpty())
    }
}
