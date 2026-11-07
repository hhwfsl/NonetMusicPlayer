package app.nonetmusicplayer.android.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.core.*
import androidx.compose.foundation.*
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.LazyListState
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.QueueMusic
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.*
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.semantics.CustomAccessibilityAction
import androidx.compose.ui.semantics.customActions
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.ui.musicString as stringResource

/** 网页式平面布局：内容内导航、推入式抽屉、全宽迷你播放器；没有顶栏或底部标签导航。 */
@Composable fun LibraryShell(state: LibraryState,playback: PlaybackState,route: String,title: String,busy: Boolean,
    onNavigate: (String)->Unit,onBack: ()->Unit,onCreate: ()->Unit,onScan: ()->Unit,onOpenPlayer: ()->Unit,onPlay: ()->Unit,onQueue: ()->Unit,
    peers: List<app.nonetmusicplayer.android.network.NearbyDevice> = emptyList(),
    onSearch: ()->Unit={onNavigate("browse")},
    onMovePlaylist: (String,Int)->Unit={_,_->},
    content: @Composable ()->Unit) {
    var opened by rememberSaveable {mutableStateOf(false)}
    // 使用新的保存键，避免旧版 Boolean 展开状态被恢复为当前的 Int 可见数量。
    var visiblePlaylistCount by key("drawer_playlist_visible_count_v2") {rememberSaveable {mutableIntStateOf(5)}}
    // 整组开合与分页数量分别保存，收起分组不重置已通过“展开更多”显示的歌单数量。
    var playlistSectionExpanded by key("drawer_playlist_section_expanded_v1") {rememberSaveable {mutableStateOf(true)}}
    val drawerList=rememberLazyListState()
    BackHandler(opened) {opened=false}
    Column(Modifier.fillMaxSize()) {
        BoxWithConstraints(Modifier.weight(1f).fillMaxWidth().clipToBounds()) {
            val wide=maxWidth>=840.dp
            val maximum=minOf(276.dp,maxWidth*.82f)
            val density=LocalDensity.current;val maximumPx=with(density) {maximum.toPx()}
            var dragging by remember {mutableStateOf(false)};var draggedWidth by remember {mutableFloatStateOf(0f)};var startWidth by remember {mutableFloatStateOf(0f)}
            val width by animateDpAsState(if(dragging) with(density) {draggedWidth.toDp()} else if(opened) maximum else 0.dp,animationSpec=if(dragging) snap() else tween(220),label="library_drawer")
            val currentWidth by rememberUpdatedState(with(density) {width.toPx()})
            val gestures=Modifier.pointerInput(maximumPx) {
                // 子控件先处理横向拖动，音轨、进度条和相册横向列表不会被侧栏抢走手势。
                detectHorizontalDragGestures(onDragStart={dragging=true;startWidth=currentWidth;draggedWidth=currentWidth},
                    onHorizontalDrag={change,delta ->change.consume();draggedWidth=(draggedWidth+delta).coerceIn(0f,maximumPx)},
                    onDragCancel={dragging=false},onDragEnd={val distance=draggedWidth-startWidth;val threshold=with(density) {40.dp.toPx()};opened=when {distance>threshold->true;distance< -threshold->false;else->draggedWidth>maximumPx/2};dragging=false})
            }
            @Composable fun Page(modifier: Modifier) {
                Column(modifier.background(NonetTheme.colorScheme.background.copy(alpha=if(state.preferences.background.isBlank()) 1f else .68f))) {
                    Row(Modifier.fillMaxWidth().height(54.dp).padding(horizontal=8.dp),verticalAlignment=Alignment.CenterVertically) {
                        IconAction(Icons.Outlined.Menu,stringResource(if(opened) R.string.close_navigation else R.string.open_navigation),Modifier.testTag("navigation_toggle")) {opened=!opened}
                        val root=route in listOf("music","playlists","browse","albums","artists","recent","statistics","devices","settings")
                        if(!root) IconAction(Icons.AutoMirrored.Outlined.ArrowBack,stringResource(R.string.back),onClick=onBack)
                        Text(title,Modifier.weight(1f).padding(start=4.dp),style=NonetTheme.typography.titleLarge,fontWeight=FontWeight.SemiBold,maxLines=1,overflow=TextOverflow.Ellipsis)
                        IconAction(Icons.Outlined.Search,stringResource(R.string.search),Modifier.testTag("open_music_search"),onClick=onSearch)
                    }
                    if(busy) LinearProgressIndicator(Modifier.fillMaxWidth().height(2.dp))
                    Box(Modifier.fillMaxSize()) {content()}
                }
            }
            Box(Modifier.fillMaxSize().then(gestures).testTag("library_swipe_area")) {
            Row(Modifier.fillMaxSize()) {
                Box(Modifier.width(width).fillMaxHeight().clipToBounds()) {
                    // 内容始终按最终侧栏宽度排版，只改变外部裁剪宽度，避免窄帧换行把歌单从下方推上来。
                    if(width>0.dp) Box(Modifier.fillMaxHeight().wrapContentWidth(Alignment.Start,unbounded=true).width(maximum)) {
                        LibraryDrawer(state,route,drawerList,{opened=false;onNavigate(it)},onCreate,{opened=false;onScan()},peers,
                            visiblePlaylistCount,{visiblePlaylistCount=it},playlistSectionExpanded,{playlistSectionExpanded=it},onMovePlaylist)
                    }
                }
                if(wide) {Page(Modifier.weight(1f).fillMaxHeight())}
            }
            if(!wide) {
                Page(Modifier.fillMaxSize().offset(x=width))
                if(opened) Box(Modifier.fillMaxHeight().fillMaxWidth().offset(x=width).clickable(indication=null,interactionSource=remember {androidx.compose.foundation.interaction.MutableInteractionSource()}) {opened=false})
            }
            }
        }
        MiniPlayer(playback,onOpenPlayer,onPlay,onQueue)
    }
}

