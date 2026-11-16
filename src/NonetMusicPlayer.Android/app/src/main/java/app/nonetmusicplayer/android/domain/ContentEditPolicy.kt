package app.nonetmusicplayer.android.domain

/** 远端来源内容不可改写；收藏、队列和本机歌单成员关系仍属于接收设备自己的资料。 */
val Track.isRemoteContent: Boolean get()=providerId.startsWith("lan:")

fun LibraryState.canEditCover(key: String): Boolean = when {
    key.startsWith("peer:") || key.startsWith("lan:") -> false
    key.startsWith("playlist:") -> playlists.any {it.id==key.substringAfter(':')}
    key.startsWith("album:") -> tracks.any {it.inLibrary && !it.isRemoteContent && it.album==key.substringAfter(':')}
    key.startsWith("artist:") -> tracks.any {it.inLibrary && !it.isRemoteContent && it.artist==key.substringAfter(':')}
    key=="browse" || key=="recent" -> tracks.any {it.inLibrary && !it.isRemoteContent}
    else -> false
}
