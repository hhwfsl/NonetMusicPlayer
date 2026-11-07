package app.nonetmusicplayer.android.ui

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.*
import androidx.compose.foundation.lazy.grid.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.QueueMusic
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

@Composable fun MusicHome(state: LibraryState,onNavigate: (String)->Unit,onCreate: ()->Unit) {
    val tracks=remember(state.tracks) {state.tracks.filter {it.inLibrary}}
    val byId=remember(state.tracks) {state.tracks.associateBy {it.id}}
    val today=java.time.LocalDate.now().toString()
    val fontScale=androidx.compose.ui.platform.LocalDensity.current.fontScale
    @Composable fun Summary(compact: Boolean=false) {
        Surface(Modifier.fillMaxWidth().testTag("home_summary"),RoundedCornerShape(14.dp),NonetTheme.colorScheme.surfaceVariant) {
            if(compact) Column(Modifier.padding(10.dp),verticalArrangement=Arrangement.spacedBy(4.dp)) {
                // 短横屏的概览列较窄，标签和值分列成三行，保证时间数值不会被省略。
                listOf(R.string.music to tracks.size,R.string.playlists to state.playlists.size,R.string.today_listening to null).forEach {(label,count) ->
                    Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(label),Modifier.weight(1f),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=1,overflow=TextOverflow.Ellipsis)
                        Text(count?.toString() ?: timeLabel(state.statistics.filter {it.day==today}.sumOf {it.durationMs}),style=NonetTheme.typography.titleSmall,maxLines=1)
                    }
                }
            } else Column(Modifier.padding(12.dp),verticalArrangement=Arrangement.spacedBy(6.dp)) {
                Text(stringResource(R.string.overview),style=NonetTheme.typography.titleSmall,maxLines=1)
                Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                    listOf(R.string.music to tracks.size,R.string.playlists to state.playlists.size,R.string.today_listening to null).forEach {(label,count) ->
                        Column(Modifier.weight(1f)) {
                            Text(count?.toString() ?: timeLabel(state.statistics.filter {it.day==today}.sumOf {it.durationMs}),style=NonetTheme.typography.titleLarge,maxLines=1,overflow=TextOverflow.Ellipsis)
                            Text(stringResource(label),style=NonetTheme.typography.bodySmall,color=NonetTheme.colorScheme.onSurfaceVariant,maxLines=1,overflow=TextOverflow.Ellipsis)
                        }
                    }
                }
            }
        }
    }
    @Composable fun Shortcuts() {
        Column(Modifier.testTag("home_shortcuts"),verticalArrangement=Arrangement.spacedBy(8.dp)) {
            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                QuickCard(stringResource(if(tracks.isEmpty()) R.string.create_playlist else R.string.music),Icons.Outlined.MusicNote,Modifier.weight(1f).testTag("home_browse")) {if(tracks.isEmpty()) onCreate() else onNavigate("browse")}
                QuickCard(stringResource(R.string.playlists),Icons.AutoMirrored.Outlined.QueueMusic,Modifier.weight(1f).testTag("home_playlists")) {onNavigate("playlists")}
            }
            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                QuickCard(stringResource(R.string.home_recent_short),Icons.Outlined.History,Modifier.weight(1f).testTag("home_recent_link")) {onNavigate("recent")}
                QuickCard(stringResource(R.string.home_statistics_short),Icons.Outlined.BarChart,Modifier.weight(1f).testTag("home_statistics")) {onNavigate("statistics")}
            }
        }
    }
    @Composable fun PlaylistPreview() {
        Column(Modifier.testTag("home_playlist_preview")) {
            SectionHeading(stringResource(R.string.playlists)) {onNavigate("playlists")}
            Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                state.playlists.take(2).forEach {p ->
                    Surface(Modifier.weight(1f).testTag("home_playlist_${p.id}"),RoundedCornerShape(10.dp),NonetTheme.colorScheme.surfaceVariant,onClick={onNavigate("playlist:${p.id}")}) {
                        Row(Modifier.padding(8.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                            Cover(state.covers["playlist:${p.id}"] ?: byId[p.trackIds.firstOrNull()]?.cover.orEmpty(),Modifier.size(36.dp),p.id=="liked")
                            Column(Modifier.weight(1f)) {
                                Text(if(p.id=="liked") stringResource(R.string.liked) else p.name,maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.titleSmall)
                                Text(stringResource(R.string.tracks_count,p.trackIds.size),style=NonetTheme.typography.bodySmall,maxLines=1,overflow=TextOverflow.Ellipsis,color=NonetTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                    }
                }
            }
        }
    }
    @Composable fun HourlyChart(modifier: Modifier=Modifier) {
        BoxWithConstraints(modifier.fillMaxWidth()) {
            // 默认使用紧凑图表的自然高度，仅在空间不足时缩小，绝不拉伸占满剩余区域。
            val normalHeight=128.dp+71.dp*fontScale
            if(maxHeight<normalHeight) ListeningChart(stringResource(R.string.chart_hours),ListeningCharts.hours(state.hourlyListening),Modifier.height(maxHeight),fitHeight=true)
            else ListeningChart(stringResource(R.string.chart_hours),ListeningCharts.hours(state.hourlyListening),compact=true)
        }
    }
    // 图表始终保留且使用统计页相同的小时数据；小屏优先保留快捷入口和图表，横屏分栏。
    BoxWithConstraints(Modifier.fillMaxSize().testTag("music_overview").padding(12.dp)) {
        val height=maxHeight/fontScale
        if(maxWidth>=400.dp && maxWidth>maxHeight*1.25f) {
            Row(Modifier.fillMaxSize().testTag("home_content"),horizontalArrangement=Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1f),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                    if(height>=250.dp) Summary(compact=true)
                    Shortcuts()
                    if(height>=450.dp) PlaylistPreview()
                }
                HourlyChart(Modifier.weight(1f))
            }
        } else {
            Column(Modifier.fillMaxSize().testTag("home_content"),verticalArrangement=Arrangement.spacedBy(8.dp)) {
                if(height>=400.dp) Summary()
                Shortcuts()
                HourlyChart()
                if(height>=650.dp) PlaylistPreview()
            }
        }
    }
}

