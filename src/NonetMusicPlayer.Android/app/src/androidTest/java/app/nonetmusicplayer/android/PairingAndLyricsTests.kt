package app.nonetmusicplayer.android

import android.app.NotificationManager
import android.content.Intent
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.network.*
import app.nonetmusicplayer.android.ui.*
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.util.UUID

/** 通知动作在 Activity 后台完成审批；逐字着色在播放采样间隔内持续推进。 */
@RunWith(AndroidJUnit4::class)
@androidx.media3.common.util.UnstableApi
class PairingAndLyricsTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    @Test fun backgroundNotificationAcceptsAndRejectsWithoutActivityFocus() = runBlocking {
        val app=compose.activity.application as MusicApplication
        InstrumentationRegistry.getInstrumentation().uiAutomation.grantRuntimePermission(app.packageName,android.Manifest.permission.POST_NOTIFICATIONS)
        compose.runOnUiThread {app.lan.setSharing(true)}
        withTimeout(15000) {while(app.lan.state.value.invitations.isEmpty()) delay(30)}
        val published=app.lan.state.value.invitations.first();val source=PeerSecurity.parse(published)
        val invite=source.copy(origin=source.origin.newBuilder().host("127.0.0.1").build())
        val http=PeerSecurity.client(invite)
        compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
        try {
            for(accept in listOf(false,true)) {
                val body=JSONObject().put("key",invite.key).put("deviceId",UUID.randomUUID().toString().replace("-","")).put("name","Background test").put("duplex",true).toString().toRequestBody("application/json".toMediaType())
                val pending=async(Dispatchers.IO) {http.newCall(Request.Builder().url(invite.origin.resolve("v1/pair")!!).post(body).build()).execute().use {it.code}}
                withTimeout(5000) {while(app.lan.state.value.incoming==null) delay(20)}
                assertEquals(30,app.lan.state.value.incoming!!.waitSeconds);assertFalse(pending.isCompleted)
                val manager=app.getSystemService(NotificationManager::class.java)
                val notification=withTimeout(5000) {var found=manager.activeNotifications.firstOrNull {it.id==205};while(found==null) {delay(20);found=manager.activeNotifications.firstOrNull {it.id==205}};found.notification}
                notification.actions[if(accept) 1 else 0].actionIntent.send()
                assertEquals(if(accept) 200 else 403,withTimeout(5000) {pending.await()})
                withTimeout(5000) {while(app.lan.state.value.incoming!=null) delay(20)}
            }
        } finally {
            compose.activity.startActivity(Intent(compose.activity,MainActivity::class.java).setAction(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
            compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
            app.lan.setSharing(false)
        }
    }
    @Test fun twoMobileServersEstablishReverseAccessAndRevokeOnDisconnect() = runBlocking {
        val (ssl,pin)=PeerSecurity.identity();val first=UUID.randomUUID().toString().replace("-","");val second=UUID.randomUUID().toString().replace("-","")
        var readReverse=false;val ended=CompletableDeferred<String>()
        fun catalog(id: String)=JSONObject().put("deviceId",id).put("name","Phone").put("deviceKind","mobile").put("revision","one").put("total",0).put("tracks",org.json.JSONArray()).put("playlists",org.json.JSONArray())
        val target=PeerServer(second,"Second phone",ssl,pin,{catalog(second)},{_,_->null})
        val source=PeerServer(first,"First phone",ssl,pin,{catalog(first)},{_,_->null},approve={true},reverse={access ->
            val client=PeerSecurity.client(access.invitation)
            withContext(Dispatchers.IO) {client.newCall(Request.Builder().url(access.invitation.origin.resolve("v1/catalog")!!).header("Authorization","Bearer ${access.token}").build()).execute().use {
                readReverse=it.isSuccessful && JSONObject(it.body!!.string()).getString("deviceId")==second;readReverse
            }}
        },disconnected={ended.complete(it)})
        try {withContext(Dispatchers.IO) {
            val invite=PeerSecurity.parse(source.invitation("127.0.0.1"));val client=PeerSecurity.client(invite)
            val body=JSONObject().put("key",invite.key).put("deviceId",second).put("name","Second phone").put("duplex",true).toString().toRequestBody("application/json".toMediaType())
            val pair=client.newCall(Request.Builder().url(invite.origin.resolve("v1/pair")!!).post(body).build()).execute().use {assertEquals(200,it.code);JSONObject(it.body!!.string())}
            assertTrue(pair.getBoolean("duplex"));assertFalse(readReverse)
            val token=pair.getString("token")
            val grant=JSONObject().put("port",target.port).put("fingerprint",pin).put("token",target.grantAccess(first,"First phone")).toString().toRequestBody("application/json".toMediaType())
            client.newCall(Request.Builder().url(invite.origin.resolve("v1/duplex")!!).header("Authorization","Bearer $token").post(grant).build()).execute().use {assertEquals(200,it.code)}
            assertTrue(readReverse)
            client.newCall(Request.Builder().url(invite.origin.resolve("v1/disconnect")!!).header("Authorization","Bearer $token").post(byteArrayOf().toRequestBody()).build()).execute().use {assertEquals(200,it.code)}
            assertEquals(second,withTimeout(3000) {ended.await()})
            client.newCall(Request.Builder().url(invite.origin.resolve("v1/catalog")!!).header("Authorization","Bearer $token").build()).execute().use {assertEquals(401,it.code)}
        }} finally {source.close();target.close()}
    }
    @Test fun wordHighlightAdvancesBetweenPlayerSamplesAndFreezesOnPause() {
        var playing by mutableStateOf(true)
        val lines=listOf(LyricLine(0,"MMMMMMMMMMMM",words=listOf(LyricWord(0,2000,0,12)),endMs=2000))
        compose.mainClock.autoAdvance=false
        compose.activity.setContent {MusicTheme(Preferences(language="en",theme="light")) {LyricView(lines,200,{},Modifier.fillMaxSize(),playing=playing)}}
        compose.mainClock.advanceTimeBy(32);compose.waitForIdle()
        fun pixels()=compose.onNodeWithTag("lyric_line_0").captureToImage().asAndroidBitmap().let {b ->IntArray(b.width*b.height).also {b.getPixels(it,0,b.width,0,0,b.width,b.height)}}
        val first=pixels();compose.mainClock.advanceTimeBy(160);compose.waitForIdle();val next=pixels()
        assertFalse("Highlight must advance without a new 250 ms player sample",first.contentEquals(next))
        compose.runOnUiThread {playing=false};compose.mainClock.advanceTimeBy(32);compose.waitForIdle();val paused=pixels()
        compose.mainClock.advanceTimeBy(160);compose.waitForIdle();assertTrue(paused.contentEquals(pixels()))
        compose.mainClock.autoAdvance=true
    }
}
