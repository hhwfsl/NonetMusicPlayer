package app.nonetmusicplayer.android

import androidx.test.core.app.ApplicationProvider
import app.nonetmusicplayer.android.data.*
import app.nonetmusicplayer.android.domain.*
import kotlinx.coroutines.*
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.util.UUID

/** 验证旧数据库升级与新旧备份，不依赖任何云端服务。 */
class HourlyStatisticsTests {
    @Test fun sqliteHourlyMigrationAndBackup()=runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val name="hourly-${UUID.randomUUID()}.db"
        MusicDatabase(app,name).apply {writableDatabase.apply {execSQL("DROP TABLE listening_hours");version=1};close()}
        val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO);val repo=MusicRepository(app,scope,name)
        try {
            val track=Track("local","content://test/123","歌名",inLibrary=false)
            repo.storeTrack(track);repo.addTracks("liked",listOf(track));repo.recordStarted(track.id)
            repo.recordDuration(track.id,5000,System.currentTimeMillis())
            assertEquals(5000L,repo.state.value.statistics.sumOf {it.durationMs});assertEquals(5000L,repo.state.value.hourlyListening.sumOf {it.durationMs})
            repo.updatePreferences(repo.state.value.preferences.copy(accent="#80123456",sleepMinutes=45,sleepFinishSong=true))
            val backup=repo.exportMetadata();assertTrue(backup.getJSONObject("tables").has("listening_hours"))
            repo.restoreMetadata(backup);assertEquals(5000L,repo.state.value.hourlyListening.sumOf {it.durationMs});assertEquals("#80123456",repo.state.value.preferences.accent)
            assertEquals(45,repo.state.value.preferences.sleepMinutes);assertTrue(repo.state.value.preferences.sleepFinishSong)
            val old=JSONObject(backup.toString());old.getJSONObject("tables").remove("listening_hours");repo.restoreMetadata(old)
            assertEquals(5000L,repo.state.value.statistics.sumOf {it.durationMs});assertTrue(repo.state.value.hourlyListening.isEmpty())
        } finally {scope.cancel();repo.close();app.deleteDatabase(name)}
    }
}
