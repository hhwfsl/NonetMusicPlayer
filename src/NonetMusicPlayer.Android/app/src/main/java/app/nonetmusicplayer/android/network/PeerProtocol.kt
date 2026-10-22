package app.nonetmusicplayer.android.network

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import kotlinx.coroutines.*
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import org.json.JSONObject
import java.io.*
import java.math.BigInteger
import java.net.InetAddress
import java.security.*
import java.security.cert.X509Certificate
import java.util.*
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Semaphore
import java.util.concurrent.TimeUnit
import javax.net.ssl.*
import javax.security.auth.x500.X500Principal

data class PeerInvitation(val origin: HttpUrl,val fingerprint: String,val key: String)
data class PeerPayload(val input: InputStream,val length: Long,val type: String): Closeable {override fun close()=input.close()}
data class IncomingConnection(val requestId: String,val deviceId: String,val name: String,val address: String,val waitSeconds: Int = 30,val duplex: Boolean = false)
data class ReverseAccess(val deviceId: String,val name: String,val invitation: PeerInvitation,val token: String)

/** 固定邀请证书，仅接收数字私网地址，不使用全局信任所有证书或 DNS。 */
object PeerSecurity {
    fun privateAddress(value: String): Boolean {
        if(!value.matches(Regex("[0-9.]+"))) return false
        val b=runCatching {InetAddress.getByName(value).address.map {it.toInt() and 255}}.getOrNull() ?: return false
        return b.size==4 && (b[0]==127 || b[0]==10 || b[0]==192 && b[1]==168 || b[0]==172 && b[1] in 16..31)
    }
    fun parse(text: String): PeerInvitation {
        val uri=java.net.URI(text.trim());require(uri.scheme=="nonet" && uri.host=="connect")
        val q=uri.rawQuery.split('&').map {it.split('=',limit=2)}.filter {it.size==2}.associate {it[0] to java.net.URLDecoder.decode(it[1],"UTF-8")}
        val host=q["host"].orEmpty();val port=q["port"]?.toIntOrNull() ?: 0;val cert=q["cert"].orEmpty().lowercase();val key=q["key"].orEmpty()
        require(privateAddress(host) && port in 1024..65535 && cert.matches(Regex("[a-f0-9]{64}")) && key.matches(Regex("[a-fA-F0-9]{64}")))
        return PeerInvitation("https://$host:$port/".toHttpUrl(),cert,key)
    }
    fun randomKey()=ByteArray(32).also {SecureRandom().nextBytes(it)}.joinToString("") {"%02x".format(it)}
    fun fingerprint(cert: X509Certificate)=MessageDigest.getInstance("SHA-256").digest(cert.encoded).joinToString("") {"%02x".format(it)}
    fun identity(): Pair<SSLContext,String> {
        val store=KeyStore.getInstance("AndroidKeyStore").apply {load(null)};val alias="nonet.lan.ec-identity.v1"
        // 使用硬件库支持的 ECDSA 签名，避免 TLS RSA 原始签名与 Keystore 填充模式不兼容。
        if(!store.containsAlias(alias)) KeyPairGenerator.getInstance("EC","AndroidKeyStore").apply {
            initialize(KeyGenParameterSpec.Builder(alias,KeyProperties.PURPOSE_SIGN).setAlgorithmParameterSpec(java.security.spec.ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_NONE,KeyProperties.DIGEST_SHA256,KeyProperties.DIGEST_SHA384,KeyProperties.DIGEST_SHA512)
                .setCertificateSubject(X500Principal("CN=Nonet local library"))
                .setCertificateSerialNumber(BigInteger.ONE).setCertificateNotBefore(Date(System.currentTimeMillis()-86400000))
                .setCertificateNotAfter(Date(System.currentTimeMillis()+5L*365*86400000)).build());generateKeyPair()
        }
        val keys=KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply {init(store,null)}
        return SSLContext.getInstance("TLS").apply {init(keys.keyManagers,null,null)} to fingerprint(store.getCertificate(alias) as X509Certificate)
    }
    fun client(invitation: PeerInvitation): OkHttpClient {
        val trust=object : X509TrustManager {
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            override fun checkClientTrusted(chain: Array<X509Certificate>,auth: String) {throw java.security.cert.CertificateException()}
            override fun checkServerTrusted(chain: Array<X509Certificate>,auth: String) {if(chain.isEmpty() || fingerprint(chain[0])!=invitation.fingerprint) throw java.security.cert.CertificateException("Peer identity changed")}
        }
        val ssl=SSLContext.getInstance("TLS").apply {init(null,arrayOf(trust),null)}
        return OkHttpClient.Builder().sslSocketFactory(ssl.socketFactory,trust).hostnameVerifier {host,session ->host==invitation.origin.host && fingerprint(session.peerCertificates[0] as X509Certificate)==invitation.fingerprint}
            .proxy(java.net.Proxy.NO_PROXY).followRedirects(false).followSslRedirects(false).connectTimeout(4,TimeUnit.SECONDS).readTimeout(20,TimeUnit.SECONDS).build()
    }
    fun deviceId(context: Context): String {
        val p=context.getSharedPreferences("lan-identity",Context.MODE_PRIVATE)
        return p.getString("id",null) ?: UUID.randomUUID().toString().replace("-","").also {p.edit().putString("id",it).apply()}
    }
}

