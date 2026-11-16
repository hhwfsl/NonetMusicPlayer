package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.*
import org.junit.Assert.*
import org.junit.Test

/** 同一套浏览规则覆盖分类、搜索、首页和原始队列，保留资料不能让断开的设备重新出现在全库。 */
class LibraryVisibilityTests {
    @Test fun connectedTracksAppearAndRetainedOfflineTracksDisappear() {
        val local=Track("local","file://test","Local")
        val temporary=local.copy(id="temporary",inLibrary=false)
        val remote=local.copy(id="remote",providerId="lan:pc",inLibrary=false)
        val retained=remote.copy(id="saved",inLibrary=true)
        val tracks=listOf(local,temporary,remote,retained)
        assertEquals(listOf("local","remote","saved"),tracks.filter {it.visibleInLibrary(setOf("remote","saved"))}.map {it.id})
        assertEquals(listOf("local"),tracks.filter {it.visibleInLibrary(emptySet())}.map {it.id})
        assertFalse(temporary.visibleInLibrary(setOf("temporary")))
    }
}
