package app.nonetmusicplayer.android.network

import android.app.*
import android.content.Intent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.IntentFilter
import android.net.ConnectivityManager
import android.net.Network
import android.content.pm.ServiceInfo
import android.os.*
import androidx.core.app.NotificationCompat
import app.nonetmusicplayer.android.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.collectLatest

/** 用户主动共享期间独立保活；不创建播放器或改变播放状态。 */
@androidx.annotation.OptIn(markerClass=[androidx.media3.common.util.UnstableApi::class])
class PeerSharingService: Service() {
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.Main.immediate)
    private lateinit var connectionLease: PeerConnectionLease
    private val networkChanged=object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {(application as MusicApplication).lan.retryConnections()}
    }
    private val wakeChanged=object : BroadcastReceiver() {
        override fun onReceive(context: Context,intent: Intent) {
            if(!getSystemService(PowerManager::class.java).isDeviceIdleMode) (application as MusicApplication).lan.retryConnections()
        }
    }
    override fun onCreate() {
        super.onCreate();getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel("nonet-lan",getString(R.string.lan_title),NotificationManager.IMPORTANCE_LOW))
        val flags=PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        val open=PendingIntent.getActivity(this,0,Intent(this,MainActivity::class.java),flags)
        val stop=PendingIntent.getService(this,1,Intent(this,PeerSharingService::class.java).setAction("stop"),flags)
        val n=NotificationCompat.Builder(this,"nonet-lan").setSmallIcon(android.R.drawable.ic_media_play).setContentTitle(getString(R.string.lan_sharing)).setContentText(getString(R.string.lan_notification)).setContentIntent(open).setOngoing(true).setSilent(true).addAction(0,getString(R.string.lan_stop),stop).build()
        if(Build.VERSION.SDK_INT>=29) startForeground(204,n,ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE) else startForeground(204,n)
        connectionLease=PeerConnectionLease(applicationContext)
        // 使用 Android 标准网络/休眠事件恢复探测，不依赖厂商白名单或 Activity 获得焦点。
        getSystemService(ConnectivityManager::class.java).registerDefaultNetworkCallback(networkChanged)
        val wakeFilter=IntentFilter(Intent.ACTION_SCREEN_ON).apply {addAction(PowerManager.ACTION_DEVICE_IDLE_MODE_CHANGED)}
        if(Build.VERSION.SDK_INT>=33) registerReceiver(wakeChanged,wakeFilter,RECEIVER_NOT_EXPORTED) else registerReceiver(wakeChanged,wakeFilter)
        scope.launch {
            (application as MusicApplication).lan.state.map {it.sharing && (it.connecting || it.incoming!=null || it.peers.isNotEmpty())}.distinctUntilChanged().collectLatest {connected ->
                try {
                    if(connected) while(isActive) {
                        try {connectionLease.renew()} catch(error: Exception) {(application as MusicApplication).log.write("WARN","LAN background protection unavailable",error)}
                        delay(45_000)
                    }
                } finally {connectionLease.close()}
            }
        }
        scope.launch {try {(application as MusicApplication).lan.startServer()} catch(e: CancellationException) {throw e} catch(error: Exception) {val app=application as MusicApplication;app.log.write("ERROR","LAN server failed",error);app.messages.tryEmit(R.string.lan_start_failed);stopSelf()}}
    }
    override fun onStartCommand(intent: Intent?,flags: Int,startId: Int): Int {if(intent?.action=="stop") stopSelf();return START_NOT_STICKY}
    override fun onBind(intent: Intent?): IBinder?=null
    override fun onTaskRemoved(rootIntent: Intent?) { (application as MusicApplication).lan.stopServer();stopSelf();super.onTaskRemoved(rootIntent) }
    override fun onDestroy() {
        runCatching {getSystemService(ConnectivityManager::class.java).unregisterNetworkCallback(networkChanged)}
        runCatching {unregisterReceiver(wakeChanged)}
        (application as MusicApplication).lan.stopServer();scope.cancel();if(::connectionLease.isInitialized) connectionLease.close();stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy()
    }
}
