package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.Track
import app.nonetmusicplayer.android.playback.PlayerViewModel
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 只搜索已经导入的曲目；保留键盘自动聚焦和按视口组合的虚拟列表。 */
@UnstableApi
@Composable internal fun MusicSearchScreen(vm: PlayerViewModel,onPlay: (Track,List<Track>)->Unit,onMore: (Track)->Unit,requestFocus: Boolean,onFocusHandled: ()->Unit) {
    val library by vm.library.collectAsStateWithLifecycle()
    var query by rememberSaveable {mutableStateOf("")}
    val tracks=remember(library.tracks,query) {library.tracks.filter {it.inLibrary && (query.isBlank() || listOf(it.title,it.artist,it.album).any {v ->v.contains(query,true)})}}
    val liked=library.playlists.firstOrNull {it.id=="liked"}?.trackIds.orEmpty()
    val list=rememberLazyListState()
    LaunchedEffect(query) {list.scrollToItem(0)}
    Column(Modifier.fillMaxSize()) {
        SearchField(query,{query=it},Modifier.fillMaxWidth().padding(horizontal=16.dp,vertical=8.dp),placeholder=stringResource(R.string.search),requestFocus=requestFocus,onFocusHandled=onFocusHandled)
        LazyColumn(Modifier.fillMaxSize().testTag("music_search_results"),state=list,contentPadding=PaddingValues(horizontal=12.dp,vertical=8.dp),verticalArrangement=Arrangement.spacedBy(4.dp)) {
            itemsIndexed(tracks,key={_,t ->t.id}) {index,t ->TrackRow(t,index,false,t.id in liked,{onPlay(t,tracks)},{vm.favorite(t)},{onMore(t)})}
            if(tracks.isEmpty()) item {EmptyState(stringResource(R.string.no_results))}
        }
    }
}