@Composable private fun LibraryDrawer(state: LibraryState,route: String,list: LazyListState,onNavigate: (String)->Unit,onCreate: ()->Unit,onScan: ()->Unit,peers: List<app.nonetmusicplayer.android.network.NearbyDevice>,
    visibleCount: Int,onVisibleCount: (Int)->Unit,expanded: Boolean,onExpanded: (Boolean)->Unit,onMove: (String,Int)->Unit) {
    val colors=NonetTheme.colorScheme
    val visible=if(expanded) state.playlists.take(visibleCount) else emptyList()
    val up=stringResource(R.string.move_up);val down=stringResource(R.string.move_down)
    @Composable fun Link(id: String,label: String,icon: ImageVector,action: ()->Unit={onNavigate(id)},modifier: Modifier=Modifier) {
        Row(modifier.fillMaxWidth().testTag("drawer_$id").clip(RoundedCornerShape(10.dp)).background(if(route==id) colors.primaryContainer else Color.Transparent).clickable(onClick=action).padding(horizontal=14.dp,vertical=13.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(14.dp)) {
            val tint=when(id) {"music","browse"->Color(0xFF2FBA68);"albums"->Color(0xFFED635C);"artists"->Color(0xFFE5AB3B);"recent","devices"->Color(0xFF408ACC);"statistics"->Color(0xFFE45E64);"settings"->Color(0xFF2EB879);else->colors.primary}
            Icon(icon,null,Modifier.size(21.dp),tint=tint)
            Text(label,maxLines=1,overflow=TextOverflow.Ellipsis,style=NonetTheme.typography.bodyLarge)
        }
    }
    @Composable fun PlaylistLink(p: Playlist,modifier: Modifier=Modifier) = Link("playlist:${p.id}",if(p.id=="liked") stringResource(R.string.liked) else p.name,
        if(p.id=="liked") Icons.Outlined.FavoriteBorder else Icons.AutoMirrored.Outlined.QueueMusic,modifier=modifier)
    ReorderableListHost(visible,{"drawer_"+it.id},expanded,{p,delta ->onMove(p.id,delta)},list=list,tag="drawer_reorder_host",draggedContent={PlaylistLink(it)}) {_,preview,dragged ->
    LazyColumn(Modifier.fillMaxWidth().fillMaxHeight().testTag("drawer_list"),state=list,userScrollEnabled=dragged==null,contentPadding=PaddingValues(12.dp),verticalArrangement=Arrangement.spacedBy(3.dp)) {
        item {Row(Modifier.fillMaxWidth().padding(horizontal=10.dp,vertical=14.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(10.dp)) {
            Image(painterResource(R.drawable.app_icon),null,Modifier.size(36.dp).testTag("drawer_app_icon"),contentScale=ContentScale.Fit)
            Text("Nonet",style=NonetTheme.typography.titleLarge,fontWeight=FontWeight.SemiBold)
        }}
        item {Surface(shape=RoundedCornerShape(22.dp)) {Column(Modifier.padding(6.dp)) {
            Link("music",stringResource(R.string.home),Icons.Outlined.Home)
            Link("browse",stringResource(R.string.browse_music),Icons.Outlined.MusicNote)
            Link("albums",stringResource(R.string.albums),Icons.Outlined.Album)
            Link("artists",stringResource(R.string.artists),Icons.Outlined.PersonOutline)
            Link("recent",stringResource(R.string.recent),Icons.Outlined.History)
            Link("statistics",stringResource(R.string.statistics),Icons.Outlined.BarChart)
        }}}
        item {HorizontalDivider(Modifier.padding(vertical=10.dp),color=colors.outlineVariant)}
        item {Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
            Text(stringResource(R.string.playlists),Modifier.weight(1f).testTag("drawer_playlists").clickable {onNavigate("playlists")}.padding(start=14.dp,top=12.dp,bottom=12.dp),style=NonetTheme.typography.labelLarge,color=colors.onSurfaceVariant)
            IconAction(Icons.Outlined.Add,stringResource(R.string.create_playlist),Modifier.testTag("drawer_add_playlist"),onClick=onCreate)
            IconAction(if(expanded) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,
                stringResource(if(expanded) R.string.collapse_playlist_section else R.string.expand_playlist_section),
                Modifier.testTag("drawer_toggle_playlists")) {onExpanded(!expanded)}
        }}
        items(preview,key={"drawer_"+it.id}) {p ->PlaylistLink(p,(if(dragged!=null) Modifier.animateItem(fadeInSpec=null,fadeOutSpec=null) else Modifier).alpha(if(dragged=="drawer_"+p.id) 0f else 1f).semantics {
            customActions=listOf(CustomAccessibilityAction(up) {onMove(p.id,-1);true},CustomAccessibilityAction(down) {onMove(p.id,1);true})
        })}
        // 每次只追加五个可见歌单；整组收起由标题行控制，分页数量保持不变。
        if(expanded && visibleCount<state.playlists.size) item(key="drawer_expand_playlists") {
            Link("expand_playlists",stringResource(R.string.show_more_playlists),Icons.Outlined.ExpandMore,
                {
                    // 插入项时保留视口索引，避免列表锚定“展开更多”按钮而把新增歌单推到屏幕上方。
                    list.requestScrollToItem(list.firstVisibleItemIndex,list.firstVisibleItemScrollOffset)
                    onVisibleCount((visibleCount+5).coerceAtMost(state.playlists.size))
                })
        }
        // 连接歌单只投影到导航，不混入本地歌单，断线后随状态自动消失。
        if(expanded) peers.forEach {peer ->items(peer.playlists,key={"peer:${peer.id}:${it.id}"}) {p ->
            val id="peer:${peer.id}:${p.id}"
            Row(Modifier.fillMaxWidth().testTag("drawer_$id").clip(RoundedCornerShape(10.dp)).background(if(route==id) colors.primaryContainer else Color.Transparent)
                .clickable {onNavigate(id)}.padding(horizontal=14.dp,vertical=10.dp),verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(14.dp)) {
                Icon(Icons.Outlined.Devices,null,Modifier.size(21.dp),tint=colors.primary)
                Column(Modifier.weight(1f)) {
                    Text(p.name,maxLines=1,overflow=TextOverflow.Ellipsis)
                    val kind=stringResource(when(peer.deviceKind) {"desktop"->R.string.lan_source_desktop;"mobile"->R.string.lan_source_mobile;else->R.string.lan_source_unknown})
                    Text(stringResource(R.string.lan_playlist_source,kind,peer.name),style=NonetTheme.typography.bodySmall,color=colors.onSurfaceVariant,maxLines=1,overflow=TextOverflow.Ellipsis)
                }
            }
        }}
        item {HorizontalDivider(Modifier.padding(vertical=10.dp),color=colors.outlineVariant)}
        item {Surface(shape=RoundedCornerShape(22.dp)) {Column(Modifier.padding(6.dp)) {
            Link("scan",stringResource(R.string.scan_music),Icons.Outlined.LibraryAdd,onScan)
            Link("devices",stringResource(R.string.lan_title),Icons.Outlined.Devices)
            Link("settings",stringResource(R.string.settings),Icons.Outlined.Settings)
        }}}
    }
    }
}
