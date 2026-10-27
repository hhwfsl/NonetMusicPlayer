package app.nonetmusicplayer.android.playback

import android.app.ActivityManager
import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.SystemClock
import androidx.media3.common.util.UnstableApi
import app.nonetmusicplayer.android.MusicApplication
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow

internal data class SleepTimerState(val remainingSeconds: Long=0,val active: Boolean=false,val waitingForSong: Boolean=false)

/** 单调时钟负责进程内计时；系统闹钟作休眠兜底，不申请精确闹钟权限。 */
@androidx.annotation.OptIn(markerClass=[UnstableApi::class])
internal class SleepTimer(private val app: MusicApplication) {
    private val preferences=app.getSharedPreferences("sleep_timer",Context.MODE_PRIVATE)
    private val alarm=app.getSystemService(AlarmManager::class.java)
    private val pending=PendingIntent.getBroadcast(app,0,Intent(app,SleepTimerReceiver::class.java),PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    private val mutable=MutableStateFlow(SleepTimerState());val state=mutable.asStateFlow()
    private var deadline=0L;private var finishSong=false;private var tick: Job?=null
    private var onDeadline: ((Boolean)->Boolean)?=null
    private var onCancel: (()->Unit)?=null
    private var onClose: (suspend ()->Unit)?=null
    private var completed=false
    var closing=false;private set
    fun onNewTask() {if(completed) {closing=false;completed=false}}
    init {
        val epoch=preferences.getLong("deadline",0)
        if(epoch>0) arm((epoch-System.currentTimeMillis()).coerceIn(1,86_400_000),preferences.getBoolean("finishSong",false))
    }
    fun start(minutes: Int,finishCurrentSong: Boolean) {
        require(minutes in 1..1440)
        if(!closing) arm(minutes*60_000L,finishCurrentSong)
    }
    /** 毫秒入口供后台服务回归测试；产品界面只接受分钟。 */
    internal fun arm(durationMs: Long,finishCurrentSong: Boolean) {
        require(durationMs in 1..86_400_000)
        cancel();finishSong=finishCurrentSong;deadline=SystemClock.elapsedRealtime()+durationMs
        preferences.edit().putLong("deadline",System.currentTimeMillis()+durationMs).putBoolean("finishSong",finishSong).apply()
        mutable.value=SleepTimerState((durationMs+999)/1000,true,false)
        runCatching {alarm.setAndAllowWhileIdle(AlarmManager.ELAPSED_REALTIME_WAKEUP,deadline,pending)}.onFailure {app.log.write("WARN","Sleep timer alarm unavailable; playback service timer remains active",it)}
        tick=app.scope.launch {
            while(isActive && deadline>0) {
                val remaining=deadline-SystemClock.elapsedRealtime()
                if(remaining<=0) {expire();return@launch}
                mutable.value=SleepTimerState((remaining+999)/1000,true,false)
                delay(minOf(1000,remaining))
            }
        }
        app.log.write("INFO","Sleep timer started")
    }
    fun cancel() {
        tick?.cancel();tick=null;deadline=0;alarm.cancel(pending)
        preferences.edit().clear().apply();mutable.value=SleepTimerState();onCancel?.invoke()
    }
    internal fun alarmReached() {if(deadline>0 && SystemClock.elapsedRealtime()>=deadline) expire()}
    private fun expire() {
        if(deadline==0L || closing) return
        deadline=0;alarm.cancel(pending)
        // 到时只等待这一首，状态由服务的播放代次绑定，单曲循环也不能开始第二轮。
        if(onDeadline?.invoke(finishSong)==true) {
            mutable.value=SleepTimerState(0,true,true)
            // 进程若被系统回收，下次闹钟恢复直接退出，不无限重播上一首。
        } else finish()
    }
    fun attach(onDeadline: (Boolean)->Boolean,onCancel: ()->Unit,onClose: suspend ()->Unit) {
        this.onDeadline=onDeadline;this.onCancel=onCancel;this.onClose=onClose
    }
    fun detach() {
        onDeadline=null;onCancel=null;onClose=null
        if(state.value.waitingForSong && !closing) finish()
    }
    fun finish() {
        if(closing) return
        closing=true;completed=false
        val saveAndStop=onClose;cancel()
        app.log.write("INFO","Sleep timer elapsed; closing playback and application")
        app.scope.launch {
            runCatching {saveAndStop?.invoke()}.onFailure {app.log.write("ERROR","Sleep timer playback shutdown failed",it)}
            withTimeoutOrNull(3500) {app.lan.disconnectAll()}
            app.lan.setSharing(false);app.updates.cancel()
            app.stopService(Intent(app,PlaybackService::class.java))
            // 使用 Android 的任务退出语义，不杀进程、不清除音乐或用户数据。
            app.getSystemService(ActivityManager::class.java).appTasks.forEach {task ->runCatching {task.finishAndRemoveTask()}}
            completed=true
        }
    }
}

/** 系统休眠期间不依赖 Activity；未导出的接收器只消费本应用创建的闹钟。 */
class SleepTimerReceiver: BroadcastReceiver() {
    override fun onReceive(context: Context,intent: Intent) {(context.applicationContext as MusicApplication).sleepTimer.alarmReached()}
}
