package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.domain.*
import org.junit.Assert.*
import org.junit.Test

/** 来源权限与本机收藏容器分开：接收端不能修改远端来源，但仍可管理自己的歌单。 */
class ContentEditPolicyTests {
    @Test fun remoteCollectionsAndGroupsAreReadOnly() {
        val remote=Track("remote","nonet-lan://pc/r","Remote","Remote artist","Remote album",providerId="lan:pc",inLibrary=false)
        val state=LibraryState(tracks=listOf(remote))
        assertTrue(remote.isRemoteContent)
        for(key in listOf("peer:pc:p","lan:pc:p","album:Remote album","artist:Remote artist","browse")) assertFalse(state.canEditCover(key))
        assertTrue(state.canEditCover("playlist:liked"))
        assertFalse(state.canEditCover("playlist:missing"))
    }
    @Test fun localCollectionsKeepTheirOriginalEditingActions() {
        val local=Track("local","file://test","Local","Artist","Album")
        val state=LibraryState(tracks=listOf(local),playlists=listOf(Playlist("mine","My list")))
        assertFalse(local.isRemoteContent)
        for(key in listOf("playlist:mine","album:Album","artist:Artist","browse","recent")) assertTrue(state.canEditCover(key))
        assertFalse(state.canEditCover("peer:pc:mine"))
    }
}
