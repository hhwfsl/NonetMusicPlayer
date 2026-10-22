package app.nonetmusicplayer.android.updates

import app.nonetmusicplayer.android.R
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.HttpUrl.Companion.toHttpUrl
import org.json.*
import java.io.*
import java.security.MessageDigest
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

internal const val NONET_REPOSITORY="https://github.com/hhwfsl/NonetMusicPlayer"
internal const val MAX_APK_BYTES=256L*1024*1024
internal data class AndroidRelease(val version: String,val notes: String,val url: String,val size: Long,val sha256: String) {
    fun json()=JSONObject().put("version",version).put("notes",notes).put("url",url).put("size",size).put("sha256",sha256)
}
internal class UpdateFailure(val text: Int): IOException()
internal fun interface UpdateSource {suspend fun latest(current: String): AndroidRelease?}

/** 固定公开仓库，仅处理 Android 专属标签与完整 APK；不携带账号或任何音乐数据。 */
internal class GithubUpdateSource: UpdateSource {
    private val http=OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).connectTimeout(15,TimeUnit.SECONDS).readTimeout(20,TimeUnit.SECONDS).callTimeout(25,TimeUnit.SECONDS).build()
    override suspend fun latest(current: String): AndroidRelease? = withContext(Dispatchers.IO) {
        val request=Request.Builder().url("https://api.github.com/repos/hhwfsl/NonetMusicPlayer/releases?per_page=100")
            .header("Accept","application/vnd.github+json").header("User-Agent","Nonet-Android/$current").build()
        http.newCall(request).execute().use {response ->
            if(response.code==403 || response.code==429) throw UpdateFailure(R.string.update_rate_limit)
            if(!response.isSuccessful) throw IOException("Release metadata unavailable")
            val input=requireNotNull(response.body).byteStream();val data=ByteArrayOutputStream();val buffer=ByteArray(16384)
            while(true) {ensureActive();val n=input.read(buffer);if(n<0) break;if(data.size()+n>8*1024*1024) throw IOException("Metadata too large");data.write(buffer,0,n)}
            select(JSONArray(data.toString("UTF-8")),current)
        }
    }
    companion object {
        fun select(releases: JSONArray,current: String): AndroidRelease? {
            val installed=UpdateVersion.parse(current) ?: throw IOException("Invalid installed version")
            return (0 until releases.length()).mapNotNull {i ->
                val r=releases.optJSONObject(i) ?: return@mapNotNull null
                if(r.optBoolean("draft") || r.optBoolean("prerelease") && installed.preview.isEmpty()) return@mapNotNull null
                val tag=r.optString("tag_name");if(!tag.startsWith("android-v")) return@mapNotNull null
                val version=tag.removePrefix("android-v");val parsed=UpdateVersion.parse(version) ?: return@mapNotNull null
                if(parsed<=installed) return@mapNotNull null
                val assets=r.optJSONArray("assets") ?: return@mapNotNull null
                val asset=(0 until assets.length()).mapNotNull {assets.optJSONObject(it)}.singleOrNull {it.optString("name")=="Nonet-$version.apk" && it.optString("state")=="uploaded"} ?: return@mapNotNull null
                val url=asset.optString("browser_download_url");val digest=asset.optString("digest").removePrefix("sha256:")
                if(!validAssetUrl(url) || !digest.matches(Regex("[a-fA-F0-9]{64}")) || asset.optLong("size") !in 1024..MAX_APK_BYTES) return@mapNotNull null
                AndroidRelease(version,r.optString("body").take(50000),url,asset.getLong("size"),digest.lowercase())
            }.maxByOrNull {requireNotNull(UpdateVersion.parse(it.version))}
        }
        fun validAssetUrl(value: String): Boolean = runCatching {
            val u=value.toHttpUrl();u.scheme=="https" && u.host=="github.com" && u.port==443 && u.username.isEmpty() && u.password.isEmpty() && u.encodedPath.startsWith("/hhwfsl/NonetMusicPlayer/releases/download/")
        }.getOrDefault(false)
    }
}

internal interface ApkTransfer {
    suspend fun fetch(release: AndroidRelease,file: File,progress: (Long)->Unit)
    fun cancel()
}
/** HTTPS、逐跳验证、限长、增量校验；取消直接中断阻塞读，不等待网络超时。 */
internal class HttpsApkTransfer(private val http: OkHttpClient=OkHttpClient.Builder().followRedirects(false).followSslRedirects(false).connectTimeout(20,TimeUnit.SECONDS).readTimeout(30,TimeUnit.SECONDS).build()): ApkTransfer {
    private val call=AtomicReference<Call?>()
    override fun cancel() {call.getAndSet(null)?.cancel()}
    override suspend fun fetch(release: AndroidRelease,file: File,progress: (Long)->Unit) = withContext(Dispatchers.IO) {
        require(GithubUpdateSource.validAssetUrl(release.url) && release.size in 1024..MAX_APK_BYTES)
        var url=release.url.toHttpUrl();var redirects=0
        while(true) {
            ensureActive()
            val pending=http.newCall(Request.Builder().url(url).header("User-Agent","Nonet-Android").build());call.set(pending)
            pending.execute().use {response ->
                if(response.code in setOf(301,302,303,307,308)) {
                    if(++redirects>5) throw IOException("Too many redirects")
                    url=url.resolve(response.header("Location").orEmpty()) ?: throw IOException("Invalid redirect")
                    require(url.scheme=="https" && url.port==443 && url.username.isEmpty() && url.password.isEmpty() && url.host in setOf("github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","github-releases.githubusercontent.com"))
                } else {
                    if(response.code!=200) throw IOException("APK download unavailable")
                    val body=requireNotNull(response.body)
                    if(body.contentLength()>0 && body.contentLength()!=release.size) throw UpdateFailure(R.string.update_invalid_package)
                    val digest=MessageDigest.getInstance("SHA-256");var bytes=0L;var last=0L;val buffer=ByteArray(65536)
                    body.byteStream().use {input ->file.outputStream().use {output ->
                        while(true) {ensureActive();val count=input.read(buffer);if(count<0) break;bytes+=count
                            if(bytes>release.size) throw UpdateFailure(R.string.update_invalid_package)
                            output.write(buffer,0,count);digest.update(buffer,0,count)
                            val now=android.os.SystemClock.elapsedRealtime();if(now-last>=200) {last=now;progress(bytes)}
                        }
                    }}
                    val expected=release.sha256.chunked(2).map {it.toInt(16).toByte()}.toByteArray()
                    if(bytes!=release.size || !MessageDigest.isEqual(digest.digest(),expected)) throw UpdateFailure(R.string.update_invalid_package)
                    progress(bytes);call.compareAndSet(pending,null);return@withContext
                }
            }
        }
    }
}
