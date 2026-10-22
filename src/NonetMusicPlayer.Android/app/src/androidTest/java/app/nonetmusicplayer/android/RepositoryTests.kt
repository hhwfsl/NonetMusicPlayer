package app.nonetmusicplayer.android

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.data.MusicRepository
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.*
import kotlinx.coroutines.*
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

/** 在真实 Android SQLite 上验证，使用独立数据库，不清理应用的歌单。 */
@RunWith(AndroidJUnit4::class)
class RepositoryTests {
    @Test fun sqliteOrderingFavoritesHistoryAndPersistence() = runBlocking {
        val context=ApplicationProvider.getApplicationContext<MusicApplication>()
        val database="repository-test-${java.util.UUID.randomUUID()}.db"
        val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO);val repo=MusicRepository(context,scope,database)
        try {
            val playlist=repo.createPlaylist("日常オレンジ")
            val one=Track("one","content://test/one","第一首");val two=Track("two","content://test/two","二首")
            repo.addTracks(playlist,listOf(one,two));assertEquals(listOf("two","one"),repo.state.value.playlists.first {it.id==playlist}.trackIds)
            repo.moveTrack(playlist,"one",-1);assertEquals(listOf("one","two"),repo.state.value.playlists.first {it.id==playlist}.trackIds)
            repo.toggleFavorite(one);assertEquals(listOf("one"),repo.state.value.playlists.first {it.id=="liked"}.trackIds)
            repo.storeTrack(one.copy(title="New title"));assertTrue(repo.state.value.playlists.first {it.id==playlist}.trackIds.contains("one"))
            repo.setLyrics("one","/local/one.lrc")
            repo.storeTracks(listOf(one.copy(inLibrary=false)))
            assertTrue(repo.state.value.tracks.first {it.id=="one"}.inLibrary)
            assertEquals("/local/one.lrc",repo.state.value.tracks.first {it.id=="one"}.lyrics)
            repo.recordStarted("one");repo.recordDuration("one",2000);assertEquals(2000L,repo.state.value.statistics.sumOf {it.durationMs})
            val temp=Track("temp","content://test/temp","Temp",inLibrary=false);repo.storeTrack(temp);repo.recordStarted("temp");repo.recordDuration("temp",4000)
            assertTrue(repo.state.value.recentIds.contains("temp"));assertFalse(repo.state.value.statistics.any {it.trackId=="temp"})
            repo.updatePreferences(repo.state.value.preferences.copy(historyLimit=1,theme="dark",language="ja"));assertEquals(1,repo.state.value.recentIds.size)
            repo.savePlayback(PlaybackSnapshot("one",listOf("one","two"),2345,.5f,"playlist:$playlist"));assertEquals(2345L,repo.restorePlayback().positionMs)
            assertEquals("playlist:$playlist",repo.restorePlayback().source)
            assertThrows(IllegalArgumentException::class.java) {runBlocking {repo.deletePlaylist("liked")}}
            repo.deletePlaylist(playlist);assertTrue(repo.state.value.tracks.any {it.id=="one"})
        } finally {scope.cancel();repo.close();context.deleteDatabase(database)}
    }
    @Test fun peerValidationAndReferencesSurviveDisconnect() = runBlocking {
        assertEquals(2L..8L,peerRange("bytes=2-8",10));assertEquals(7L..9L,peerRange("bytes=-3",10));assertNull(peerRange("bytes=10-20",10))
        val key="a".repeat(64)
        assertEquals("192.168.1.2",PeerSecurity.parse("nonet://connect?host=192.168.1.2&amp;port=45678&amp;cert=$key&amp;key=$key".replace("&amp;","&")).origin.host)
        assertThrows(IllegalArgumentException::class.java) {PeerSecurity.parse("nonet://connect?host=example.com&port=45678&cert=$key&key=$key")}
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val name="peer-test-${java.util.UUID.randomUUID()}.db";val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO);val repo=MusicRepository(app,scope,name)
        try {val temporary=Track("remote-a","nonet-lan://peer/a","A",providerId="lan:peer",inLibrary=false);val saved=temporary.copy(id="remote-b",title="B")
            repo.storeTracks(listOf(temporary,saved));repo.addTracks("liked",listOf(saved));repo.removePeerEphemeral("peer")
            assertFalse(repo.state.value.tracks.any {it.id==temporary.id});assertTrue(repo.state.value.tracks.any {it.id==saved.id});assertTrue(saved.id in repo.state.value.playlists.first {it.id=="liked"}.trackIds)
        } finally {scope.cancel();repo.close();app.deleteDatabase(name)}
    }
}
