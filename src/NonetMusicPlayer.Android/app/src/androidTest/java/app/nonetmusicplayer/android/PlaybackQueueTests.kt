package app.nonetmusicplayer.android

import android.content.ComponentName
import android.os.Bundle
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.*
import androidx.test.core.app.ApplicationProvider
import app.nonetmusicplayer.android.domain.*
import app.nonetmusicplayer.android.playback.PlaybackService
import kotlinx.coroutines.*
import org.junit.Assert.*
import org.junit.Test
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.UUID
import java.util.concurrent.TimeUnit

/** 使用真实 Media3 服务确认自然结束、媒体按钮、队列拖动与保存遵循同一份洗牌结果。 */
@UnstableApi
class PlaybackQueueTests {
    @Test fun shuffledQueueIsTheActualPlaybackOrderAndMovesDoNotRestartAudio()=runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>()
        val scenario=androidx.test.core.app.ActivityScenario.launch(MainActivity::class.java)
        val future=withContext(Dispatchers.Main) {MediaController.Builder(app,SessionToken(app,ComponentName(app,PlaybackService::class.java))).buildAsync()}
        val controller=future.get(15,TimeUnit.SECONDS)
        val prefix="queue-"+UUID.randomUUID();val file=File(app.cacheDir,"$prefix.wav")
        val count=48000*4*2
        val header=ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).put("RIFF".toByteArray()).putInt(36+count).put("WAVEfmt ".toByteArray())
            .putInt(16).putShort(1).putShort(1).putInt(48000).putInt(96000).putShort(2).putShort(16).put("data".toByteArray()).putInt(count)
        file.outputStream().use {it.write(header.array());it.write(ByteArray(count))}
        val tracks=List(8) {Track("$prefix-$it",file.toURI().toString(),"Queue fixture $it",durationMs=4000,inLibrary=false)}
        val preferences=app.repository.state.value.preferences
        suspend fun command(command: SessionCommand,args: Bundle=Bundle.EMPTY) {
            val result=withContext(Dispatchers.Main) {controller.sendCustomCommand(command,args)}.get(5,TimeUnit.SECONDS)
            assertEquals(SessionResult.RESULT_SUCCESS,result.resultCode)
        }
        suspend fun current(id: String) = withTimeout(10000) {
            while(!withContext(Dispatchers.Main) {controller.currentMediaItem?.mediaId==id && controller.isPlaying}) delay(30)
        }
        try {
            app.repository.storeTracks(tracks);app.repository.updatePreferences(preferences.copy(mode="shuffle"))
            val selected=tracks[3].id
            command(PlaybackService.PLAY_QUEUE,Bundle().apply {putString("track",selected);putStringArrayList("ids",ArrayList(tracks.map {it.id}))})
            current(selected)
            val initial=app.playQueue.value
            assertEquals(selected,initial.first());assertEquals(tracks.map {it.id}.toSet(),initial.toSet())
            delay(400)
            val before=withContext(Dispatchers.Main) {controller.currentPosition}
            val movedId=initial.last();val delta=1-initial.lastIndex
            command(PlaybackService.MOVE_QUEUE,Bundle().apply {putString("track",movedId);putInt("delta",delta)})
            val order=PlaybackOrder.move(initial,movedId,delta)
            assertEquals(order,app.playQueue.value)
            assertEquals(selected,withContext(Dispatchers.Main) {controller.currentMediaItem?.mediaId})
            assertTrue("排序不能重置音频进度",withContext(Dispatchers.Main) {controller.currentPosition}>=before)
            withContext(Dispatchers.Main) {controller.seekTo(3800)}
            current(order[1]);assertEquals(order,app.playQueue.value)
            command(PlaybackService.PREVIOUS);current(order[0])
            for(id in order.drop(1)) {command(PlaybackService.NEXT);current(id);assertEquals(order,app.playQueue.value)}
            command(PlaybackService.NEXT);current(order[0])
            val manuallySelected=order[4]
            command(PlaybackService.PLAY_QUEUE,Bundle().apply {putString("track",manuallySelected);putStringArrayList("ids",ArrayList(order));putBoolean("keepSourceOrder",true)})
            current(manuallySelected)
            val fresh=app.playQueue.value
            assertEquals(manuallySelected,fresh.first());assertEquals(order.toSet(),fresh.toSet());assertNotEquals(order,fresh)
            withContext(Dispatchers.Main) {controller.pause()}
            // pause 是异步媒体会话命令；等待服务确认且位置连续稳定，再验证切换模式不会重置进度。
            withTimeout(5000) {
                var previousPosition=-1L;var stableSamples=0
                while(stableSamples<3) {
                    delay(50)
                    val state=withContext(Dispatchers.Main) {Triple(controller.isPlaying,controller.playWhenReady,controller.currentPosition)}
                    stableSamples=if(!state.first && !state.second && state.third==previousPosition) stableSamples+1 else 0
                    previousPosition=state.third
                }
            }
            withTimeout(8000) {while(app.repository.restorePlayback().let {it.queueIds!=fresh || it.trackId!=manuallySelected}) delay(50)}
            // 手动切换到单曲循环仍由既有模式控制；排序和选歌不修改用户模式。
            assertEquals("shuffle",app.repository.state.value.preferences.mode)
            assertEquals(tracks.map {it.id},app.repository.restorePlayback().sourceQueueIds)
            val position=withContext(Dispatchers.Main) {controller.currentPosition}
            app.repository.updatePreferences(preferences.copy(mode="repeat-all"))
            withTimeout(5000) {while(app.playQueue.value!=tracks.map {it.id}) delay(20)}
            assertEquals(manuallySelected,withContext(Dispatchers.Main) {controller.currentMediaItem?.mediaId})
            assertEquals(position,withContext(Dispatchers.Main) {controller.currentPosition})
            command(PlaybackService.NEXT);current(tracks[(tracks.indexOfFirst {it.id==manuallySelected}+1)%tracks.size].id)
            app.repository.updatePreferences(preferences.copy(mode="shuffle"))
            withTimeout(5000) {while(app.playQueue.value.first()!=withContext(Dispatchers.Main) {controller.currentMediaItem?.mediaId}) delay(20)}
            app.repository.updatePreferences(preferences.copy(mode="repeat-one"))
            withTimeout(5000) {while(app.playQueue.value!=tracks.map {it.id}) delay(20)}
        } finally {
            withContext(Dispatchers.Main) {controller.pause();controller.release()}
            app.repository.updatePreferences(preferences);scenario.close();file.delete()
        }
    }
}
