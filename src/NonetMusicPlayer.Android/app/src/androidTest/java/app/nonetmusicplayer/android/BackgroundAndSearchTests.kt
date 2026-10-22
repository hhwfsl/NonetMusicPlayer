package app.nonetmusicplayer.android

import android.content.Intent
import android.os.ParcelFileDescriptor
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.compose.ui.test.*
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.network.*
import kotlinx.coroutines.*
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import org.junit.*
import org.junit.Assert.*
import org.junit.runner.RunWith
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

/** 真实前台服务、HTTPS 和输入法回归，不依赖界面保持焦点，不访问真实网络设备。 */
@RunWith(AndroidJUnit4::class)
@androidx.media3.common.util.UnstableApi
class BackgroundAndSearchTests {
    @get:Rule val compose=createAndroidComposeRule<MainActivity>()
    private fun shell(command: String)=ParcelFileDescriptor.AutoCloseInputStream(InstrumentationRegistry.getInstrumentation().uiAutomation.executeShellCommand(command)).bufferedReader().use {it.readText()}
    // dumpsys 还包含已释放锁的历史日志，只检查当前 PARTIAL_WAKE_LOCK 条目。
    private fun connectionLockHeld()=shell("dumpsys power").lineSequence().any {it.trimStart().startsWith("PARTIAL_WAKE_LOCK") && it.contains("'Nonet:lan-connection'")}
    private fun resume() {
        shell("input keyevent KEYCODE_WAKEUP");shell("wm dismiss-keyguard")
        // 与启动器保持相同的 Intent 身份，避免创建测试 Scenario 无法跟踪的 Activity。
        compose.activity.startActivity(Intent(compose.activity,MainActivity::class.java).setAction(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
        compose.activityRule.scenario.moveToState(androidx.lifecycle.Lifecycle.State.RESUMED)
    }
    @Test fun searchButtonFocusesInputAndShowsKeyboardOnEveryRequest() {
        compose.onNodeWithTag("open_music_search").performClick()
        compose.waitUntil(5000) {compose.onAllNodes(hasTestTag("search_input") and isFocused()).fetchSemanticsNodes().isNotEmpty()}
        compose.onNodeWithTag("search_input").assertIsFocused().performTextInput("Search focus")
        fun keyboardVisible()=ViewCompat.getRootWindowInsets(compose.activity.window.decorView)?.isVisible(WindowInsetsCompat.Type.ime())==true
        compose.waitUntil(5000) {keyboardVisible()}
        compose.onNodeWithTag("search_input").performImeAction();compose.onNodeWithTag("search_input").assertIsNotFocused()
        compose.onNodeWithTag("open_music_search").performClick()
        compose.waitUntil(5000) {keyboardVisible()}
        compose.onNodeWithTag("search_input").assertIsFocused()
        compose.onNodeWithTag("search_input").performImeAction()
    }
    @Test fun connectionSurvivesBackgroundScreenOffAndOneHeartbeatTimeout() = runBlocking {
        val app=compose.activity.application as MusicApplication
        val id=UUID.randomUUID().toString().replace("-","")
        val (ssl,pin)=PeerSecurity.identity();val reads=AtomicInteger();val delayOnce=AtomicBoolean(false)
        var reverse: ReverseAccess?=null
        val fixture=PeerServer(id,"Background fixture",ssl,pin,{
            reads.incrementAndGet()
            // 只阻塞一个心跳，超过其四秒请求期限；下一次探测必须可以恢复原会话。
            if(delayOnce.compareAndSet(true,false)) Thread.sleep(5500)
            JSONObject().put("deviceId",id).put("name","Background fixture").put("deviceKind","desktop").put("revision","one").put("total",0).put("tracks",JSONArray()).put("playlists",JSONArray())
        },{_,_->null},approve={true},reverse={reverse=it;true})
        try {
            app.lan.connect(fixture.invitation("127.0.0.1"))
            withTimeout(15000) {while(app.lan.state.value.peers.none {it.id==id} || app.lan.state.value.connecting) delay(30)}
            withTimeout(5000) {while(!connectionLockHeld()) delay(100)}
            delayOnce.set(true)
            compose.runOnUiThread {compose.activity.moveTaskToBack(true)}
            withTimeout(25000) {while(reads.get()<4) {assertTrue(app.lan.state.value.peers.any {it.id==id});delay(150)}}
            shell("input keyevent KEYCODE_SLEEP");delay(6000)
            assertTrue(app.lan.state.value.peers.any {it.id==id})
            assertTrue(connectionLockHeld())
            val access=requireNotNull(reverse)
            withContext(Dispatchers.IO) {
                PeerSecurity.client(access.invitation).newCall(Request.Builder().url(access.invitation.origin.resolve("v1/info")!!).header("Authorization","Bearer ${access.token}").build()).execute().use {
                    assertEquals(200,it.code);assertEquals(app.lan.deviceId,JSONObject(it.body!!.string()).getString("deviceId"))
                }
            }
            resume();assertTrue(app.lan.state.value.peers.any {it.id==id})
            app.lan.disconnect(id)
            withTimeout(5000) {while(app.lan.state.value.peers.any {it.id==id} || connectionLockHeld()) delay(100)}
        } finally {resume();app.lan.disconnect(id);app.lan.setSharing(false);fixture.close()}
    }
}
