package app.nonetmusicplayer.android

import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.runBlocking
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.util.UUID

/** 从真实侧栏打开新建弹窗，验证写入 SQLite 后路由、侧栏状态和后台歌单网格位置都不变。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class PlaylistCreationTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    @Test fun creatingPlaylistKeepsCurrentPageAndScrollPosition() {
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        compose.waitUntil(15000) {vm.playback.value.ready}
        val repository=vm.repository;val preferences=repository.state.value.preferences
        val prefix="creation-"+UUID.randomUUID().toString().take(8)+"-"
        try {
            runBlocking {
                repository.updatePreferences(preferences.copy(language="en"))
                repeat(16) {repository.createPlaylist(prefix+it)}
            }
            compose.onNodeWithTag("navigation_toggle").performClick()
            compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_playlists"))
            compose.onNodeWithTag("drawer_playlists").performClick()
            compose.onNodeWithTag("playlist_grid").performScrollToNode(hasText(prefix+"8"))
            val before=compose.onNodeWithText(prefix+"8").getUnclippedBoundsInRoot()
            compose.onNodeWithTag("navigation_toggle").performClick()
            compose.onNodeWithTag("drawer_list").performScrollToNode(hasTestTag("drawer_playlists"))
            compose.onNodeWithContentDescription("New playlist").performClick()
            compose.onNode(hasSetTextAction() and hasAnyAncestor(hasTestTag("playlist_name_field"))).performClick().performTextInput(prefix+"created")
            compose.onNodeWithTag("playlist_save").performClick()
            compose.waitUntil(5000) {repository.state.value.playlists.any {it.name==prefix+"created"}}
            compose.onNodeWithTag("playlist_grid").assertExists()
            compose.onNodeWithTag("drawer_list").assertExists()
            compose.onNodeWithTag("navigation_toggle").performSemanticsAction(SemanticsActions.OnClick) {it()}
            compose.waitForIdle()
            val after=compose.onNodeWithText(prefix+"8").getUnclippedBoundsInRoot()
            assertEquals("新建后应保留网格滚动位置",before.top.value,after.top.value,.5f)
        } finally {
            runBlocking {
                repository.state.value.playlists.filter {it.name.startsWith(prefix)}.forEach {repository.deletePlaylist(it.id)}
                repository.updatePreferences(preferences)
            }
        }
    }
}