fun peerRange(value: String,length: Long): LongRange? {
    if(!value.startsWith("bytes=") || ',' in value || length<=0) return null
    val p=value.substring(6).split('-',limit=2);if(p.size!=2) return null
    val start: Long;val end: Long
    if(p[0].isEmpty()) {val suffix=p[1].toLongOrNull() ?: return null;if(suffix<=0) return null;start=(length-suffix).coerceAtLeast(0);end=length-1}
    else {start=p[0].toLongOrNull() ?: return null;end=if(p[1].isEmpty()) length-1 else (p[1].toLongOrNull() ?: return null).coerceAtMost(length-1)}
    return if(start in 0 until length && end>=start) start..end else null
}

/** 与桌面共享 v1 只读协议。认证在内存内，音频仅用 64 KiB 流式缓冲。 */
class PeerServer(private val id: String,private val name: String,ssl: SSLContext,val fingerprint: String,
    private val catalog: ()->JSONObject,private val open: suspend (String,String)->PeerPayload?,private val onError: (Exception)->Unit={},
    private val approve: suspend (IncomingConnection)->Boolean = {false},
    private val waitSeconds: ()->Int = {30},private val reverse: (suspend (ReverseAccess)->Boolean)? = null,
    private val disconnected: (String)->Unit = {},private val activity: (String)->Unit = {}): Closeable {
    private val scope=CoroutineScope(SupervisorJob()+Dispatchers.IO)
    private val socket=ssl.serverSocketFactory.createServerSocket(0,12,InetAddress.getByName("0.0.0.0")) as SSLServerSocket
    private val slots=Semaphore(12);private val clients=ConcurrentHashMap.newKeySet<SSLSocket>()
    private val sessions=ConcurrentHashMap<String,Long>();private val attempts=ConcurrentHashMap<String,Pair<Long,Int>>()
    private data class Session(val id: String,val name: String,val address: String,val duplex: Boolean)
    private val identities=ConcurrentHashMap<String,Session>()
    private val pairing=Semaphore(1)
    @Volatile private var key=PeerSecurity.randomKey();@Volatile private var expires=System.currentTimeMillis()+300000
    val port get()=socket.localPort
    init {scope.launch {try {while(isActive) {val client=socket.accept() as SSLSocket
        if(!PeerSecurity.privateAddress(client.inetAddress.hostAddress.orEmpty()) || !slots.tryAcquire()) {client.close();continue}
        clients.add(client);launch {try {client.use {serve(it)}} catch(e: Exception) {if(e !is java.net.SocketException && e !is CancellationException) onError(e)} finally {clients.remove(client);slots.release()}}
    }} catch(_: IOException) {}}}
    fun renew() {key=PeerSecurity.randomKey();expires=System.currentTimeMillis()+300000}
    fun invitation(host: String)="nonet://connect?host=$host&port=$port&cert=$fingerprint&key=$key"
    fun grantAccess(device: String,name: String): String {
        require(device.matches(Regex("[a-fA-F0-9]{32}")) && device!=id && scope.isActive && sessions.size<16)
        return PeerSecurity.randomKey().also {sessions[it]=System.currentTimeMillis();identities[it]=Session(device,name,"",false)}
    }
    fun revoke(device: String) {identities.entries.filter {it.value.id==device}.forEach {identities.remove(it.key);sessions.remove(it.key)}}
    private suspend fun serve(client: SSLSocket) {
        client.soTimeout=12000;client.startHandshake();val input=client.inputStream.buffered();val output=client.outputStream.buffered()
        val raw=ByteArrayOutputStream();var tail=""
        while(raw.size()<16384) {val b=input.read();if(b<0) return;raw.write(b);tail=(tail+b.toChar()).takeLast(4);if(tail=="\r\n\r\n") break};require(tail=="\r\n\r\n")
        val lines=raw.toString("US-ASCII").split("\r\n");val first=lines.first().split(' ');require(first.size==3)
        val method=first[0];val path=first[1].substringBefore('?');val head=method=="HEAD";val headers=mutableMapOf<String,String>()
        lines.drop(1).filter {':' in it}.forEach {val h=it.substringBefore(':').lowercase();require(!headers.containsKey(h));headers[h]=it.substringAfter(':').trim()}
        fun respond(code: Int,body: ByteArray=byteArrayOf()) {header(output,code,body.size.toLong(),"application/json");if(!head) output.write(body);output.flush()}
        if(path=="/v1/pair" && method=="POST") {
            val remote=client.inetAddress.hostAddress.orEmpty();val now=System.currentTimeMillis();val previous=attempts[remote]
            attempts.entries.removeIf {it.value.first<now-60000}
            if(attempts.size>=256 && !attempts.containsKey(remote)) {respond(429);return}
            val tries=if(previous==null || previous.first<now-60000) now to 1 else previous.first to previous.second+1;attempts[remote]=tries
            val length=headers["content-length"]?.toIntOrNull() ?: 0
            if(tries.second>6 || length !in 1..2048 || headers.containsKey("transfer-encoding")) {respond(429);return}
            val bytes=ByteArray(length);DataInputStream(input).readFully(bytes);val request=JSONObject(String(bytes,Charsets.UTF_8))
            val remoteId=request.optString("deviceId");val remoteName=request.optString("name")
            if(now>expires || sessions.size>=16 || !remoteId.matches(Regex("[a-fA-F0-9]{32}")) || remoteId==id || remoteName.isBlank() || remoteName.length>100 || remoteName.any {it.isISOControl()} ||
                !MessageDigest.isEqual(request.optString("key").toByteArray(),key.toByteArray())) {respond(403);return}
            if(!pairing.tryAcquire()) {respond(403);return}
            val wait=waitSeconds().coerceIn(10,120);val duplex=request.optBoolean("duplex") && reverse!=null
            val accepted=try {withTimeoutOrNull(wait*1000L) {approve(IncomingConnection(UUID.randomUUID().toString(),remoteId,remoteName,remote,wait,duplex))} == true} finally {pairing.release()}
            // 接受之前不创建会话；超时、停止共享、邀请轮换或界面关闭均不授予访问。
            if(!accepted || !scope.isActive || !MessageDigest.isEqual(request.optString("key").toByteArray(),key.toByteArray())) {respond(403);return}
            val token=PeerSecurity.randomKey();sessions[token]=System.currentTimeMillis();identities[token]=Session(remoteId,remoteName,remote,duplex)
            respond(200,JSONObject().put("deviceId",id).put("name",name).put("token",token).put("protocol",1).put("duplex",duplex).toString().toByteArray());return
        }
        val token=headers["authorization"].orEmpty().removePrefix("Bearer ");val seen=sessions[token]
        val identity=identities[token]
        // 会话只在显式撤销或服务退出时清除；后台停网不会让已批准的凭据按闲置时间失效。
        if(seen==null || identity==null || identity.address.isNotEmpty() && identity.address!=client.inetAddress.hostAddress) {respond(401);return};sessions[token]=System.currentTimeMillis()
        if(path=="/v1/disconnect" && method=="POST") {revoke(identity.id);disconnected(identity.id);respond(200);return}
        if(path=="/v1/duplex" && method=="POST") {
            val length=headers["content-length"]?.toIntOrNull() ?: 0
            if(!identity.duplex || reverse==null || length !in 1..2048 || headers.containsKey("transfer-encoding")) {respond(403);return}
            val bytes=ByteArray(length);DataInputStream(input).readFully(bytes);val grant=JSONObject(String(bytes,Charsets.UTF_8))
            val port=grant.optInt("port");val cert=grant.optString("fingerprint");val bearer=grant.optString("token")
            if(port !in 1024..65535 || !cert.matches(Regex("[a-fA-F0-9]{64}")) || !bearer.matches(Regex("[a-fA-F0-9]{64}")) || !identities.replace(token,identity,identity.copy(duplex=false))) {respond(400);return}
            // 不接收回连主机参数，只回连真正配对来源的固定证书端口，避免任意 URL 代理。
            val origin="https://${client.inetAddress.hostAddress}:$port/".toHttpUrl()
            val ok=withTimeoutOrNull(15000) {reverse.invoke(ReverseAccess(identity.id,identity.name,PeerInvitation(origin,cert.lowercase(),""),bearer))} == true
            if(!ok) revoke(identity.id);respond(if(ok) 200 else 409);return
        }
        if(method !in setOf("GET","HEAD")) {respond(405);return}
        // 只在认证和来源验证成功后记录活跃，不以未授权探测延长会话。
        if(path=="/v1/info" || path=="/v1/catalog") activity(identity.id)
        if(path=="/v1/info") {respond(200,JSONObject().put("deviceId",id).put("name",name).put("revision",catalog().getString("revision")).put("protocol",1).toString().toByteArray());return}
        if(path=="/v1/catalog") {
            val c=JSONObject(catalog().toString());val tracks=c.getJSONArray("tracks")
            val offset=first[1].substringAfter('?',"").split('&').firstOrNull {it.startsWith("offset=")}?.substringAfter('=')?.toIntOrNull()?.coerceIn(0,20000) ?: 0
            c.put("tracks",org.json.JSONArray().apply {for(i in offset until minOf(offset+200,tracks.length())) put(tracks.getJSONObject(i))});if(offset>0) c.put("playlists",org.json.JSONArray());respond(200,c.toString().toByteArray());return
        }
        val p=path.split('/').filter {it.isNotEmpty()}
        if(p.size!=3 || p[0]!="v1" || p[1] !in setOf("audio","artwork","lyrics")) {respond(404);return}
        val track=java.net.URLDecoder.decode(p[2],"UTF-8");if(track.length !in 1..256 || '/' in track || '\\' in track) {respond(400);return}
        val payload=open(p[1],track) ?: run {respond(404);return}
        payload.use {file ->
            val range=headers["range"]?.let {peerRange(it,file.length)}
            if(headers.containsKey("range") && range==null) {header(output,416,0,"text/plain","Content-Range: bytes */${file.length}\r\n");output.flush();return}
            val length=range?.let {it.last-it.first+1} ?: file.length
            header(output,if(range==null) 200 else 206,length,file.type,"Accept-Ranges: bytes\r\n"+(range?.let {"Content-Range: bytes ${it.first}-${it.last}/${file.length}\r\n"} ?: ""));output.flush();if(head) return
            var skip=range?.first ?: 0;while(skip>0) {val n=file.input.skip(skip);if(n>0) skip-=n else {if(file.input.read()<0) return;skip--}}
            val buffer=ByteArray(65536);var left=length
            while(left>0 && scope.isActive) {val n=file.input.read(buffer,0,minOf(buffer.size.toLong(),left).toInt());if(n<0) break;output.write(buffer,0,n);left-=n};output.flush()
        }
    }
    private fun header(out: OutputStream,status: Int,length: Long,type: String,extra: String="") {out.write("HTTP/1.1 $status Response\r\nContent-Length: $length\r\nContent-Type: $type\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n$extra\r\n".toByteArray(Charsets.US_ASCII))}
    override fun close() {scope.cancel();socket.close();clients.forEach {runCatching {it.close()}};clients.clear();sessions.clear();identities.clear()}
}
