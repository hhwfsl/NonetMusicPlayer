package app.nonetmusicplayer.android.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.PowerManager
import java.io.Closeable

/** 连接由前台服务持有，不依赖 Activity；空闲共享不持锁，连接结束后立即释放。 */
internal class PeerConnectionLease(context: Context): Closeable {
    private val power=context.getSystemService(PowerManager::class.java)
    private val connectivity=context.getSystemService(ConnectivityManager::class.java)
    private val wake=power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"Nonet:lan-connection").apply {setReferenceCounted(false)}
    private var network: ConnectivityManager.NetworkCallback?=null

    @Suppress("DEPRECATION")
    fun renew() {
        // 有界租期由服务续租；即使协程意外退出，也不会留下永久 CPU 锁。
        wake.acquire(120_000)
        // 保留已连接的 Wi-Fi 网络，不绑定整个进程，不强制打开无线网络，也不绕过 VPN。
        // 不使用 Android 34 起仅限前台亮屏的低延迟锁来冒充后台保活。
        if(network==null && connectivity.allNetworks.any {connectivity.getNetworkCapabilities(it)?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)==true}) {
            val callback=object : ConnectivityManager.NetworkCallback() {}
            connectivity.requestNetwork(NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_WIFI).build(),callback)
            network=callback
        }
    }
    override fun close() {
        network?.let {runCatching {connectivity.unregisterNetworkCallback(it)}};network=null
        if(wake.isHeld) wake.release()
    }
}
