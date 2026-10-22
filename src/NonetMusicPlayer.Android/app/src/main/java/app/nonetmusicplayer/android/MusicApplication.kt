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
    val messages=MutableSharedFlow<Int>(extraBufferCapacity=16)
    val playQueue=kotlinx.coroutines.flow.MutableStateFlow<List<String>>(emptyList())
    override fun onCreate() {
        super.onCreate();log=AppLog(this);repository=MusicRepository(this,scope)
        lan=app.nonetmusicplayer.android.network.LanManager(this)
        updates=app.nonetmusicplayer.android.updates.AppUpdates(this)
        scope.launch {repository.removePeerEphemeral()}
        log.write("INFO","Application started")
    }
    override fun newImageLoader(): ImageLoader = ImageLoader.Builder(this).okHttpClient(lan.http)
        .memoryCache {MemoryCache.Builder(this).maxSizePercent(.05).build()}
        .diskCache {DiskCache.Builder().directory(java.io.File(cacheDir,"artwork")).maxSizeBytes(32L*1024*1024).build()}.build()
}
