package app.nonetmusicplayer.android

import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.lifecycle.ViewModelProvider
import androidx.media3.common.util.UnstableApi
import androidx.test.ext.junit.runners.AndroidJUnit4
import app.nonetmusicplayer.android.network.*
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.io.ByteArrayInputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

/** 使用环回固定证书、合成音频验证取消与长时间停网，不依赖任何真实媒体或厂商接口。 */
@RunWith(AndroidJUnit4::class)
@UnstableApi
class RemotePlaybackTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    @Test fun rapidRemoteSwitchDoesNotWaitForThePreviousHeaders() = runBlocking {
        val app=compose.activity.application as MusicApplication
        val vm=ViewModelProvider(compose.activity)[PlayerViewModel::class.java]
        compose.waitUntil(15000) {vm.playback.value.ready}
        val id=UUID.randomUUID().toString().replace("-","");val (ssl,pin)=PeerSecurity.identity()
        val bytes=wave();val slowRequested=AtomicBoolean(false)
        val fixture=PeerServer(id,"Switch fixture",ssl,pin,{catalog(id,listOf("slow","fast"))},{kind,track ->
            if(kind!="audio") null else {
                if(track=="slow") {slowRequested.set(true);delay(60000)}
                PeerPayload(ByteArrayInputStream(bytes),bytes.size.toLong(),"audio/wav")
            }
        },approve={true},reverse={true})
        try {
            app.lan.connect(fixture.invitation("127.0.0.1"))
            compose.waitUntil(15000) {app.lan.state.value.peers.any {it.id==id} && !app.lan.state.value.connecting}
            val tracks=app.repository.state.value.tracks.filter {it.providerId=="lan:$id"}
            compose.runOnUiThread {vm.play(tracks.first {it.title=="slow"},tracks)}
            compose.waitUntil(10000) {slowRequested.get()}
            val before=android.os.SystemClock.elapsedRealtime()
            compose.runOnUiThread {vm.play(tracks.first {it.title=="fast"},tracks)}
            compose.waitUntil(4000) {vm.playback.value.track?.title=="fast" && vm.playback.value.playing && vm.playback.value.positionMs>100}
            assertTrue(android.os.SystemClock.elapsedRealtime()-before<3500)
            assertTrue(app.lan.state.value.peers.any {it.id==id})
        } finally {compose.runOnUiThread {if(vm.playback.value.playing) vm.togglePlay()};app.lan.disconnect(id);app.lan.setSharing(false);fixture.close()}
    }
    @Test fun aLongUnavailablePeerKeepsItsPairingAndRecovers() = runBlocking {
        val app=compose.activity.application as MusicApplication
        val id=UUID.randomUUID().toString().replace("-","");val (ssl,pin)=PeerSecurity.identity();val blocked=AtomicBoolean(false)
        val fixture=PeerServer(id,"Recovery fixture",ssl,pin,{
            if(blocked.get()) Thread.sleep(5500)
            catalog(id,emptyList())
        },{_,_->null},approve={true},reverse={true})
        try {
            app.lan.connect(fixture.invitation("127.0.0.1"))
            compose.waitUntil(15000) {app.lan.state.value.peers.any {it.id==id} && !app.lan.state.value.connecting}
            blocked.set(true);compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
            delay(45000)
            assertTrue(app.lan.state.value.peers.any {it.id==id});assertFalse(app.lan.state.value.peers.single {it.id==id}.reachable)
            blocked.set(false)
            withTimeout(25000) {while(app.lan.state.value.peers.none {it.id==id && it.reachable}) delay(100)}
            assertTrue(app.lan.state.value.sharing)
        } finally {
            blocked.set(false)
            compose.activity.startActivity(android.content.Intent(compose.activity,MainActivity::class.java).setAction(android.content.Intent.ACTION_MAIN).addCategory(android.content.Intent.CATEGORY_LAUNCHER).addFlags(android.content.Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
            app.lan.disconnect(id);app.lan.setSharing(false);fixture.close()
        }
    }
    private fun catalog(id: String,ids: List<String>)=JSONObject().put("deviceId",id).put("name","Remote fixture").put("deviceKind","desktop").put("revision","one")
        .put("total",ids.size).put("playlists",JSONArray()).put("tracks",JSONArray().apply {ids.forEach {put(JSONObject().put("id",it).put("title",it).put("artist","Fixture").put("album","").put("durationMs",8000))}})
    private fun wave(): ByteArray {
        val size=48000*2*8;return ByteBuffer.allocate(size+44).order(ByteOrder.LITTLE_ENDIAN).apply {
            put("RIFF".toByteArray()).putInt(size+36).put("WAVEfmt ".toByteArray()).putInt(16).putShort(1).putShort(1)
            putInt(48000).putInt(96000).putShort(2).putShort(16).put("data".toByteArray()).putInt(size).put(ByteArray(size))
        }.array()
    }
}
