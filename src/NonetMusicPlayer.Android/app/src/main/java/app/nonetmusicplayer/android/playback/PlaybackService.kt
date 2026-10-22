package app.nonetmusicplayer.android.playback

import android.app.PendingIntent
import android.content.Intent
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioManager
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.os.Process
import android.os.Looper
import android.os.SystemClock
import androidx.media3.common.*
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.okhttp.OkHttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.session.*
import app.nonetmusicplayer.android.*
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.PeerMediaDataSource
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import kotlinx.coroutines.*
import java.io.File

/** 音频解码和媒体会话仅由服务拥有；界面旋转或退出不会创建第二个播放器。 */
@UnstableApi
class PlaybackService : MediaSessionService() {
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    private val app get()=application as MusicApplication
    private lateinit var player: ExoPlayer
    private val decodeThread = HandlerThread("Nonet-audio", Process.THREAD_PRIORITY_AUDIO)
    private var session: MediaSession?=null
    private var queue=emptyList<String>()
    private var origin=""
    private val shuffle=WeightedShuffle()
    private val failed=mutableSetOf<String>()
    private var counted=false
    private var revision=0
    private var generation=0
    private var networkResume: Triple<String,Long,Boolean>?=null
    private var lastStatsAt=SystemClock.elapsedRealtime()
    private var lastStatsId=""
    private var lastStatsPlaying=false
    private val pendingDuration=mutableMapOf<String,Long>()
    private lateinit var audioManager: AudioManager
    private var outputIds=emptySet<Int>()
    private val deviceCallback=object : AudioDeviceCallback() {
        override fun onAudioDevicesAdded(added: Array<out AudioDeviceInfo>) = checkDevices()
        override fun onAudioDevicesRemoved(removed: Array<out AudioDeviceInfo>) = checkDevices()
    }
    override fun onCreate() {
        super.onCreate()
        val source=DefaultMediaSourceFactory(DefaultDataSource.Factory(this,DataSource.Factory {PeerMediaDataSource(app.lan)}))
        // 有界缓冲按需读取音频，不把整首歌解码为内存数组。
        val buffers=DefaultLoadControl.Builder().setBufferDurationsMs(15000,45000,250,2500)
            .setTargetBufferBytes(8*1024*1024).setPrioritizeTimeOverSizeThresholds(true).build()
        // 仅播放/缓冲时保持 CPU 运行，暂停即释放，支持灭屏后的后台解码。
        // 解码线程使用音频优先级；界面切换和动画不参与音频时钟调度。
        decodeThread.start()
        player=ExoPlayer.Builder(this).setPlaybackLooper(decodeThread.looper).setMediaSourceFactory(source).setLoadControl(buffers).setWakeMode(C.WAKE_MODE_LOCAL).build().apply {
            setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(),true)
            setHandleAudioBecomingNoisy(true)
            repeatMode=Player.REPEAT_MODE_OFF
            addListener(object : Player.Listener {
                override fun onPlaybackStateChanged(state: Int) {
                    if(BuildConfig.DEBUG) android.util.Log.d("LmpPlayback","state=$state playing=${isPlaying} ready=${playWhenReady} position=${currentPosition}")
                    collectListening()
                    if(state==Player.STATE_ENDED) {
                        val ended=generation;failed.clear()
                        Handler(mainLooper).post {if(generation==ended && playbackState==Player.STATE_ENDED) advance(1,false)}
                    }
                }
                override fun onIsPlayingChanged(isPlaying: Boolean) {
                    if(BuildConfig.DEBUG) android.util.Log.d("LmpPlayback","isPlaying=$isPlaying position=${currentPosition}")
                    collectListening()
                    if(isPlaying && !counted) {
                        counted=true
                        currentMediaItem?.mediaId?.let { id -> scope.launch { app.repository.recordStarted(id) } }
                    }
                    persist()
                }
                override fun onPlayerError(error: PlaybackException) {
                    collectListening()
                    val track=app.repository.state.value.tracks.firstOrNull {it.id==currentMediaItem?.mediaId}
                    if(track?.providerId?.startsWith("lan:")==true && (error.errorCode in 2000..2008 || !app.lan.available(track))) {
                        suspendForNetwork(track);app.lan.noteTransportFailure(track)
                        app.log.write("WARN","Remote audio temporarily unavailable; track and position retained")
                        return
                    }
                    app.log.write("ERROR","Audio playback failed",error);app.messages.tryEmit(R.string.playback_failed)
                    currentMediaItem?.mediaId?.let { failed+=it }
                    val broken=generation
                    Handler(mainLooper).post {if(generation==broken) advance(1,true)}
                }
            })
        }
        // 状态包装器统一生成事件，避免 Kotlin 委托 Java 默认回调时丢失播放状态。
        val states=object : ForwardingSimpleBasePlayer(player) {
            override fun getState(): SimpleBasePlayer.State {
                val state=super.getState()
                val commands=state.availableCommands.buildUpon()
                    .add(Player.COMMAND_SEEK_TO_NEXT).add(Player.COMMAND_SEEK_TO_NEXT_MEDIA_ITEM)
                    .add(Player.COMMAND_SEEK_TO_PREVIOUS).add(Player.COMMAND_SEEK_TO_PREVIOUS_MEDIA_ITEM).build()
                return state.buildUpon().setAvailableCommands(commands).build()
            }
        }
        val facade=object : ForwardingPlayer(states) {
            override fun play() {resumeCurrent()}
            override fun pause() {networkResume=null;player.pause()}
            override fun seekToNext() { advance(1,true) }
            override fun seekToNextMediaItem() { advance(1,true) }
            override fun seekToPrevious() { advance(-1,true) }
            override fun seekToPreviousMediaItem() { advance(-1,true) }
        }
        val intent=PendingIntent.getActivity(this,0,Intent(this,MainActivity::class.java),PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        session=MediaSession.Builder(this,facade).setSessionActivity(intent).setCallback(object : MediaSession.Callback {
            override fun onConnect(session: MediaSession, controller: MediaSession.ControllerInfo): MediaSession.ConnectionResult {
                if(controller.packageName!=packageName && !controller.isTrusted) return MediaSession.ConnectionResult.reject()
                val commands=MediaSession.ConnectionResult.DEFAULT_SESSION_COMMANDS.buildUpon()
                if(controller.packageName==packageName) commands.add(PLAY_QUEUE).add(NEXT).add(PREVIOUS)
                return MediaSession.ConnectionResult.AcceptedResultBuilder(session).setAvailableSessionCommands(commands.build()).build()
            }
            override fun onCustomCommand(session: MediaSession, controller: MediaSession.ControllerInfo, command: SessionCommand, args: Bundle): ListenableFuture<SessionResult> {
                if(controller.packageName!=packageName) return Futures.immediateFuture(SessionResult(SessionError.ERROR_NOT_SUPPORTED))
                if(command.customAction==NEXT.customAction || command.customAction==PREVIOUS.customAction) {
                    advance(if(command.customAction==NEXT.customAction) 1 else -1,true)
                    return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
                }
                if(command.customAction!=PLAY_QUEUE.customAction) return Futures.immediateFuture(SessionResult(SessionError.ERROR_NOT_SUPPORTED))
                val ids=(args.getStringArrayList("ids") ?: app.playQueue.value).take(10000)
                val selected=args.getString("track").orEmpty()
                val available=app.repository.state.value.tracks.filter {app.lan.available(it)}.map {it.id}.toSet()
                if(selected !in ids || selected !in available) return Futures.immediateFuture(SessionResult(SessionError.ERROR_BAD_VALUE))
                queue=ids.distinct().filter { it in available };app.playQueue.value=queue;origin=args.getString("source").orEmpty();failed.clear();revision++
                startTrack(selected,0,true)
                return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
        }).build()
        audioManager=getSystemService(AudioManager::class.java)
        outputIds=audioManager.getDevices(AudioManager.GET_DEVICES_OUTPUTS).map { it.id }.toSet()
        audioManager.registerAudioDeviceCallback(deviceCallback,Handler(Looper.getMainLooper()))
        scope.launch {
            val before=revision
            val snapshot=app.repository.restorePlayback()
            if(revision==before && snapshot.trackId.isNotBlank()) {
                queue=snapshot.queueIds;app.playQueue.value=queue;origin=snapshot.source;player.volume=snapshot.volume
                startTrack(snapshot.trackId,snapshot.positionMs,false)
            }
        }
        scope.launch {
            var saved=0
            while(isActive) {
                delay(1000)
                collectListening()
                if(++saved>=5) {
                    flushListening();saved=0;persist()
                }
            }
        }
        scope.launch {app.lan.state.collect {state ->
            val track=app.repository.state.value.tracks.firstOrNull {t ->t.id==player.currentMediaItem?.mediaId}
            if(track!=null && track.providerId.startsWith("lan:")) {
                if(state.peers.none {it.id==track.providerId.removePrefix("lan:")}) {networkResume=null;player.pause()}
                else if(!app.lan.available(track) && player.playWhenReady) suspendForNetwork(track)
                else if(app.lan.available(track)) networkResume?.takeIf {it.first==track.id && it.third}?.let {startTrack(it.first,it.second,true)}
            }
        }}
    }
    /** 临时停网不视为坏歌，也不跳过整张远端歌单；恢复时继续原进度，用户暂停可取消自动继续。 */
    private fun suspendForNetwork(track: Track) {
        if(networkResume==null) networkResume=Triple(track.id,player.currentPosition.coerceAtLeast(0),player.playWhenReady)
        player.pause();persist();app.messages.tryEmit(R.string.lan_reconnecting)
    }
    private fun checkDevices() {
        val current=audioManager.getDevices(AudioManager.GET_DEVICES_OUTPUTS).map { it.id }.toSet()
        if(current!=outputIds && player.isPlaying) { player.pause();app.messages.tryEmit(R.string.output_changed);app.log.write("INFO","Audio output changed; playback paused") }
        outputIds=current
    }
    /** 设备重新配对可能换端口，继续播放时重新解析地址并保留原进度。 */
    private fun resumeCurrent() {
        val item=player.currentMediaItem ?: return
        val track=app.repository.state.value.tracks.firstOrNull {it.id==item.mediaId} ?: return
        if(!app.lan.available(track)) {player.pause();app.messages.tryEmit(R.string.lan_disconnected);return}
        networkResume?.takeIf {it.first==track.id}?.let {startTrack(it.first,it.second,true);return}
        val uri=app.lan.resolve(track)
        if(track.providerId.startsWith("lan:") && item.localConfiguration?.uri.toString()!=uri) {
            val position=player.currentPosition;player.setMediaItem(item.buildUpon().setUri(uri).build(),position);player.prepare()
        } else if(player.playbackState==Player.STATE_IDLE) player.prepare()
        player.play()
    }
    private fun startTrack(id: String, position: Long, play: Boolean) {
        networkResume=null
        val track=app.repository.state.value.tracks.firstOrNull { it.id==id } ?: return
        if(!app.lan.available(track)) {player.pause();app.messages.tryEmit(R.string.lan_disconnected);return}
        collectListening();player.pause();counted=false;generation++
        val metadata=MediaMetadata.Builder().setTitle(track.title).setArtist(track.artist).setAlbumTitle(track.album).setExtras(Bundle().apply { putString("source",origin) }).apply {
            if(track.cover.isNotBlank()) setArtworkUri(if(track.cover.startsWith("https:")) android.net.Uri.parse(track.cover) else android.net.Uri.fromFile(File(track.cover)))
        }.build()
        player.setMediaItem(MediaItem.Builder().setMediaId(track.id).setUri(app.lan.resolve(track)).setMediaMetadata(metadata).build(),position.coerceAtLeast(0))
        player.prepare();player.playWhenReady=play
        persist();app.log.write("INFO","Playback source changed")
    }
    private fun advance(direction: Int, manual: Boolean) {
        val current=player.currentMediaItem?.mediaId
        val allowed=app.repository.state.value.tracks.filter {app.lan.available(it)}.map {it.id}.toSet()
        val candidates=queue.filter { it in allowed && it !in failed }
        if(candidates.isEmpty()) { player.pause();if(failed.isNotEmpty()) app.messages.tryEmit(R.string.no_playable_tracks);return }
        val mode=app.repository.state.value.preferences.mode
        val next=when {
            !manual && mode=="repeat-one" && current in candidates -> current
            direction>0 && mode=="shuffle" -> shuffle.choose(candidates,current)
            else -> candidates[Math.floorMod(candidates.indexOf(current)+direction,candidates.size)]
        }
        next?.let { startTrack(it,0,true) }
    }
    private fun persist() {
        if(!::player.isInitialized) return
        val snapshot=PlaybackSnapshot(player.currentMediaItem?.mediaId.orEmpty(),queue,player.currentPosition.coerceAtLeast(0),player.volume,origin)
        app.scope.launch { app.repository.savePlayback(snapshot) }
    }
    /** 在切歌、暂停及周期保存时结算旧区间，不把旧歌曲时长算到新歌曲上。 */
    private fun collectListening() {
        if(!::player.isInitialized) return
        val now=SystemClock.elapsedRealtime()
        if(lastStatsPlaying && lastStatsId.isNotBlank()) pendingDuration[lastStatsId]=(pendingDuration[lastStatsId] ?: 0)+(now-lastStatsAt).coerceIn(0,3000)
        lastStatsAt=now;lastStatsId=player.currentMediaItem?.mediaId.orEmpty();lastStatsPlaying=player.isPlaying
    }
    private fun flushListening() {
        val batch=pendingDuration.toMap();pendingDuration.clear()
        app.scope.launch {batch.forEach { (id,duration) -> if(duration>0) app.repository.recordDuration(id,duration) }}
    }
    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session
    override fun onDestroy() {
        collectListening();flushListening();persist();audioManager.unregisterAudioDeviceCallback(deviceCallback);scope.cancel();session?.release();player.release();session=null;decodeThread.quitSafely();super.onDestroy()
    }
    companion object {
        val PLAY_QUEUE=SessionCommand("app.nonetmusicplayer.PLAY_QUEUE",Bundle.EMPTY)
        val NEXT=SessionCommand("app.nonetmusicplayer.NEXT",Bundle.EMPTY)
        val PREVIOUS=SessionCommand("app.nonetmusicplayer.PREVIOUS",Bundle.EMPTY)
    }
}
