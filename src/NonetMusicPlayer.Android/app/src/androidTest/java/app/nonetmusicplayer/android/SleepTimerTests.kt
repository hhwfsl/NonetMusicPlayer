package app.nonetmusicplayer.android

import android.content.ComponentName
import android.os.Bundle
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.*
import androidx.test.core.app.ApplicationProvider
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.playback.PlaybackService
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.*
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.UUID
import java.util.concurrent.TimeUnit

/** 真实音频时钟覆盖后台到时、取消以及单曲循环下仅播完当前一首。 */
@UnstableApi
class SleepTimerTests {
    @Test fun timerStopsImmediatelyAndCanBeRestarted()=runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>()
        // 每次测试先结束上次任务；新建 Activity 应能重新打开已经定时退出的同一缓存进程。
        val scenario=androidx.test.core.app.ActivityScenario.launch(MainActivity::class.java)
        val future=withContext(Dispatchers.Main) {MediaController.Builder(app,SessionToken(app,ComponentName(app,PlaybackService::class.java))).buildAsync()}
        val controller=future.get(15,TimeUnit.SECONDS)
        val id="sleep-immediate-"+UUID.randomUUID();val audio=File(app.cacheDir,"$id.wav")
        val count=48000*10*2
        val header=ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).put("RIFF".toByteArray()).putInt(36+count).put("WAVEfmt ".toByteArray()).putInt(16).putShort(1).putShort(1).putInt(48000).putInt(96000).putShort(2).putShort(16).put("data".toByteArray()).putInt(count)
        audio.outputStream().use {it.write(header.array());it.write(ByteArray(count))}
        try {
            app.repository.storeTrack(Track(id,audio.toURI().toString(),"Immediate fixture",durationMs=10000,inLibrary=false))
            val command=withContext(Dispatchers.Main) {app.sleepTimer.onNewTask();controller.sendCustomCommand(PlaybackService.PLAY_QUEUE,Bundle().apply {putString("track",id);putStringArrayList("ids",arrayListOf(id))})}
            assertEquals(SessionResult.RESULT_SUCCESS,command.get(5,TimeUnit.SECONDS).resultCode)
            withTimeout(10000) {while(!withContext(Dispatchers.Main) {controller.isPlaying}) delay(40)}
            scenario.onActivity {it.moveTaskToBack(true)}
            withContext(Dispatchers.Main) {app.sleepTimer.arm(250,false)}
            withTimeout(5000) {while(!app.sleepTimer.closing) delay(40)}
            withTimeout(5000) {while(withContext(Dispatchers.Main) {controller.isPlaying}) delay(40)}
            val snapshot=app.repository.restorePlayback();assertEquals(id,snapshot.trackId);assertTrue(snapshot.positionMs in 100..5000)
            delay(200);assertFalse(app.sleepTimer.state.value.active)
        } finally {withContext(Dispatchers.Main) {app.sleepTimer.cancel();controller.release()};scenario.close();audio.delete()}
    }
    @Test fun timerCancelAndFinishCurrentSong()=runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>()
        val scenario=androidx.test.core.app.ActivityScenario.launch(MainActivity::class.java)
        val main=Dispatchers.Main
        val future=withContext(main) {MediaController.Builder(app,SessionToken(app,ComponentName(app,PlaybackService::class.java))).buildAsync()}
        val controller=future.get(15,TimeUnit.SECONDS)
        val id="sleep-"+UUID.randomUUID();val audio=File(app.cacheDir,"$id.wav")
        val count=48000*4*2
        val header=ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).put("RIFF".toByteArray()).putInt(36+count).put("WAVEfmt ".toByteArray()).putInt(16).putShort(1).putShort(1).putInt(48000).putInt(96000).putShort(2).putShort(16).put("data".toByteArray()).putInt(count)
        audio.outputStream().use {it.write(header.array());it.write(ByteArray(count))}
        val original=app.repository.state.value.preferences
        try {
            app.repository.storeTrack(Track(id,audio.toURI().toString(),"Sleep fixture",durationMs=4000,inLibrary=false))
            app.repository.updatePreferences(original.copy(mode="repeat-one"))
            withContext(main) {
                app.sleepTimer.onNewTask()
                app.playQueue.value=listOf(id)
                controller.sendCustomCommand(PlaybackService.PLAY_QUEUE,Bundle().apply {putString("track",id);putStringArrayList("ids",arrayListOf(id))})
            }
            withTimeout(10000) {while(!withContext(main) {controller.isPlaying}) delay(50)}
            withContext(main) {app.sleepTimer.arm(180,false);app.sleepTimer.cancel()}
            delay(350);assertFalse(app.sleepTimer.closing);assertTrue(withContext(main) {controller.isPlaying})
            scenario.onActivity {it.moveTaskToBack(true)}
            withContext(main) {app.sleepTimer.arm(180,true)}
            withTimeout(5000) {while(!app.sleepTimer.state.value.waitingForSong) delay(30)}
            assertTrue(withContext(main) {controller.isPlaying});assertFalse(app.sleepTimer.closing)
            withTimeout(10000) {while(!app.sleepTimer.closing) delay(50)}
            withTimeout(5000) {while(withContext(main) {controller.isPlaying}) delay(50)}
            val snapshot=app.repository.restorePlayback();assertEquals(id,snapshot.trackId);assertTrue(snapshot.positionMs>=3500)
            delay(300);assertFalse(app.sleepTimer.state.value.active)
            withContext(main) {controller.release()};scenario.close()
            val reopened=androidx.test.core.app.ActivityScenario.launch(MainActivity::class.java)
            try {
                var vm: PlayerViewModel?=null
                reopened.onActivity {vm=androidx.lifecycle.ViewModelProvider(it)[PlayerViewModel::class.java]}
                withTimeout(10000) {while(vm?.playback?.value?.ready!=true) delay(50)}
                delay(500);assertFalse(app.sleepTimer.closing);assertFalse(vm!!.playback.value.playing)
            } finally {reopened.close()}
        } finally {
            app.repository.updatePreferences(original)
            withContext(main) {app.sleepTimer.cancel();controller.release()}
            scenario.close();audio.delete()
        }
    }
}
