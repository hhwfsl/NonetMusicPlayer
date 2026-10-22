package app.nonetmusicplayer.android.ui

import android.net.Uri
import androidx.activity.compose.BackHandler
import androidx.activity.compose.LocalActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material.icons.automirrored.outlined.QueueMusic
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.PlaylistAdd
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import app.nonetmusicplayer.android.ui.musicString as stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.*
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.*

/** 内容内导航与抽屉共享同一后台会话；所有长列表保留各自滚动状态。 */
@UnstableApi
@Composable fun MusicApp(vm: PlayerViewModel) {
    val library by vm.library.collectAsStateWithLifecycle();val playback by vm.playback.collectAsStateWithLifecycle();val busy by vm.busy.collectAsStateWithLifecycle()
    val nearby by vm.app.lan.state.collectAsStateWithLifecycle()
    val queue by vm.queue.collectAsStateWithLifecycle()
    var showQueue by remember {mutableStateOf(false)}
    var route by rememberSaveable { mutableStateOf("music") };var expanded by rememberSaveable { mutableStateOf(false) }
    var searchRequested by remember {mutableStateOf(false)}
    val backstack=remember { mutableStateListOf<String>() }
    var create by remember { mutableStateOf(false) };var editing by remember { mutableStateOf<Playlist?>(null) };var deleting by remember { mutableStateOf<Playlist?>(null) }
    var moreTrack by remember { mutableStateOf<Track?>(null) };var infoTrack by remember { mutableStateOf<Track?>(null) };var addTrack by remember { mutableStateOf<Track?>(null) }
    var playlistMore by remember { mutableStateOf(false) };var menuPlaylist by remember {mutableStateOf<Playlist?>(null)}
    var imageUri by remember {mutableStateOf<Uri?>(null)};var imageKey by remember {mutableStateOf("")}
    var batchIds by remember {mutableStateOf(emptySet<String>())};var removeIds by remember {mutableStateOf(emptySet<String>())}
    var lyricTarget by remember {mutableStateOf<Track?>(null)};var exportList by remember {mutableStateOf<Playlist?>(null)};var importTarget by rememberSaveable { mutableStateOf("liked") }
    val holder=rememberSaveableStateHolder()
    val activity=LocalActivity.current as? MainActivity
    fun navigate(next: String) {if(next!="browse") searchRequested=false;if(next!=route) {backstack+=route;route=next} }
    fun back() { if(expanded) expanded=false else route=if(backstack.isNotEmpty()) backstack.removeAt(backstack.lastIndex) else "music" }
    fun start(track: Track,queue: List<Track>) {activity?.requestNotificationPermission();vm.play(track,queue,route)}
    val files=rememberLauncherForActivityResult(ActivityResultContracts.OpenMultipleDocuments()) { uris -> if(uris.isNotEmpty()) vm.importFiles(importTarget,uris) }
    var folderCreatesPlaylist by remember {mutableStateOf(false)}
    val folder=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocumentTree()) { uri -> if(uri!=null) {if(folderCreatesPlaylist) vm.createFolderPlaylist(uri) {navigate("playlist:$it")} else vm.importFolder(importTarget,uri)};folderCreatesPlaylist=false }
    var scanConfirmation by remember {mutableStateOf<ScanOptions?>(null)}
    var pendingScan by remember {mutableStateOf<ScanOptions?>(null)}
    val scanPermission=rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {granted ->val options=pendingScan;pendingScan=null;if(granted && options!=null) vm.scanMusic(options) else if(!granted) vm.app.messages.tryEmit(R.string.scan_permission_denied)}
    fun beginScan(options: ScanOptions) {
        val permission=if(android.os.Build.VERSION.SDK_INT>=33) android.Manifest.permission.READ_MEDIA_AUDIO else android.Manifest.permission.READ_EXTERNAL_STORAGE
        if(options.includedFolders.isNotEmpty() || androidx.core.content.ContextCompat.checkSelfPermission(vm.app,permission)==android.content.pm.PackageManager.PERMISSION_GRANTED) vm.scanMusic(options) else {pendingScan=options;scanPermission.launch(permission)}
    }
    val imagePicker=rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) {imageUri=it}
    val exportM3u=rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("audio/x-mpegurl")) {uri ->if(uri!=null) exportList?.let {vm.exportPlaylist(it,uri)}}
    val exportLrc=rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("text/plain")) {uri ->if(uri!=null) lyricTarget?.let {vm.exportLyrics(it,uri)}}
    val lyricsFile=rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri -> if(uri!=null) lyricTarget?.let {vm.importLyrics(it,uri)} }
    BackHandler(expanded || route!="music") {back()}
    MusicTheme(library.preferences) {
        UpdateDialog(vm.app)
        val context=LocalContext.current;val snackbar=remember {SnackbarHostState()};val focus=LocalFocusManager.current
        LaunchedEffect(context) {vm.app.messages.collect {snackbar.showSnackbar(context.getString(it))}}
        LaunchedEffect(vm.app.lan) {vm.app.lan.notices.collect {snackbar.showSnackbar(it)}}
        var lines by remember { mutableStateOf<List<LyricLine>>(emptyList()) }
        LaunchedEffect(playback.track?.id,playback.track?.lyrics) {lines=playback.track?.let {vm.lyricsFor(it)} ?: emptyList()}
        Box(Modifier.fillMaxSize().background(NonetTheme.colorScheme.background).windowInsetsPadding(WindowInsets.safeDrawing)) {
            if(library.preferences.background.isNotBlank()) {
                coil.compose.AsyncImage(java.io.File(library.preferences.background),null,Modifier.fillMaxSize(),contentScale=androidx.compose.ui.layout.ContentScale.Crop,alpha=library.preferences.backgroundOpacity)
            }
            if(expanded && playback.track!=null) {
                PlayerScreen(playback,library.preferences.mode,library.playlists.first {it.id=="liked"}.trackIds.contains(playback.track!!.id),lines,
                    {expanded=false},vm::togglePlay,vm::previous,vm::next,vm::seek,vm::cycleMode,{vm.favorite(playback.track!!)},{moreTrack=playback.track})
            } else {
                val pageTitle=when {
                    route=="music" ->stringResource(R.string.home);route=="playlists" ->stringResource(R.string.playlists);route=="devices" ->stringResource(R.string.lan_title);route=="settings" ->stringResource(R.string.settings)
                    route=="browse" ->stringResource(R.string.browse_music);route=="albums" ->stringResource(R.string.albums);route=="artists" ->stringResource(R.string.artists);route=="recent" ->stringResource(R.string.recent);route=="statistics" ->stringResource(R.string.statistics)
                    route.startsWith("playlist:") ->library.playlists.firstOrNull {it.id==route.substringAfter(':')}?.let {if(it.id=="liked") stringResource(R.string.liked) else it.name}.orEmpty()
                    route.startsWith("peer:") ->nearby.peers.firstOrNull {it.id==route.split(':').getOrNull(1)}?.playlists?.firstOrNull {it.id==route.split(':').getOrNull(2)}?.name.orEmpty()
                    else ->route.substringAfter(':')
                }
                LibraryShell(library,playback,route,pageTitle,busy,{focus.clearFocus();navigate(it)},::back,{create=true},{if(!busy) scanConfirmation=library.preferences.scan},{expanded=true},vm::togglePlay,{showQueue=true},nearby.peers,
                    onSearch={focus.clearFocus();searchRequested=true;navigate("browse")}) {holder.SaveableStateProvider(route) {
                    when(route) {
                        "music" -> MusicHome(library,::navigate,{create=true})
                        "playlists" -> PlaylistGrid(library,{navigate("playlist:$it")},{create=true},{menuPlaylist=it;playlistMore=true},{id,delta ->vm.launchOperation {vm.repository.movePlaylist(id,delta)}})
                        "albums","artists" -> GroupGrid(library,route=="artists") {navigate((if(route=="artists") "artist:" else "album:")+it)}
                        "settings" -> SettingsScreen(library.preferences,{vm.launchOperation {vm.repository.updatePreferences(it)}},{vm.app.messages.tryEmit(R.string.invalid_settings)},{vm.launchOperation {vm.repository.updatePreferences(Preferences())}},{imageKey="background";imagePicker.launch("image/*")},vm)
                        "devices","sources" ->NearbyScreen(vm)
                        "statistics" -> StatisticsScreen(library)
                        else -> {
                            val playlist=library.playlists.firstOrNull {route=="playlist:${it.id}"}
                            val tracks=remember(library.tracks,library.recentIds,playlist,route,nearby.peers) {
                                val byId=library.tracks.associateBy {it.id}
                                when {
                                    playlist!=null -> playlist.trackIds.mapNotNull {byId[it]}
                                    route.startsWith("peer:") ->nearby.peers.firstOrNull {it.id==route.split(':').getOrNull(1)}?.playlists?.firstOrNull {it.id==route.split(':').getOrNull(2)}?.trackIds.orEmpty().mapNotNull {byId[it]}
                                    route=="recent" -> library.recentIds.mapNotNull {byId[it]}
                                    route.startsWith("album:") -> library.tracks.filter {it.inLibrary && it.album==route.substringAfter(':')}
                                    route.startsWith("artist:") -> library.tracks.filter {it.inLibrary && it.artist==route.substringAfter(':')}
                                    route=="browse" -> library.tracks.filter {it.inLibrary}
                                    else -> emptyList()
                                }
                            }
                            val title=pageTitle
                            TrackListPage(title,playlist?.description.orEmpty(),tracks,library,if(playback.source==route) playback.track?.id.orEmpty() else "",playlist?.id,::start,vm::favorite,{moreTrack=it},
                                {if(playlist!=null){importTarget=playlist.id;files.launch(arrayOf("audio/*"))}},
                                {if(playlist!=null){importTarget=playlist.id;folder.launch(null)}},{menuPlaylist=playlist;playlistMore=true},library.covers[route].orEmpty(),{batchIds=it},{track,delta ->if(playlist!=null) vm.launchOperation {vm.repository.moveTrack(playlist.id,track.id,delta)}},
                                requestSearchFocus=route=="browse" && searchRequested,onSearchFocusHandled={searchRequested=false})
                        }
                    }
                }}
            }
            SnackbarHost(snackbar,Modifier.align(Alignment.BottomCenter).padding(bottom=if(expanded) 16.dp else 80.dp,start=16.dp,end=16.dp))
        }
        if(create || editing!=null) {
            val target=editing;var name by remember(target) {mutableStateOf(target?.name.orEmpty())};var description by remember(target) {mutableStateOf(target?.description.orEmpty())}
            AlertDialog(onDismissRequest={create=false;editing=null},title={Text(stringResource(if(target==null) R.string.create_playlist else R.string.edit))},
                text={Column(verticalArrangement=Arrangement.spacedBy(12.dp)) {OutlinedTextField(name,{name=it.take(100)},label={Text(stringResource(R.string.playlist_name))},singleLine=true);if(target!=null) OutlinedTextField(description,{description=it.take(2000)},label={Text(stringResource(R.string.description))}) else OutlinedButton(onClick={create=false;folderCreatesPlaylist=true;folder.launch(null)}) {Icon(Icons.Outlined.CreateNewFolder,null);Spacer(Modifier.width(8.dp));Text(stringResource(R.string.create_from_folder))}}},
                confirmButton={TextButton(enabled=name.isNotBlank(),onClick={create=false;editing=null;vm.launchOperation {if(target==null) navigate("playlist:"+vm.repository.createPlaylist(name)) else vm.repository.editPlaylist(target.id,name,description)}}){Text(stringResource(R.string.save))}},
                dismissButton={TextButton(onClick={create=false;editing=null}){Text(stringResource(R.string.cancel))}})
        }
        val m3uPrefix=stringResource(R.string.playlist_file_prefix);val likedName=stringResource(R.string.liked)
        if(playlistMore) MusicSheet(onDismissRequest={playlistMore=false}) {
            val target=menuPlaylist ?: library.playlists.firstOrNull {route=="playlist:${it.id}"}
            SheetAction(Icons.Outlined.Image,stringResource(R.string.change_cover)) {playlistMore=false;imageKey=target?.let {"playlist:${it.id}"} ?: route;imagePicker.launch("image/*")}
            SheetAction(Icons.Outlined.Restore,stringResource(R.string.restore_cover)) {playlistMore=false;vm.launchOperation {vm.repository.setCover(target?.let {"playlist:${it.id}"} ?: route,"")}}
            if(target!=null) {
                SheetAction(Icons.Outlined.ArrowUpward,stringResource(R.string.move_up)) {playlistMore=false;vm.launchOperation {vm.repository.movePlaylist(target.id,-1)}}
                SheetAction(Icons.Outlined.ArrowDownward,stringResource(R.string.move_down)) {playlistMore=false;vm.launchOperation {vm.repository.movePlaylist(target.id,1)}}
                SheetAction(Icons.Outlined.FileUpload,stringResource(R.string.export_m3u)) {playlistMore=false;exportList=target;exportM3u.launch(m3uPrefix+"_"+(if(target.id=="liked") likedName else target.name)+".m3u")}
                if(target.id!="liked") {
                    SheetAction(Icons.Outlined.Edit,stringResource(R.string.edit)) {playlistMore=false;editing=target}
                    SheetAction(Icons.Outlined.DeleteOutline,stringResource(R.string.delete)) {playlistMore=false;deleting=target}
                }
            }
            Spacer(Modifier.height(24.dp))
        }
        if(deleting!=null) AlertDialog(onDismissRequest={deleting=null},title={Text(stringResource(R.string.delete))},text={Text(stringResource(R.string.delete_confirm))},confirmButton={TextButton(onClick={val id=deleting!!.id;deleting=null;vm.launchOperation {vm.repository.deletePlaylist(id);if(route=="playlist:$id") route="playlists"}}){Text(stringResource(R.string.delete))}},dismissButton={TextButton(onClick={deleting=null}){Text(stringResource(R.string.cancel))}})
        moreTrack?.let { track -> MusicSheet(onDismissRequest={moreTrack=null}) {
            Text(track.title,Modifier.padding(horizontal=24.dp,vertical=12.dp),style=NonetTheme.typography.titleLarge,maxLines=2,overflow=TextOverflow.Ellipsis)
            val liked=library.playlists.first {it.id=="liked"}.trackIds.contains(track.id)
            SheetAction(if(liked) Icons.Outlined.Favorite else Icons.Outlined.FavoriteBorder,stringResource(if(liked) R.string.remove_liked else R.string.add_liked)) {moreTrack=null;vm.favorite(track)}
            SheetAction(Icons.AutoMirrored.Outlined.PlaylistAdd,stringResource(R.string.add_to_playlist)) {moreTrack=null;addTrack=track}
            SheetAction(Icons.Outlined.Info,stringResource(R.string.track_info)) {moreTrack=null;infoTrack=track}
            if(expanded) {
                SheetAction(Icons.AutoMirrored.Outlined.QueueMusic,stringResource(R.string.queue)) {moreTrack=null;showQueue=true}
                SheetAction(Icons.Outlined.Subtitles,stringResource(R.string.import_lyrics)) {moreTrack=null;lyricTarget=track;lyricsFile.launch(arrayOf("*/*"))}
                SheetAction(Icons.Outlined.LinkOff,stringResource(R.string.unlink_lyrics)) {moreTrack=null;vm.launchOperation {vm.repository.setLyrics(track.id,"none:")}}
                if(track.lyrics!="none:" && (track.lyrics.isNotBlank() || track.providerId.startsWith("lan:") || (track.id==playback.track?.id && lines.isNotEmpty()))) SheetAction(Icons.Outlined.FileUpload,stringResource(R.string.export_lyrics)) {moreTrack=null;lyricTarget=track;exportLrc.launch(track.title.replace(Regex("[\\\\/:*?\"<>|]"),"_")+".lrc")}
            } else if(route=="recent") {
                SheetAction(Icons.Outlined.RemoveCircleOutline,stringResource(R.string.remove_recent)) {moreTrack=null;vm.launchOperation {vm.repository.removeRecent(setOf(track.id))}}
            } else if(route=="browse") {
                SheetAction(Icons.Outlined.DeleteOutline,stringResource(R.string.remove_everywhere)) {moreTrack=null;removeIds=setOf(track.id)}
            } else if(route.startsWith("playlist:")) {
                val id=route.substringAfter(':')
                SheetAction(Icons.Outlined.ArrowUpward,stringResource(R.string.move_up)) {moreTrack=null;vm.launchOperation {vm.repository.moveTrack(id,track.id,-1)}}
                SheetAction(Icons.Outlined.ArrowDownward,stringResource(R.string.move_down)) {moreTrack=null;vm.launchOperation {vm.repository.moveTrack(id,track.id,1)}}
                SheetAction(Icons.Outlined.RemoveCircleOutline,stringResource(R.string.remove)) {moreTrack=null;vm.launchOperation {vm.repository.removeTrack(id,track.id)}}
            }
            Spacer(Modifier.height(24.dp))
        } }
        addTrack?.let { track -> MusicSheet(onDismissRequest={addTrack=null}) {library.playlists.forEach {list ->SheetAction(Icons.AutoMirrored.Outlined.QueueMusic,if(list.id=="liked") stringResource(R.string.liked) else list.name) {addTrack=null;vm.addToPlaylist(list.id,listOf(track))}};Spacer(Modifier.height(24.dp))} }
        scanConfirmation?.let {options ->ScanConfirmation(options,{scanConfirmation=null;beginScan(options)},{scanConfirmation=null},{scanConfirmation=null;navigate("settings")})}
        nearby.incoming?.let {request ->AlertDialog(onDismissRequest={vm.app.lan.answerConnection(request.requestId,false)},title={Text(stringResource(R.string.lan_pair_title))},
            text={Text(stringResource(R.string.lan_pair_body,request.name,request.address,request.waitSeconds))},
            confirmButton={TextButton(onClick={vm.app.lan.answerConnection(request.requestId,true)},modifier=Modifier.testTag("lan_pair_accept")) {Text(stringResource(R.string.lan_accept))}},
            dismissButton={TextButton(onClick={vm.app.lan.answerConnection(request.requestId,false)},modifier=Modifier.testTag("lan_pair_reject")) {Text(stringResource(R.string.lan_reject))}})}
        if(showQueue) ModalBottomSheet(onDismissRequest={showQueue=false},dragHandle=null) {
            Row(Modifier.fillMaxWidth().padding(start=20.dp,top=8.dp,end=8.dp),verticalAlignment=Alignment.CenterVertically) {Text(stringResource(R.string.queue),Modifier.weight(1f),style=NonetTheme.typography.titleMedium);IconAction(Icons.Outlined.Close,stringResource(R.string.close)) {showQueue=false}}
            androidx.compose.foundation.lazy.LazyColumn(Modifier.fillMaxWidth().heightIn(max=480.dp),contentPadding=PaddingValues(8.dp)) {
                if(queue.isEmpty()) item {EmptyState(stringResource(R.string.queue_empty))}
                items(queue.size,key={queue[it].id}) {i ->val t=queue[i];TrackRow(t,i,t.id==playback.track?.id,library.playlists.first {it.id=="liked"}.trackIds.contains(t.id),{vm.play(t,queue,playback.source)},{vm.favorite(t)},{showQueue=false;moreTrack=t})}
            }
        }
        if(batchIds.isNotEmpty()) MusicSheet(onDismissRequest={batchIds=emptySet()}) {
            SheetAction(Icons.Outlined.FavoriteBorder,stringResource(R.string.add_liked)) {val ids=batchIds;batchIds=emptySet();vm.addToPlaylist("liked",library.tracks.filter {it.id in ids})}
            if(route.startsWith("playlist:")) SheetAction(Icons.Outlined.RemoveCircleOutline,stringResource(R.string.remove)) {val ids=batchIds;batchIds=emptySet();vm.launchOperation {vm.repository.removeTracks(route.substringAfter(':'),ids)}}
            if(route=="recent") SheetAction(Icons.Outlined.RemoveCircleOutline,stringResource(R.string.remove_recent)) {val ids=batchIds;batchIds=emptySet();vm.launchOperation {vm.repository.removeRecent(ids)}}
            if(route=="browse") SheetAction(Icons.Outlined.DeleteOutline,stringResource(R.string.remove_everywhere)) {removeIds=batchIds;batchIds=emptySet()}
            Spacer(Modifier.height(24.dp))
        }
        if(removeIds.isNotEmpty()) AlertDialog(onDismissRequest={removeIds=emptySet()},title={Text(stringResource(R.string.remove_everywhere))},
            text={Text(stringResource(R.string.remove_everywhere_confirm))},
            confirmButton={TextButton(onClick={val ids=removeIds;removeIds=emptySet();vm.launchOperation {vm.repository.removeEverywhere(ids)}}) {Text(stringResource(R.string.confirm))}},
            dismissButton={TextButton(onClick={removeIds=emptySet()}) {Text(stringResource(R.string.cancel))}})
        imageUri?.let {uri ->ImageCropper(uri,if(imageKey=="background") activity?.resources?.displayMetrics?.let {it.widthPixels.toFloat()/it.heightPixels} ?: 1f else 1f,
            {imageUri=null},{path ->vm.launchOperation {if(imageKey=="background") vm.repository.updatePreferences(library.preferences.copy(background=path)) else vm.repository.setCover(imageKey,path)}},{vm.app.messages.tryEmit(R.string.invalid_image)})}
        infoTrack?.let { track -> AlertDialog(onDismissRequest={infoTrack=null},title={Text(stringResource(R.string.track_info))},text={Column(Modifier.heightIn(max=420.dp).verticalScroll(rememberScrollState()),verticalArrangement=Arrangement.spacedBy(12.dp)) {
            Cover(track.cover,Modifier.size(120.dp));SelectionContainer {Text(listOf(track.title,track.artist,track.album,timeLabel(track.durationMs),track.uri).filter {it.isNotBlank()}.joinToString("\n\n"))}
        }},confirmButton={TextButton(onClick={infoTrack=null}){Text(stringResource(R.string.close))}}) }
    }
}
@Composable fun SheetAction(icon: androidx.compose.ui.graphics.vector.ImageVector,label: String,onClick: ()->Unit) {
    ListItem(headlineContent={Text(label,maxLines=2,overflow=TextOverflow.Ellipsis)},leadingContent={Icon(icon,null)},modifier=Modifier.clickable(onClick=onClick).padding(horizontal=8.dp))
}
