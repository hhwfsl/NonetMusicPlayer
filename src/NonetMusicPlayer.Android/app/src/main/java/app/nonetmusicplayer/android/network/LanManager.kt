package app.nonetmusicplayer.android.network

import android.content.Intent
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import app.nonetmusicplayer.android.MusicApplication
import app.nonetmusicplayer.android.MainActivity
import app.nonetmusicplayer.android.R
import app.nonetmusicplayer.android.domain.*
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.channels.Channel
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.io.*
import java.net.NetworkInterface
import java.security.MessageDigest
import java.util.concurrent.ConcurrentHashMap

data class NearbyPlaylist(val id: String,val name: String,val description: String,val trackIds: List<String>)
data class NearbyDevice(val id: String,val name: String,val playlists: List<NearbyPlaylist>,val trackIds: List<String>,val deviceKind: String = "unknown",val reachable: Boolean=true)
data class NearbyState(val sharing: Boolean=false,val invitations: List<String> = emptyList(),val peers: List<NearbyDevice> = emptyList(),val connecting: Boolean=false,val incoming: IncomingConnection? = null)
private class PeerConnection(val id: String,val name: String,val invitation: PeerInvitation,val http: OkHttpClient,val token: String) {
    @Volatile var revision="";@Volatile var alive=true;val health=PeerHeartbeatPolicy();val retry=Channel<Unit>(Channel.CONFLATED)
    // 媒体请求由自己的 Call 直接持有认证及固定证书，取消不再被内层同步 execute 吞掉。
    val mediaHttp=http.newBuilder().addInterceptor {chain ->
        val request=chain.request();require(request.url.scheme==invitation.origin.scheme && request.url.host==invitation.origin.host && request.url.port==invitation.origin.port)
        chain.proceed(request.newBuilder().header("Authorization","Bearer $token").build())
    }.build()
}
private class PeerSessionRevoked: IOException("Peer session is no longer authorized")

