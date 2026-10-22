package app.nonetmusicplayer.android.network

import android.net.Uri
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.*
import androidx.media3.datasource.okhttp.OkHttpDataSource

/** 每次打开使用已配对的直接 HTTP Call；由 Media3 关闭/取消，避免嵌套请求拖延快速切歌。 */
@UnstableApi
internal class PeerMediaDataSource(private val manager: LanManager): DataSource {
    private val listeners=mutableListOf<TransferListener>()
    private var source: DataSource?=null
    override fun addTransferListener(listener: TransferListener) {listeners+=listener;source?.addTransferListener(listener)}
    override fun open(spec: DataSpec): Long {
        val active=OkHttpDataSource.Factory(manager.mediaClient(spec.uri)).createDataSource()
        source=active;listeners.forEach(active::addTransferListener)
        return active.open(spec)
    }
    override fun read(buffer: ByteArray,offset: Int,length: Int)=requireNotNull(source).read(buffer,offset,length)
    override fun getUri(): Uri?=source?.uri
    override fun getResponseHeaders(): Map<String,List<String>> = source?.responseHeaders ?: emptyMap()
    override fun close() {source?.close();source=null}
}
