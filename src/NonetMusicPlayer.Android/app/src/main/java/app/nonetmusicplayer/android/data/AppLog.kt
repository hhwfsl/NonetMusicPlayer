package app.nonetmusicplayer.android.data

import android.content.Context
import android.util.Log
import app.nonetmusicplayer.android.BuildConfig
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime

/** 固定日志策略，不记录文件路径、认证内容和原始服务器响应。 */
class AppLog(context: Context) {
    private val folder=File(context.filesDir,"Logs").apply { mkdirs() }
    // 单线程写日志，避免磁盘写入阻塞界面及媒体会话。
    private val writer = java.util.concurrent.Executors.newSingleThreadExecutor { job ->
        Thread(job, "Nonet-log").apply { isDaemon = true }
    }
    fun write(level: String, event: String, error: Throwable? = null) {
        val message="${LocalDateTime.now()} [$level] $event${error?.let { " (${it.javaClass.simpleName})" } ?: ""}"
        Log.println(if(level=="ERROR") Log.ERROR else Log.INFO,"NonetMusicPlayer",message)
        writer.execute { runCatching {
            val file=File(folder,"${LocalDate.now()}_${BuildConfig.VERSION_NAME}.log")
            if(file.length()>2_000_000) file.writeText(file.readText().takeLast(1_000_000).substringAfter('\n'))
            file.appendText(message+"\n")
            folder.listFiles()?.filter { it.extension=="log" }?.sortedByDescending { it.name }?.drop(7)?.forEach { it.delete() }
        } }
    }
}