/** 远端设备仅提供音乐数据；不共享数据库、设置、播放状态或本地路径。 */
class LanManager(private val app: MusicApplication) {
    val deviceId=PeerSecurity.deviceId(app);val deviceName=android.os.Build.MODEL.take(100)
    private val _state=MutableStateFlow(NearbyState());val state=_state.asStateFlow()
    private val connections=ConcurrentHashMap<String,PeerConnection>()
    @Volatile private var server: PeerServer?=null
    private var approval: Pair<String,CompletableDeferred<Boolean>>?=null
    private var approvalDeadline=0L
    private val _notices=MutableSharedFlow<String>(extraBufferCapacity=16);val notices=_notices.asSharedFlow()
    private var sourceTracks: List<Track>?=null;private var sourceLists: List<Playlist>?=null;private var cached: JSONObject?=null
    private val plain=OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).build()
    /** 每个请求只向同源已配对设备发送令牌，封面和 Media3 共用此路由。 */
    val http: OkHttpClient=plain.newBuilder().addInterceptor {chain ->
        val r=chain.request();val peer=connections.values.firstOrNull {it.alive && r.url.scheme=="https" && it.invitation.origin.host==r.url.host && it.invitation.origin.port==r.url.port}
        if(peer==null) chain.proceed(r) else peer.http.newCall(r.newBuilder().header("Authorization","Bearer ${peer.token}").build()).execute()
    }.build()
    fun available(t: Track)=t.providerId.isBlank() || t.providerId.startsWith("lan:") && connections[t.providerId.removePrefix("lan:")]?.let {it.alive && it.health.reachable}==true
    internal fun mediaClient(uri: Uri): OkHttpClient = connections.values.firstOrNull {it.alive && uri.scheme=="https" && it.invitation.origin.host==uri.host && it.invitation.origin.port==uri.port}?.mediaHttp ?: plain
    internal fun retryConnections() {connections.values.forEach {it.retry.trySend(Unit)}}
    internal fun noteTransportFailure(t: Track) {connections[t.providerId.removePrefix("lan:")]?.let {it.health.unavailable();publishHealth(it);it.retry.trySend(Unit)}}
    private fun publishHealth(peer: PeerConnection) {
        val old=_state.value.peers.firstOrNull {it.id==peer.id} ?: return
        if(old.reachable==peer.health.reachable) return
        app.log.write("INFO",if(peer.health.reachable) "LAN reachability restored; pairing retained" else "LAN temporarily unavailable; retaining pairing and retrying")
        _state.update {it.copy(peers=it.peers.map {p ->if(p.id==peer.id) p.copy(reachable=peer.health.reachable) else p})}
    }
    fun resolve(t: Track): String {
        if(t.providerId.isBlank()) return t.uri
        require(t.providerId.startsWith("lan:"));val peer=connections[t.providerId.removePrefix("lan:")] ?: error("Device disconnected");require(peer.alive)
        return peer.invitation.origin.newBuilder().addPathSegments("v1/audio").addPathSegment(requireNotNull(Uri.parse(t.uri).lastPathSegment)).build().toString()
    }
    fun setSharing(value: Boolean) {val intent=Intent(app,PeerSharingService::class.java);if(value) androidx.core.content.ContextCompat.startForegroundService(app,intent) else app.stopService(intent)}
    internal suspend fun startServer() {
        val (ssl,pin)=withContext(Dispatchers.IO) {PeerSecurity.identity()}
        // 在主线程交接所有权；服务已取消时不会把后台生成的监听器重新挂回应用。
        withContext(Dispatchers.Main.immediate) {if(server==null) {server=PeerServer(deviceId,deviceName,ssl,pin,::catalog,::openLocal,{app.log.write("WARN","LAN request failed",it)},::confirmConnection,
            {app.repository.state.value.preferences.lanApprovalWaitSeconds},::acceptReverse,{id ->connections[id]?.let {p ->app.scope.launch {disconnect(p,"remote")}}},
            {id ->connections[id]?.let {it.health.contact(android.os.SystemClock.elapsedRealtime());publishHealth(it)}});publishSharing()}}
    }
    @androidx.annotation.OptIn(markerClass=[androidx.media3.common.util.UnstableApi::class])
    private suspend fun confirmConnection(request: IncomingConnection): Boolean = withContext(Dispatchers.Main.immediate) {
        val result=CompletableDeferred<Boolean>();approval=request.requestId to result;_state.update {it.copy(incoming=request)}
        approvalDeadline=android.os.SystemClock.elapsedRealtime()+request.waitSeconds*1000L
        val manager=app.getSystemService(android.app.NotificationManager::class.java)
        val wake=app.getSystemService(android.os.PowerManager::class.java).newWakeLock(android.os.PowerManager.PARTIAL_WAKE_LOCK,"Nonet:pairing").apply {setReferenceCounted(false)}
        try {
            runCatching {wake.acquire(request.waitSeconds*1000L+5000)}
            manager.createNotificationChannel(android.app.NotificationChannel("nonet-lan-pair",app.getString(R.string.lan_pair_title),android.app.NotificationManager.IMPORTANCE_HIGH))
            val open=android.app.PendingIntent.getActivity(app,205,Intent(app,MainActivity::class.java),android.app.PendingIntent.FLAG_UPDATE_CURRENT or android.app.PendingIntent.FLAG_IMMUTABLE)
            fun action(accept: Boolean)=android.app.PendingIntent.getBroadcast(app,if(accept) 206 else 207,
                Intent(app,PairingActionReceiver::class.java).setAction(if(accept) "nonet.lan.accept" else "nonet.lan.reject").setData(Uri.parse("nonet-pair:"+request.requestId)).putExtra("requestId",request.requestId),
                android.app.PendingIntent.FLAG_UPDATE_CURRENT or android.app.PendingIntent.FLAG_IMMUTABLE)
            if(androidx.core.app.NotificationManagerCompat.from(app).areNotificationsEnabled()) {
                val notification=androidx.core.app.NotificationCompat.Builder(app,"nonet-lan-pair").setSmallIcon(android.R.drawable.ic_dialog_info)
                    .setContentTitle(app.getString(R.string.lan_pair_title)).setContentText(request.name).setContentIntent(open).setAutoCancel(true).setTimeoutAfter(request.waitSeconds*1000L)
                    .addAction(0,app.getString(R.string.lan_reject),action(false)).addAction(0,app.getString(R.string.lan_accept),action(true)).build()
                runCatching {manager.notify(205,notification)}
            }
            withTimeoutOrNull(request.waitSeconds*1000L) {result.await()} == true
        } finally {runCatching {manager.cancel(205)};runCatching {if(wake.isHeld) wake.release()};if(approval?.first==request.requestId) approval=null;_state.update {if(it.incoming?.requestId==request.requestId) it.copy(incoming=null) else it}}
    }
    fun answerConnection(requestId: String,accepted: Boolean) {approval?.takeIf {it.first==requestId}?.second?.complete(accepted && android.os.SystemClock.elapsedRealtime()<=approvalDeadline)}
    internal fun stopServer() {server?.close();server=null;approval?.second?.complete(false);connections.values.toList().forEach {p ->app.scope.launch {disconnect(p)}};publishSharing()}
    fun renew() {server?.renew();publishSharing()}
    private fun publishSharing() {
        val s=server;val addresses=runCatching {NetworkInterface.getNetworkInterfaces().toList().filter {it.isUp && !it.isLoopback}.flatMap {it.inetAddresses.toList()}.mapNotNull {it.hostAddress}.filter {PeerSecurity.privateAddress(it) && !it.startsWith("127.")}}.getOrDefault(emptyList())
        _state.update {it.copy(sharing=s!=null,invitations=if(s==null) emptyList() else addresses.distinct().map(s::invitation))}
    }
    @Synchronized private fun catalog(): JSONObject {
        val state=app.repository.state.value;if(sourceTracks===state.tracks && sourceLists===state.playlists) cached?.let {return it}
        val ids=state.playlists.flatMap {it.trackIds}.toSet();val tracks=state.tracks.filter {it.inLibrary && it.providerId.isBlank() && it.id in ids}.take(20000);val localIds=tracks.map {it.id}.toSet()
        val songs=JSONArray().apply {tracks.forEach {put(JSONObject().put("id",it.id).put("title",it.title).put("artist",it.artist).put("album",it.album).put("durationMs",it.durationMs).put("artwork",it.cover.isNotBlank()).put("lyrics",it.lyrics.isNotBlank() && it.lyrics!="none:"))}}
        val lists=JSONArray().apply {state.playlists.take(2000).forEach {p ->put(JSONObject().put("id",p.id).put("name",if(p.id=="liked") app.getString(R.string.liked) else p.name).put("description",p.description).put("trackIds",JSONArray(p.trackIds.filter {it in localIds})))}}
        val c=JSONObject().put("deviceId",deviceId).put("name",deviceName).put("deviceKind","mobile").put("tracks",songs).put("playlists",lists).put("total",tracks.size);c.put("revision",hash(c.toString()))
        sourceTracks=state.tracks;sourceLists=state.playlists;cached=c;return c
    }
    private suspend fun openLocal(kind: String,id: String): PeerPayload?=withContext(Dispatchers.IO) {
        val s=app.repository.state.value;val t=s.tracks.firstOrNull {it.id==id && it.providerId.isBlank() && it.inLibrary && s.playlists.any {p ->id in p.trackIds}} ?: return@withContext null
        when(kind) {
            "lyrics" -> {val text=when {t.lyrics=="none:" ->"";t.lyrics.startsWith("embedded:") ->t.lyrics.removePrefix("embedded:");File(t.lyrics).isFile && File(t.lyrics).length()<=2_000_000 ->File(t.lyrics).readText();else ->""};val bytes=text.toByteArray();if(bytes.isEmpty() || bytes.size>2_000_000) null else PeerPayload(ByteArrayInputStream(bytes),bytes.size.toLong(),"text/plain; charset=utf-8")}
            "artwork" ->File(t.cover).takeIf {it.isFile && it.length()<=5_000_000}?.let {PeerPayload(it.inputStream(),it.length(),if(it.extension=="png") "image/png" else "image/jpeg")}
            "audio" -> {val uri=Uri.parse(t.uri);if(uri.scheme=="file") File(requireNotNull(uri.path)).let {PeerPayload(it.inputStream(),it.length(),"application/octet-stream")} else {
                val fd=app.contentResolver.openFileDescriptor(uri,"r") ?: return@withContext null;var length=fd.statSize
                if(length<0) length=app.contentResolver.query(uri,arrayOf(OpenableColumns.SIZE),null,null,null)?.use {if(it.moveToFirst()) it.getLong(0) else -1} ?: -1
                if(length<=0) {fd.close();null} else PeerPayload(ParcelFileDescriptor.AutoCloseInputStream(fd),length,"application/octet-stream")
            }}
            else ->null
        }
    }
    fun connect(text: String) {app.scope.launch {
        if(_state.value.connecting) return@launch;_state.update {it.copy(connecting=true)}
        var pending: PeerConnection?=null
        try {
            if(server==null) {setSharing(true);withTimeout(10000) {while(server==null) delay(40)}}
            withContext(Dispatchers.IO) {
            val invitation=PeerSecurity.parse(text);val client=PeerSecurity.client(invitation)
            val body=JSONObject().put("key",invitation.key).put("deviceId",deviceId).put("name",deviceName).put("duplex",true).toString().toRequestBody("application/json".toMediaType())
            val pair=client.newBuilder().readTimeout(135,java.util.concurrent.TimeUnit.SECONDS).callTimeout(140,java.util.concurrent.TimeUnit.SECONDS).build()
                .newCall(Request.Builder().url(invitation.origin.resolve("v1/pair")!!).post(body).build()).execute().use {require(it.isSuccessful);JSONObject(read(it,4096))}
            val id=pair.getString("deviceId");val token=pair.getString("token");require(pair.getInt("protocol")==1 && id.matches(Regex("[a-f0-9]{32}")) && id!=deviceId && token.matches(Regex("[a-f0-9]{64}")))
            require(pair.optBoolean("duplex")) {"Both devices must support bidirectional pairing"}
            val peer=PeerConnection(id,pair.getString("name").take(100),invitation,client,token);pending=peer;install(peer)
            val local=requireNotNull(server);val grant=local.grantAccess(id,peer.name)
            val reverseBody=JSONObject().put("port",local.port).put("fingerprint",local.fingerprint).put("token",grant).toString().toRequestBody("application/json".toMediaType())
            peer.http.newCall(Request.Builder().url(invitation.origin.resolve("v1/duplex")!!).header("Authorization","Bearer $token").post(reverseBody).build()).execute().use {require(it.isSuccessful)}
            pending=null
        }} catch(e: CancellationException) {pending?.let {disconnect(it)};throw e} catch(_: Exception) {pending?.let {disconnect(it)};app.messages.tryEmit(R.string.lan_connect_failed);app.log.write("WARN","LAN pairing failed")}
        finally {_state.update {it.copy(connecting=false)}}
    }}
    private suspend fun install(peer: PeerConnection) {
        connections.put(peer.id,peer)?.let {old ->old.alive=false;old.http.dispatcher.cancelAll()}
        try {refresh(peer)} catch(e: Exception) {connections.remove(peer.id,peer);peer.alive=false;throw e}
        app.scope.launch {watch(peer)}
    }
    private suspend fun acceptReverse(access: ReverseAccess): Boolean {
        if(access.deviceId==deviceId) return false
        val peer=PeerConnection(access.deviceId,access.name,access.invitation,PeerSecurity.client(access.invitation),access.token)
        return try {install(peer);true} catch(e: CancellationException) {throw e} catch(_: Exception) {false}
    }
    private suspend fun refresh(peer: PeerConnection)=withContext(Dispatchers.IO) {
        var first: JSONObject?=null;val songs=mutableListOf<Track>();var total: Int
        do {
            val page=JSONObject(request(peer,"v1/catalog?offset=${songs.size}",4*1024*1024));if(first==null) first=page
            require(page.getString("deviceId")==peer.id && page.getString("revision")==first!!.getString("revision"));total=page.getInt("total");require(total in 0..20000)
            val rows=page.getJSONArray("tracks");require(rows.length()<=200 && (rows.length()>0 || songs.size>=total))
            for(i in 0 until rows.length()) {val r=rows.getJSONObject(i);val remote=r.getString("id");require(remote.length in 1..256 && '/' !in remote && '\\' !in remote)
                val cover=if(r.optBoolean("artwork")) peer.invitation.origin.newBuilder().addPathSegments("v1/artwork").addPathSegment(remote).build().toString() else ""
                songs+=Track("lan_"+hash(peer.id+":"+remote),"nonet-lan://${peer.id}/"+Uri.encode(remote),r.getString("title").take(2000),r.optString("artist").take(2000),r.optString("album").take(2000),r.optLong("durationMs").coerceAtLeast(0),cover,providerId="lan:${peer.id}",inLibrary=false,sourceDeviceKind=page.optString("deviceKind","unknown"))
            }
        } while(songs.size<total)
        require(songs.distinctBy {it.id}.size==songs.size);val lists=first!!.getJSONArray("playlists");require(lists.length()<=2000);val byId=songs.associateBy {Uri.parse(it.uri).lastPathSegment}
        val playlists=List(lists.length()) {i ->val p=lists.getJSONObject(i);val ids=p.getJSONArray("trackIds");require(ids.length()<=20000);NearbyPlaylist(p.getString("id"),p.getString("name").take(100),p.optString("description").take(2000),List(ids.length()) {ids.getString(it)}.mapNotNull {byId[it]?.id})}
        if(!peer.alive || connections[peer.id]!==peer) return@withContext
        app.repository.setPeerKind(peer.id,first!!.optString("deviceKind","unknown"));app.repository.storeTracks(songs);peer.revision=first!!.getString("revision")
        _state.update {it.copy(peers=it.peers.filterNot {p ->p.id==peer.id}+NearbyDevice(peer.id,peer.name,playlists,songs.map {t ->t.id},first!!.optString("deviceKind","unknown")))}
    }
    private suspend fun watch(peer: PeerConnection) {
        val health=peer.health
        while(peer.alive) {
            withTimeoutOrNull(health.delayMs) {peer.retry.receive()}
            try {
                withContext(Dispatchers.IO) {
                    val info=JSONObject(request(peer,"v1/info",8192,4))
                    if(info.getString("deviceId")!=peer.id || info.getInt("protocol")!=1) throw PeerSessionRevoked()
                    // 目录刷新可能较慢；已验证的心跳不应被后续分页失败抹去。
                    health.contact(android.os.SystemClock.elapsedRealtime())
                    if(info.getString("revision")!=peer.revision) refresh(peer)
                }
                health.succeeded()
            } catch(error: CancellationException) {throw error}
            catch(_: PeerSessionRevoked) {disconnect(peer,"revoked");return}
            catch(_: Exception) {
                // 休眠和断网只改变可达性，不撤销会话；无论持续多久都等待网络恢复。
                health.failed(android.os.SystemClock.elapsedRealtime())
            }
            publishHealth(peer)
        }
    }
    /** 定时退出等待已连接设备收到断开通知，然后再停止共享服务。 */
    internal suspend fun disconnectAll() {connections.values.toList().map {peer ->app.scope.async {disconnect(peer)}}.awaitAll()}
    fun disconnect(id: String) {connections[id]?.let {p ->app.scope.launch {disconnect(p)}}}
    @androidx.annotation.OptIn(markerClass=[androidx.media3.common.util.UnstableApi::class])
    private suspend fun disconnect(peer: PeerConnection,reason: String="local") {
        if(!connections.remove(peer.id,peer)) return
        app.log.write("INFO","LAN session ended: $reason")
        peer.alive=false;server?.revoke(peer.id);_state.update {it.copy(peers=it.peers.filterNot {p ->p.id==peer.id})}
        withContext(Dispatchers.IO) {runCatching {
            val client=peer.http.newBuilder().callTimeout(1,java.util.concurrent.TimeUnit.SECONDS).build()
            client.newCall(Request.Builder().url(peer.invitation.origin.resolve("v1/disconnect")!!).header("Authorization","Bearer ${peer.token}").post(byteArrayOf().toRequestBody()).build()).execute().close()
        };peer.http.dispatcher.cancelAll();peer.http.connectionPool.evictAll()}
        app.repository.removePeerEphemeral(peer.id)
        val message=app.getString(R.string.lan_device_disconnected,peer.name);_notices.tryEmit(message)
        // 不依赖 Activity 焦点；另一端退出时后台也能看到一次连接结束通知。
        runCatching {
            val manager=app.getSystemService(android.app.NotificationManager::class.java)
            manager.createNotificationChannel(android.app.NotificationChannel("nonet-lan-events",app.getString(R.string.lan_title),android.app.NotificationManager.IMPORTANCE_DEFAULT))
            if(androidx.core.app.NotificationManagerCompat.from(app).areNotificationsEnabled()) {
                val open=android.app.PendingIntent.getActivity(app,208,Intent(app,MainActivity::class.java),android.app.PendingIntent.FLAG_UPDATE_CURRENT or android.app.PendingIntent.FLAG_IMMUTABLE)
                manager.notify("peer:"+peer.id,208,androidx.core.app.NotificationCompat.Builder(app,"nonet-lan-events").setSmallIcon(android.R.drawable.ic_dialog_info)
                    .setContentTitle(app.getString(R.string.lan_title)).setContentText(message).setStyle(androidx.core.app.NotificationCompat.BigTextStyle().bigText(message)).setContentIntent(open).setAutoCancel(true).setTimeoutAfter(10000).build())
            }
        }
    }
    suspend fun lyrics(t: Track): String=withContext(Dispatchers.IO) {val p=connections[t.providerId.removePrefix("lan:")] ?: return@withContext "";runCatching {request(p,"v1/lyrics/"+Uri.encode(Uri.parse(t.uri).lastPathSegment),2*1024*1024)}.getOrDefault("")}
    suspend fun saveArtwork(t: Track): Track=withContext(Dispatchers.IO) {
        if(!t.providerId.startsWith("lan:") || !t.cover.startsWith("https:")) return@withContext t
        val peer=connections[t.providerId.removePrefix("lan:")] ?: return@withContext t
        runCatching {val url=peer.invitation.origin.newBuilder().addPathSegments("v1/artwork").addPathSegment(requireNotNull(Uri.parse(t.uri).lastPathSegment)).build()
            val bytes=peer.http.newCall(Request.Builder().url(url).header("Authorization","Bearer ${peer.token}").build()).execute().use {require(it.isSuccessful);it.body!!.byteStream().use {stream ->stream.readLimited(5*1024*1024)}}
            val file=File(app.filesDir,"Artwork/${t.id}.jpg").apply {parentFile?.mkdirs();writeBytes(bytes)};t.copy(cover=file.absolutePath)
        }.getOrDefault(t)
    }
    private fun request(p: PeerConnection,path: String,limit: Int,seconds: Long=20): String {require(p.alive);val call=p.http.newCall(Request.Builder().url(p.invitation.origin.resolve(path)!!).header("Authorization","Bearer ${p.token}").build());call.timeout().timeout(seconds,java.util.concurrent.TimeUnit.SECONDS);return call.execute().use {if(it.code==401 || it.code==403) throw PeerSessionRevoked();require(it.isSuccessful);read(it,limit)}}
    private fun read(response: Response,limit: Int): String {require((response.body?.contentLength() ?: -1)<=limit);return response.body!!.byteStream().use {String(it.readLimited(limit),Charsets.UTF_8)}}
    private fun hash(s: String)=MessageDigest.getInstance("SHA-256").digest(s.toByteArray()).joinToString("") {"%02x".format(it)}
}
