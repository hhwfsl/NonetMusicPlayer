package app.nonetmusicplayer.android.updates

import android.app.*
import android.content.*
import android.content.pm.ServiceInfo
import android.os.*
import androidx.core.app.NotificationCompat
import app.nonetmusicplayer.android.*
import kotlinx.coroutines.*
import java.util.Locale

/** 用户发起的下载由 dataSync 服务承载；前后台共用一个任务、一个进度与取消入口。 */
internal class UpdateDownloadService: Service() {
    private val app get()=application as MusicApplication
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    private var transfer="";private var finished=false
    override fun onBind(intent: Intent?): IBinder?=null
    override fun onStartCommand(intent: Intent?,flags: Int,startId: Int): Int {
        if(intent?.action==CANCEL) {
            app.updates.cancel(intent.getStringExtra("transfer").orEmpty())
            if(!app.updates.state.value.active) {finished=true;stopSelf()}
            return START_NOT_STICKY
        }
        val notification=UpdateNotifications.build(app,app.updates.state.value)
        if(Build.VERSION.SDK_INT>=29) startForeground(UpdateNotifications.ID,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else startForeground(UpdateNotifications.ID,notification)
        val id=intent?.getStringExtra("transfer").orEmpty()
        if(transfer.isNotEmpty()) return START_NOT_STICKY
        transfer=id
        scope.launch {app.updates.state.collect {if(it.active) UpdateNotifications.post(app,it)}}
        app.updates.runTransfer(id) {state ->
            finished=true;stopForeground(STOP_FOREGROUND_REMOVE)
            if(state.phase==UpdatePhase.Ready || state.phase==UpdatePhase.Error) UpdateNotifications.post(app,state)
            stopSelf()
        }
        return START_NOT_STICKY
    }
    override fun onTimeout(startId: Int,fgsType: Int) {app.updates.cancel(transfer);stopSelf()}
    override fun onDestroy() {if(!finished) app.updates.cancel(transfer);scope.cancel();super.onDestroy()}
    companion object {
        const val START="nonet.update.start";const val CANCEL="nonet.update.cancel"
        const val SHOW="nonet.update.show";const val INSTALL="nonet.update.install"
    }
}

@androidx.annotation.OptIn(markerClass=[androidx.media3.common.util.UnstableApi::class])
internal object UpdateNotifications {
    const val ID=305
    private fun context(app: MusicApplication): Context {
        val lang=app.repository.state.value.preferences.language
        return if(lang=="system") app else app.createConfigurationContext(android.content.res.Configuration(app.resources.configuration).apply {setLocale(Locale.forLanguageTag(lang))})
    }
    fun build(app: MusicApplication,state: UpdateState): Notification {
        val c=context(app);val manager=app.getSystemService(NotificationManager::class.java)
        val ready=state.phase==UpdatePhase.Ready;val channel=if(state.active) "nonet-update-progress" else "nonet-update-result"
        manager.createNotificationChannel(NotificationChannel(channel,c.getString(R.string.update_title),if(state.active) NotificationManager.IMPORTANCE_LOW else NotificationManager.IMPORTANCE_DEFAULT))
        val open=Intent(app,MainActivity::class.java).setAction(if(ready) UpdateDownloadService.INSTALL else UpdateDownloadService.SHOW)
        val pending=PendingIntent.getActivity(app,305,open,PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val total=state.release?.size ?: 0
        val body=when(state.phase) {
            UpdatePhase.Ready ->c.getString(R.string.update_ready)
            UpdatePhase.Error ->c.getString(state.error.takeIf {it!=0} ?: R.string.update_download_failed)
            UpdatePhase.Verifying ->c.getString(R.string.update_verifying)
            UpdatePhase.Cancelling ->c.getString(R.string.update_cancelling)
            else ->c.getString(R.string.update_progress,if(total>0) (state.received*100/total).toInt() else 0)
        }
        val builder=NotificationCompat.Builder(app,channel).setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle(c.getString(R.string.update_title)).setContentText(body).setContentIntent(pending).setOnlyAlertOnce(true).setOngoing(state.active).setAutoCancel(!state.active)
            .setExtras(Bundle().apply {putString("nonet.update.transfer",state.transferId)})
        if(state.active) {
            builder.setProgress(100,if(total>0) (state.received*100/total).toInt() else 0,total==0L || state.phase==UpdatePhase.Verifying)
            val cancel=Intent(app,UpdateDownloadService::class.java).setAction(UpdateDownloadService.CANCEL).setData(android.net.Uri.parse("nonet-update:"+state.transferId)).putExtra("transfer",state.transferId)
            builder.addAction(0,c.getString(R.string.update_cancel_download),PendingIntent.getService(app,306,cancel,PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
        } else if(ready) builder.addAction(0,c.getString(R.string.update_install),pending)
        return builder.build()
    }
    fun post(app: MusicApplication,state: UpdateState) {runCatching {app.getSystemService(NotificationManager::class.java).notify(ID,build(app,state))}}
    fun clear(app: Context) {app.getSystemService(NotificationManager::class.java).cancel(ID)}
}
