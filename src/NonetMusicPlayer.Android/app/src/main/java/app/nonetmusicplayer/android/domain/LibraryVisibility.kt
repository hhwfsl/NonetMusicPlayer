package app.nonetmusicplayer.android.domain

/** 浏览页使用连接投影；断开后即使保留统计/收藏资料，也不再把远端曲目列入全库分类。 */
fun Track.visibleInLibrary(activeRemoteIds: Set<String>): Boolean =
    if(providerId.startsWith("lan:")) id in activeRemoteIds else inLibrary