@Composable private fun QuickCard(label: String,icon: androidx.compose.ui.graphics.vector.ImageVector,modifier: Modifier,onClick: ()->Unit) {
    Surface(onClick=onClick,modifier=modifier,shape=RoundedCornerShape(10.dp),color=NonetTheme.colorScheme.surface,border=BorderStroke(1.dp,NonetTheme.colorScheme.outlineVariant)) {
        BoxWithConstraints(Modifier.fillMaxWidth()) {
            // 短横屏的入口列较窄时省略装饰图标，为完整操作文字保留空间。
            val showIcon=maxWidth>=128.dp*androidx.compose.ui.platform.LocalDensity.current.fontScale
            Row(Modifier.fillMaxWidth().heightIn(min=48.dp).padding(horizontal=10.dp,vertical=10.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                if(showIcon) Icon(icon,null,tint=NonetTheme.colorScheme.primary,modifier=Modifier.size(20.dp))
                Text(label,Modifier.weight(1f),maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.labelLarge)
            }
        }
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
    val fontScale=androidx.compose.ui.platform.LocalDensity.current.fontScale
    @Composable fun ToolbarActions() {
        if(playlistId!=null) {
            IconAction(Icons.Outlined.Add,stringResource(R.string.add_files),Modifier.testTag("playlist_add_files"),onClick=onAddFiles)
            IconAction(Icons.Outlined.CreateNewFolder,stringResource(R.string.add_folder),Modifier.testTag("playlist_add_folder"),onClick=onAddFolder)
        }
        IconAction(Icons.Outlined.MoreVert,stringResource(R.string.more),Modifier.testTag("playlist_page_more"),onClick=onPageMore)
        IconAction(if(bulk) Icons.Outlined.Close else Icons.Outlined.Checklist,stringResource(if(bulk) R.string.exit_selection else R.string.batch),Modifier.testTag("playlist_selection")) {bulk=!bulk;selected=emptySet()}
    }
    SongReorderHost(filtered,canReorder,onMove,draggedContent={track ->TrackRow(track,filtered.indexOf(track),track.id==currentId,track.id in liked,{},{},{})}) {list,preview,draggedId ->
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
        item { BoxWithConstraints(Modifier.fillMaxWidth()) {
            // 小屏或大字时将操作按钮另放一行，搜索框不被四个固定触摸区挤到无法输入。
            if(maxWidth<180.dp*fontScale+48.dp*(if(playlistId!=null) 4 else 2)) {
                Column {
                    SearchField(query,{query=it},Modifier.fillMaxWidth(),requestFocus=requestSearchFocus,onFocusHandled=onSearchFocusHandled)
                    Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.End,verticalAlignment=Alignment.CenterVertically) {ToolbarActions()}
                }
            } else Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                SearchField(query,{query=it},Modifier.weight(1f),requestFocus=requestSearchFocus,onFocusHandled=onSearchFocusHandled)
                ToolbarActions()
            }
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
        itemsIndexed(preview,key={_,t -> t.id}) { index,track ->
            Row(Modifier.animateItem(fadeInSpec=null,fadeOutSpec=null).fillMaxWidth().alpha(if(draggedId==track.id) 0f else 1f).semantics {
                if(canReorder) customActions=listOf(CustomAccessibilityAction(up) {onMove(track,-1);true},CustomAccessibilityAction(down) {onMove(track,1);true})
            },verticalAlignment=Alignment.CenterVertically) {
                if(bulk) Checkbox(track.id in selected,{checked ->selected=if(checked) selected+track.id else selected-track.id})
                Box(Modifier.weight(1f)) {TrackRow(track,index,track.id==currentId,track.id in liked,{onPlay(track,tracks)},{onLike(track)},{onMore(track)})}
            }
        }
    }
    }
}
