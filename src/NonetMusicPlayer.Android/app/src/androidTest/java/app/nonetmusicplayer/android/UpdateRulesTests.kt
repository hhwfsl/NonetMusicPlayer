package app.nonetmusicplayer.android

import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.updates.*
import kotlinx.coroutines.runBlocking
import okhttp3.*
import okhttp3.ResponseBody.Companion.toResponseBody
import org.json.*
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.File
import java.security.MessageDigest
import androidx.test.core.app.ApplicationProvider

/** 使用模拟公开元数据与 HTTP 响应，不创建或上传测试 Release。 */
@RunWith(AndroidJUnit4::class)
class UpdateRulesTests {
    private fun release(version: String)=JSONObject().put("tag_name","android-v$version").put("body","Changes for $version").put("prerelease",true)
        .put("assets",JSONArray().put(JSONObject().put("name","Nonet-$version.apk").put("state","uploaded").put("size",1024).put("digest","sha256:"+"a".repeat(64)).put("browser_download_url","https://github.com/hhwfsl/NonetMusicPlayer/releases/download/android-v$version/Nonet-$version.apk")))
    @Test fun selectAndroidVersionAndRequireVerifiableAsset() {
        val releases=JSONArray().put(release("0.3.0-beta.9")).put(release("0.3.0-beta.10")).put(release("0.3.0-beta.20").put("draft",true)).put(release("0.3.0-beta.40").put("tag_name","v0.3.0-beta.40"))
        assertEquals("0.3.0-beta.10",GithubUpdateSource.select(releases,"0.3.0-beta.8")?.version)
        assertNull(GithubUpdateSource.select(releases,"0.3.0-beta.10"))
        assertNull(GithubUpdateSource.select(releases,"0.3.0"))
        val invalid=release("1.0.0");invalid.getJSONArray("assets").getJSONObject(0).put("digest","")
        assertNull(GithubUpdateSource.select(JSONArray().put(invalid),"0.3.0-beta.9"))
        listOf("http://github.com/hhwfsl/NonetMusicPlayer/releases/download/x/a.apk","https://evil.invalid/a.apk","https://github.com/other/repo/releases/download/x/a.apk","https://u:p@github.com/hhwfsl/NonetMusicPlayer/releases/download/x/a.apk").forEach {assertFalse(GithubUpdateSource.validAssetUrl(it))}
    }
    @Test fun rejectBadDigestWrongLengthAndUntrustedRedirect()=runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val file=File(app.cacheDir,"update-transfer-fixture.part")
        val data=ByteArray(1024){it.toByte()};val hash=MessageDigest.getInstance("SHA-256").digest(data).joinToString(""){"%02x".format(it)}
        val release=AndroidRelease("9.0.0","","https://github.com/hhwfsl/NonetMusicPlayer/releases/download/android-v9.0.0/Nonet-9.0.0.apk",1024,hash)
        val http=OkHttpClient.Builder().addInterceptor {chain ->Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(200).message("OK").body(data.toResponseBody()).build()}.build()
        try {
            var received=0L;HttpsApkTransfer(http).fetch(release,file){received=it};assertEquals(1024L,received);assertArrayEquals(data,file.readBytes())
            try {HttpsApkTransfer(http).fetch(release.copy(sha256="0".repeat(64)),file){};fail("Invalid digest accepted")}catch(_: UpdateFailure){}
            try {HttpsApkTransfer(http).fetch(release.copy(size=1025),file){};fail("Invalid length accepted")}catch(_: UpdateFailure){}
            val redirect=OkHttpClient.Builder().followRedirects(false).addInterceptor {chain ->Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).code(302).message("Redirect").header("Location","http://evil.invalid/update.apk").body(ByteArray(0).toResponseBody()).build()}.build()
            try {HttpsApkTransfer(redirect).fetch(release,file){};fail("Untrusted redirect accepted")}catch(_: IllegalArgumentException){}
            try {InstalledApkValidation(app).validate(file,release);fail("Invalid APK accepted")}catch(_: UpdateFailure){}
        } finally {file.delete()}
    }
}
