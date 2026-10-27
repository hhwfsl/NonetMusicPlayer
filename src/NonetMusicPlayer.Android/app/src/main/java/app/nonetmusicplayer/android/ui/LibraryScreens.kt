package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.*
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.platform.testTag
import app.nonetmusicplayer.android.ui.musicString as stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*

@Composable fun MusicHome(state: LibraryState,onNavigate: (String)->Unit,onCreate: ()->Unit,onPlay: (Track,List<Track>)->Unit={_,_->}) {
    val tracks=remember(state.tracks) {state.tracks.filter {it.inLibrary}}
    val byId=remember(state.tracks) {state.tracks.associateBy {it.id}}
    val today=java.time.LocalDate.now().toString()
    LazyColumn(Modifier.testTag("music_overview"),contentPadding=PaddingValues(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
        item {
            Surface(shape=RoundedCornerShape(16.dp),color=NonetTheme.colorScheme.surfaceVariant) {
                Column(Modifier.fillMaxWidth().padding(16.dp),verticalArrangement=Arrangement.spacedBy(10.dp)) {
                    Text(stringResource(R.string.overview),style=NonetTheme.typography.titleMedium)
                    Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(16.dp)) {
                        listOf(R.string.music to tracks.size,R.string.playlists to state.playlists.size,R.string.today_listening to null).forEach {(label,count) ->Column(Modifier.weight(1f)) {Text(count?.toString() ?: timeLabel(state.statistics.filter {it.day==today}.sumOf {it.durationMs}),style=NonetTheme.typography.headlineSmall,fontWeight=FontWeight.SemiBold,maxLines=1,overflow=TextOverflow.Ellipsis);Text(stringResource(label),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)}}
                    }
                }
            }
        }
        item { Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(12.dp)) {
            QuickCard(stringResource(R.string.recent),Icons.Outlined.History,Modifier.weight(1f)) { onNavigate("recent") }
            QuickCard(stringResource(R.string.statistics),Icons.Outlined.BarChart,Modifier.weight(1f)) { onNavigate("statistics") }
        } }
        if(tracks.isEmpty()) item { EmptyState(stringResource(R.string.empty_music),stringResource(R.string.empty_music_body),stringResource(R.string.create_playlist),onCreate) }
        item {ListeningChart(stringResource(R.string.chart_hours),ListeningCharts.hours(state.hourlyListening),compact=true)}
        item {SectionHeading(stringResource(R.string.playlists)) {onNavigate("playlists")}}
        item {LazyRow(horizontalArrangement=Arrangement.spacedBy(12.dp)) {
            items(state.playlists.take(8),key={it.id}) {p ->
                Column(Modifier.width(104.dp).clickable {onNavigate("playlist:${p.id}")},verticalArrangement=Arrangement.spacedBy(6.dp)) {
                    Cover(state.covers["playlist:${p.id}"] ?: byId[p.trackIds.firstOrNull()]?.cover.orEmpty(),Modifier.size(104.dp),p.id=="liked")
                    Text(if(p.id=="liked") stringResource(R.string.liked) else p.name,maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.titleSmall)
                    Text(stringResource(R.string.tracks_count,p.trackIds.size),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                }
            }
        }}
        if(state.recentIds.isNotEmpty()) {
            item {SectionHeading(stringResource(R.string.recent_music)) {onNavigate("recent")}}
            items(state.recentIds.take(3),key={"recent-$it"}) {id ->byId[id]?.let {t ->
                Surface(Modifier.fillMaxWidth(),RoundedCornerShape(12.dp),NonetTheme.colorScheme.surfaceVariant,onClick={onPlay(t,state.recentIds.mapNotNull {byId[it]})}) {
                    Row(Modifier.padding(10.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(10.dp)) {Cover(t.cover,Modifier.size(40.dp));Column(Modifier.weight(1f)) {Text(t.title,maxLines=1,overflow=TextOverflow.Ellipsis);Text(t.artist,maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)};Icon(Icons.Outlined.PlayArrow,stringResource(R.string.play),tint=NonetTheme.colorScheme.primary)}
                }
            }}
        }
        item {Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(10.dp)) {QuickCard(stringResource(R.string.albums),Icons.Outlined.Album,Modifier.weight(1f)) {onNavigate("albums")};QuickCard(stringResource(R.string.artists),Icons.Outlined.PersonOutline,Modifier.weight(1f)) {onNavigate("artists")}}}
    }
}
@Composable private fun QuickCard(label: String,icon: androidx.compose.ui.graphics.vector.ImageVector,modifier: Modifier,onClick: ()->Unit) {
    Surface(onClick=onClick,modifier=modifier,shape=RoundedCornerShape(10.dp),color=NonetTheme.colorScheme.surface,border=BorderStroke(1.dp,NonetTheme.colorScheme.outlineVariant)) {
        Row(Modifier.padding(14.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(10.dp)) {Icon(icon,null,tint=NonetTheme.colorScheme.primary,modifier=Modifier.size(20.dp));Text(label,maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.labelLarge)}
    }
}
@Composable private fun GroupStrip(groups: Map<String,List<Track>>,covers: Map<String,String>,prefix: String,onSelect: (String)->Unit) {
    LazyRow(horizontalArrangement=Arrangement.spacedBy(16.dp)) {
        items(groups.entries.take(12),key={it.key}) { entry ->
            Column(Modifier.width(104.dp).clickable { onSelect(entry.key) },verticalArrangement=Arrangement.spacedBy(6.dp)) {
                Cover(covers[prefix+entry.key] ?: entry.value.firstOrNull { it.cover.isNotBlank() }?.cover.orEmpty(),Modifier.size(104.dp))
                Text(entry.key,maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.titleSmall)
                Text(stringResource(R.string.tracks_count,entry.value.size),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}
@Composable fun GroupGrid(state: LibraryState,artist: Boolean,onSelect: (String)->Unit) {
    val groups=remember(state.tracks,artist) {
        state.tracks.filter {it.inLibrary}.groupBy {if(artist) it.artist else it.album}.filterKeys {it.isNotBlank()}
    }
    LazyVerticalGrid(columns=GridCells.Adaptive(128.dp),contentPadding=PaddingValues(20.dp),horizontalArrangement=Arrangement.spacedBy(16.dp),verticalArrangement=Arrangement.spacedBy(20.dp)) {
        items(groups.entries.toList(),key={it.key}) { (name,tracks) ->
            Column(Modifier.clickable { onSelect(name) },verticalArrangement=Arrangement.spacedBy(8.dp)) {
                Cover(state.covers[(if(artist) "artist:" else "album:")+name] ?: tracks.firstOrNull { it.cover.isNotBlank() }?.cover.orEmpty(),Modifier.fillMaxWidth().aspectRatio(1f))
                Text(name,maxLines=1,overflow=TextOverflow.Ellipsis,fontWeight=FontWeight.Medium)
                Text(stringResource(R.string.tracks_count,tracks.size),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
            }
        }
    }
}
@Composable fun PlaylistGrid(state: LibraryState,onSelect: (String)->Unit,onCreate: ()->Unit,onMore: (Playlist)->Unit,onMove: (String,Int)->Unit) {
    LazyVerticalGrid(columns=GridCells.Adaptive(128.dp),modifier=Modifier.testTag("playlist_grid"),contentPadding=PaddingValues(20.dp),horizontalArrangement=Arrangement.spacedBy(16.dp),verticalArrangement=Arrangement.spacedBy(20.dp)) {
        item(span={GridItemSpan(maxLineSpan)}) { Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
            Text(stringResource(R.string.playlists_count,state.playlists.size),Modifier.weight(1f),color=NonetTheme.colorScheme.onSurfaceVariant)
            FilledTonalIconButton(onClick=onCreate) { Icon(Icons.Outlined.Add,stringResource(R.string.create_playlist)) }
        } }
        items(state.playlists,key={it.id}) { playlist ->
            val cover=state.covers["playlist:${playlist.id}"] ?: state.tracks.firstOrNull { it.id==playlist.trackIds.firstOrNull() }?.cover.orEmpty()
            Column(Modifier.clickable { onSelect(playlist.id) },verticalArrangement=Arrangement.spacedBy(8.dp)) {
                Cover(cover,Modifier.fillMaxWidth().aspectRatio(1f),playlist.id=="liked")
                Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                    Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(4.dp)) {
                        Text(if(playlist.id=="liked") stringResource(R.string.liked) else playlist.name,maxLines=1,overflow=TextOverflow.Ellipsis,fontWeight=FontWeight.Medium)
                        Text(stringResource(R.string.tracks_count,playlist.trackIds.size),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                    }
                    IconAction(Icons.Outlined.MoreHoriz,stringResource(R.string.more),Modifier.testTag("playlist_more_${playlist.id}").reorderOnLongPress {onMove(playlist.id,it)}) {onMore(playlist)}
                }
            }
        }
    }
}
@Composable fun TrackListPage(title: String,description: String,tracks: List<Track>,state: LibraryState,currentId: String,
    playlistId: String?,onPlay: (Track,List<Track>)->Unit,onLike: (Track)->Unit,onMore: (Track)->Unit,onAddFiles: ()->Unit,onAddFolder: ()->Unit,onPageMore: ()->Unit,cover: String="",onBatch: (Set<String>)->Unit={},onMove: (Track,Int)->Unit={_,_->},
    requestSearchFocus: Boolean=false,onSearchFocusHandled: ()->Unit={}) {
    var bulk by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(false) }
    var selected by remember { mutableStateOf(emptySet<String>()) }
    var query by remember(title) { mutableStateOf("") }
    val filtered=remember(tracks,query) { if(query.isBlank()) tracks else tracks.filter { listOf(it.title,it.artist,it.album).any { value -> value.contains(query,true) } } }
    val liked=state.playlists.firstOrNull { it.id=="liked" }?.trackIds.orEmpty()
    val canReorder=!bulk && playlistId!=null && query.isBlank()
    val up=stringResource(R.string.move_up);val down=stringResource(R.string.move_down)
    SongReorderHost(filtered,canReorder,onMove,draggedContent={track ->TrackRow(track,filtered.indexOf(track),track.id==currentId,track.id in liked,{},{},{})}) {list,draggedId ->
    // 先实现搜索框所在的惰性列表项，再交给输入框请求焦点；重复点击也能从长列表底部返回。
    LaunchedEffect(requestSearchFocus) {if(requestSearchFocus) list.scrollToItem(1)}
    // 长按拾起后由外层处理拖拽，暂时禁用用户滚动，避免子列表先消费同一条手指移动；程序边缘滚动仍可用。
    LazyColumn(Modifier.fillMaxSize().testTag("track_list"),state=list,userScrollEnabled=draggedId==null,contentPadding=PaddingValues(16.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
        item { Row(Modifier.fillMaxWidth().padding(4.dp),horizontalArrangement=Arrangement.spacedBy(16.dp),verticalAlignment=Alignment.CenterVertically) {
            Cover(cover.ifBlank {tracks.firstOrNull()?.cover.orEmpty()},Modifier.size(112.dp),playlistId=="liked")
            Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(6.dp)) {
                Text(title,style=NonetTheme.typography.headlineSmall,fontWeight=FontWeight.SemiBold,maxLines=2,overflow=TextOverflow.Ellipsis)
                Text(description.ifBlank { stringResource(R.string.tracks_count,tracks.size) },maxLines=2,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant)
                IconAction(Icons.Outlined.PlayArrow,stringResource(R.string.play),enabled=tracks.isNotEmpty()) { tracks.firstOrNull()?.let { onPlay(it,tracks) } }
            }
        } }
        item { Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
            SearchField(query,{query=it},Modifier.weight(1f),requestFocus=requestSearchFocus,onFocusHandled=onSearchFocusHandled)
            if(playlistId!=null) {
                IconAction(Icons.Outlined.Add,stringResource(R.string.add_files),onClick=onAddFiles)
                IconAction(Icons.Outlined.CreateNewFolder,stringResource(R.string.add_folder),onClick=onAddFolder)
            }
            IconAction(Icons.Outlined.MoreVert,stringResource(R.string.more),onClick=onPageMore)
            IconAction(if(bulk) Icons.Outlined.Close else Icons.Outlined.Checklist,stringResource(if(bulk) R.string.exit_selection else R.string.batch)) {bulk=!bulk;selected=emptySet()}
        } }
        if(bulk) item {
            Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                if(bulk) {
                    TriStateCheckbox(if(selected.isEmpty()) androidx.compose.ui.state.ToggleableState.Off else if(filtered.all {it.id in selected}) androidx.compose.ui.state.ToggleableState.On else androidx.compose.ui.state.ToggleableState.Indeterminate,
                        onClick={selected=if(filtered.all {it.id in selected}) emptySet() else filtered.map {it.id}.toSet()})
                    Text(stringResource(R.string.selected_count,selected.size),Modifier.weight(1f))
                    IconAction(Icons.Outlined.MoreHoriz,stringResource(R.string.selected_actions),enabled=selected.isNotEmpty()) {onBatch(selected)}
                } else Spacer(Modifier.weight(1f))
            }
        }
        if(filtered.isEmpty()) item { EmptyState(stringResource(if(query.isBlank()) R.string.empty_playlist else R.string.no_results)) }
        itemsIndexed(filtered,key={_,t -> t.id}) { index,track ->
            Row(Modifier.fillMaxWidth().alpha(if(draggedId==track.id) 0f else 1f).semantics {
                if(canReorder) customActions=listOf(CustomAccessibilityAction(up) {onMove(track,-1);true},CustomAccessibilityAction(down) {onMove(track,1);true})
            },verticalAlignment=Alignment.CenterVertically) {
                if(bulk) Checkbox(track.id in selected,{checked ->selected=if(checked) selected+track.id else selected-track.id})
                Box(Modifier.weight(1f)) {TrackRow(track,index,track.id==currentId,track.id in liked,{onPlay(track,tracks)},{onLike(track)},{onMore(track)})}
            }
        }
    }
    }
}
