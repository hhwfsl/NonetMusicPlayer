package app.nonetmusicplayer.android

import android.app.Application
import app.nonetmusicplayer.android.data.AppLog
import app.nonetmusicplayer.android.data.MusicRepository
import coil.ImageLoader
import coil.ImageLoaderFactory
import coil.memory.MemoryCache
import coil.disk.DiskCache
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableSharedFlow

/** 应用级对象不引用 Activity，防止后台播放长期持有界面。 */
class MusicApplication : Application(), ImageLoaderFactory {
    val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    lateinit var repository: MusicRepository; private set
    lateinit var log: AppLog; private set
    lateinit var lan: app.nonetmusicplayer.android.network.LanManager; private set
    internal lateinit var updates: app.nonetmusicplayer.android.updates.AppUpdates; private set
    internal lateinit var sleepTimer: app.nonetmusicplayer.android.playback.SleepTimer; private set
    fun canPlay(track: app.nonetmusicplayer.android.domain.Track)=lan.available(track)
    val messages=MutableSharedFlow<Int>(extraBufferCapacity=16)
    val playQueue=kotlinx.coroutines.flow.MutableStateFlow<List<String>>(emptyList())
    override fun onCreate() {
        super.onCreate();log=AppLog(this);repository=MusicRepository(this,scope)
        lan=app.nonetmusicplayer.android.network.LanManager(this)
        updates=app.nonetmusicplayer.android.updates.AppUpdates(this)
        sleepTimer=app.nonetmusicplayer.android.playback.SleepTimer(this)
        // 移除旧登录密文和专用密钥；不触碰本地音频及设备互听凭据。
        scope.launch(Dispatchers.IO) {
            getSharedPreferences("cloud_session",MODE_PRIVATE).edit().clear().commit()
            runCatching {java.security.KeyStore.getInstance("AndroidKeyStore").apply {load(null)}.deleteEntry("Nonet.Netease")}
        }
        scope.launch {repository.removePeerEphemeral()}
        log.write("INFO","Application started")
    }
    override fun newImageLoader(): ImageLoader = ImageLoader.Builder(this).okHttpClient(lan.http)
        .memoryCache {MemoryCache.Builder(this).maxSizePercent(.05).build()}
        .diskCache {DiskCache.Builder().directory(java.io.File(cacheDir,"artwork")).maxSizeBytes(32L*1024*1024).build()}.build()
}
