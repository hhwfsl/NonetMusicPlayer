package app.nonetmusicplayer.android.playback

import android.app.Application
import android.content.ComponentName
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import androidx.core.content.ContextCompat
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import app.nonetmusicplayer.android.*
import app.nonetmusicplayer.android.data.LibraryBackup
import app.nonetmusicplayer.android.data.AudioImporter
import app.nonetmusicplayer.android.data.EmbeddedLyrics
import app.nonetmusicplayer.android.data.DeviceMusicScanner
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import app.nonetmusicplayer.android.domain.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import java.io.File

/** 控制器只持有服务连接；UI 以 Flow 观察状态，不直接持有解码器。 */
@UnstableApi
class PlayerViewModel(application: Application) : AndroidViewModel(application) {
    val app=application as MusicApplication
    val repository=app.repository
    val library=repository.state
    val queue=combine(library.map {it.tracks}.distinctUntilChanged(),app.playQueue) {tracks,ids ->val byId=tracks.associateBy {it.id};ids.mapNotNull {byId[it]}}
        .stateIn(viewModelScope,SharingStarted.WhileSubscribed(5000),emptyList())
    private val _playback=MutableStateFlow(PlaybackState())
    val playback=_playback.asStateFlow()
    private val _busy=MutableStateFlow(false)
    val busy=_busy.asStateFlow()
    private val future=MediaController.Builder(application,SessionToken(application,ComponentName(application,PlaybackService::class.java))).buildAsync()
    private var controller: MediaController?=null
    private var indexedTracks: List<Track> = emptyList()
    private var trackIndex: Map<String,Track> = emptyMap()
    private val importer=AudioImporter(application)
    private val embeddedLock=Mutex()
    private val embeddedCache=linkedMapOf<String,String>()
    private val listener=object : Player.Listener { override fun onEvents(player: Player, events: Player.Events) { updatePlayback() } }
    init {
        future.addListener({ runCatching { controller=future.get();controller?.addListener(listener);updatePlayback() }.onFailure { report(R.string.playback_failed,it) } },ContextCompat.getMainExecutor(application))
        viewModelScope.launch { while(isActive) { delay(250);updatePlayback() } }
    }
    private fun updatePlayback() {
        val c=controller ?: return
        val tracks=library.value.tracks
        if(tracks !== indexedTracks) {indexedTracks=tracks;trackIndex=tracks.associateBy {it.id}}
        val track=trackIndex[c.currentMediaItem?.mediaId]
        _playback.value=PlaybackState(track,c.currentPosition.coerceAtLeast(0),c.duration.takeIf { it>0 } ?: track?.durationMs ?: 0,c.isPlaying,c.playbackState==Player.STATE_BUFFERING,true,c.mediaMetadata.extras?.getString("source").orEmpty())
    }
    fun launchOperation(block: suspend () -> Unit) { viewModelScope.launch {
        try { block() } catch(cancel: CancellationException) { throw cancel }
        catch(error: Throwable) { report(R.string.operation_failed,error) }
    } }
    private fun report(message: Int, error: Throwable) { app.messages.tryEmit(message);app.log.write("ERROR","User operation failed",error) }
    fun play(track: Track, queue: List<Track>, source: String="") {
        if(!app.lan.available(track)) {app.messages.tryEmit(R.string.lan_disconnected);return}
        val c=controller ?: run { app.messages.tryEmit(R.string.player_connecting);return }
        // 队列位于同一应用进程，不把数千个 ID 重复塞进 Binder 消息。
        app.playQueue.value=queue.map {it.id}
        c.sendCustomCommand(PlaybackService.PLAY_QUEUE,Bundle().apply {putString("track",track.id);putString("source",source)})
    }
    fun togglePlay() {controller?.let {if(it.isPlaying) it.pause() else it.play()}}
    // 解码器只保留当前项；使用队列命令避免客户端把单项时间轴的切歌当成无操作。
    fun next() { controller?.sendCustomCommand(PlaybackService.NEXT,Bundle.EMPTY) }
    fun previous() { controller?.sendCustomCommand(PlaybackService.PREVIOUS,Bundle.EMPTY) }
    fun seek(position: Long) { controller?.seekTo(position.coerceIn(0,playback.value.durationMs.coerceAtLeast(0))) }
    fun setVolume(volume: Float) { controller?.volume=volume.coerceIn(0f,1f) }
    fun cycleMode() = launchOperation {
        val p=library.value.preferences;val modes=listOf("repeat-all","repeat-one","shuffle")
        repository.updatePreferences(p.copy(mode=modes[(modes.indexOf(p.mode)+1)%modes.size]))
    }
    fun favorite(track: Track) = launchOperation {repository.toggleFavorite(app.lan.saveArtwork(track))}
    fun addToPlaylist(id: String,tracks: List<Track>) = launchOperation {repository.addTracks(id,tracks.map {app.lan.saveArtwork(it)})}
    fun importFiles(playlistId: String, uris: List<Uri>) = launchOperation {
        _busy.value=true
        try {
            val tracks=mutableListOf<Track>();var errors=0
            for(uri in uris.take(10000)) {
                persistPermission(uri)
                runCatching { importer.read(uri) }.onSuccess { tracks+=it }.onFailure { errors++;app.log.write("ERROR","Audio import failed",it) }
            }
            if(tracks.isNotEmpty()) { repository.addTracks(playlistId,tracks);app.messages.tryEmit(if(errors==0) R.string.import_complete else R.string.import_partial) }
            else app.messages.tryEmit(R.string.import_failed)
        } finally { _busy.value=false }
    }
    fun importFolder(playlistId: String, uri: Uri) = launchOperation {
        _busy.value=true
        try { persistPermission(uri);val tracks=importer.scanFolder(uri);repository.addTracks(playlistId,tracks);app.messages.tryEmit(if(tracks.isEmpty()) R.string.import_failed else R.string.import_complete) } finally { _busy.value=false }
    }
    fun createFolderPlaylist(uri: Uri,onCreated: (String)->Unit) = launchOperation {
        _busy.value=true
        try {persistPermission(uri);val tracks=importer.scanFolder(uri)
            val name=androidx.documentfile.provider.DocumentFile.fromTreeUri(app,uri)?.name?.take(100).orEmpty().ifBlank {app.getString(R.string.playlists)}
            val id=repository.createPlaylist(name);repository.addTracks(id,tracks);onCreated(id);app.messages.tryEmit(if(tracks.isEmpty()) R.string.import_failed else R.string.import_complete)
        } finally {_busy.value=false}
    }
    fun addScanFolder(uri: Uri, excluded: Boolean) = launchOperation {
        requireNotNull(DeviceMusicScanner.location(uri))
        app.contentResolver.takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION)
        check(androidx.documentfile.provider.DocumentFile.fromTreeUri(app,uri)?.canRead()==true)
        val p=library.value.preferences;val scan=p.scan
        repository.updatePreferences(p.copy(scan=if(excluded) scan.copy(excludedFolders=(scan.excludedFolders+uri.toString()).distinct()) else scan.copy(includedFolders=(scan.includedFolders+uri.toString()).distinct())))
    }
    fun scanMusic(options: ScanOptions) = launchOperation {
        if(_busy.value) return@launchOperation
        _busy.value=true
        try {
            val tracks=DeviceMusicScanner(app,importer).scan(options)
            if(tracks.isNotEmpty()) {
                val name=app.getString(R.string.device_music);val id=library.value.playlists.firstOrNull {it.name==name}?.id ?: repository.createPlaylist(name)
                repository.addTracks(id,tracks)
            }
            app.messages.tryEmit(if(tracks.isEmpty()) R.string.scan_empty else R.string.import_complete)
        } catch(e: CancellationException) {throw e}
        catch(e: Exception) {app.log.write("WARN","Device music scan failed",e);app.messages.tryEmit(R.string.scan_failed)
        } finally {_busy.value=false}
    }
    private fun persistPermission(uri: Uri) { runCatching { app.contentResolver.takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION) } }
    fun openExternal(uri: Uri) = openExternalQueue(listOf(uri))
    fun openExternalQueue(uris: List<Uri>) = launchOperation {
        val tracks=uris.take(1000).mapNotNull {uri ->
            persistPermission(uri);runCatching {importer.read(uri,false)}.getOrNull()
        }
        require(tracks.isNotEmpty());repository.storeTracks(tracks)
        if(controller==null) { withTimeout(10000) { while(controller==null) delay(100) } }
        play(tracks.first(),tracks)
    }
    fun importLyrics(track: Track, uri: Uri) = launchOperation {
        try {
            val path=importer.importLyrics(track.id,uri)
            repository.setLyrics(track.id,path);updatePlayback();app.messages.tryEmit(R.string.lyrics_imported)
        } catch(cancel: CancellationException) {throw cancel}
        catch(error: Throwable) {report(R.string.invalid_lyrics,error)}
    }
    suspend fun lyricsFor(track: Track): List<LyricLine> = LrcParser.parse(lyricText(track))
    suspend fun lyricText(track: Track): String = withContext(Dispatchers.IO) {
        if(track.lyrics=="none:") return@withContext ""
        if(track.providerId.startsWith("lan:") && track.lyrics.isBlank()) return@withContext app.lan.lyrics(track)
        // 为旧导入记录补读源文件，不要求重新导入，不生成 LRC；手动取消关联仍优先返回。
        if(track.lyrics.isBlank() && track.providerId.isBlank()) return@withContext embeddedLock.withLock {
            embeddedCache[track.uri] ?: EmbeddedLyrics.read(app,Uri.parse(track.uri)).also {text ->
                embeddedCache[track.uri]=text
                while(embeddedCache.size>16 || embeddedCache.values.sumOf {it.length}>1_000_000) embeddedCache.remove(embeddedCache.keys.first())
            }
        }
        val file=File(track.lyrics)
        if(track.lyrics.startsWith("embedded:")) track.lyrics.removePrefix("embedded:") else if(file.isAbsolute && file.isFile) file.readText() else if(track.providerId.isNotEmpty()) track.lyrics else ""
    }
    fun exportLyrics(track: Track, uri: Uri) = launchOperation {
        val text=lyricText(track);require(text.isNotBlank())
        withContext(Dispatchers.IO) {app.contentResolver.openOutputStream(uri,"wt")?.bufferedWriter()?.use {it.write(text)} ?: error("Output unavailable")}
        app.messages.tryEmit(R.string.export_complete)
    }
    fun exportPlaylist(playlist: Playlist, uri: Uri) = launchOperation {
        val byId=library.value.tracks.associateBy {it.id}
        val text=buildString {
            append("#EXTM3U\n")
            playlist.trackIds.mapNotNull {byId[it]}.forEach {
                append("#EXTINF:").append(it.durationMs/1000).append(',').append(it.title.replace("\n"," ")).append("\n")
                append(it.uri).append("\n")
            }
        }
        withContext(Dispatchers.IO) {app.contentResolver.openOutputStream(uri,"wt")?.bufferedWriter()?.use {it.write(text)} ?: error("Output unavailable")}
        app.messages.tryEmit(R.string.export_complete)
    }
    fun backup(uri: Uri) = launchOperation {
        _busy.value=true
        try {LibraryBackup(app,repository).export(uri);app.messages.tryEmit(R.string.backup_done)}
        finally {_busy.value=false}
    }
    fun restoreBackup(uri: Uri) = launchOperation {
        _busy.value=true
        try {
            controller?.pause()
            LibraryBackup(app,repository).restore(uri)
            controller?.stop();controller?.clearMediaItems();updatePlayback()
            app.messages.tryEmit(R.string.backup_restored)
        } finally {_busy.value=false}
    }
    override fun onCleared() { controller?.removeListener(listener);MediaController.releaseFuture(future);super.onCleared() }
}
