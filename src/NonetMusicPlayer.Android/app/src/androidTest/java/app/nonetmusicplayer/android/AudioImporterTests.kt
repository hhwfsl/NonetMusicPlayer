package app.nonetmusicplayer.android

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.net.Uri
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import app.nonetmusicplayer.android.data.AudioImporter
import androidx.documentfile.provider.DocumentFile
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.ViewModelStore
import androidx.lifecycle.ViewModelStoreOwner
import app.nonetmusicplayer.android.playback.PlayerViewModel
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.ByteBuffer

/** 使用测试 APK 的小音频与合成 APIC 验证原生标签读取，不向正式 APK 放入示例歌曲。 */
@RunWith(AndroidJUnit4::class)
class AudioImporterTests {
    private fun embeddedSong(folder: File): File {
        folder.mkdirs();val payload=byteArrayOf(3)+"jpn".toByteArray()+byteArrayOf(0)+"[00:00.000]内嵌歌词\n[00:02.000]次の行".toByteArray()
        val frame="USLT".toByteArray()+ByteBuffer.allocate(4).putInt(payload.size).array()+byteArrayOf(0,0)+payload
        val size=frame.size;val file=File(folder,"embedded.mp3")
        file.outputStream().use {it.write("ID3".toByteArray()+byteArrayOf(3,0,0,(size shr 21 and 127).toByte(),(size shr 14 and 127).toByte(),(size shr 7 and 127).toByte(),(size and 127).toByte()));it.write(frame);InstrumentationRegistry.getInstrumentation().context.assets.open("tone.mp3").use {audio->audio.copyTo(it)}}
        return file
    }
    @Test fun folderImportPreservesEmbeddedAndManualSidecarWins() = runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val folder=File(app.filesDir,"folder-lyrics-${java.util.UUID.randomUUID()}")
        val source=embeddedSong(folder);val before=source.readBytes();val importer=AudioImporter(app)
        val track=importer.scanFolder(DocumentFile.fromFile(folder)).single()
        assertTrue(track.lyrics.startsWith("embedded:"));assertTrue(track.lyrics.contains("内嵌歌词"))
        assertFalse(File(app.filesDir,"Lyrics").listFiles().orEmpty().any {it.name.startsWith(track.id)})
        File(folder,"embedded.lrc").writeText("[00:00.000]手动指定")
        val manual=importer.scanFolder(DocumentFile.fromFile(folder)).single()
        assertFalse(manual.lyrics.startsWith("embedded:"));assertTrue(File(manual.lyrics).readText().contains("手动指定"))
        File(folder,"embedded.lrc").writeText("[ar:invalid]")
        assertTrue(importer.scanFolder(DocumentFile.fromFile(folder)).single().lyrics.startsWith("embedded:"))
        assertArrayEquals(before,source.readBytes())
    }
    @androidx.media3.common.util.UnstableApi
    @Test fun oldImportedTrackReadsEmbeddedWithoutReimportAndUnlinkWins() = runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>();val source=embeddedSong(File(app.filesDir,"old-lyrics-${java.util.UUID.randomUUID()}"))
        val track=AudioImporter(app).read(Uri.fromFile(source)).copy(lyrics="")
        val store=ViewModelStore();val owner=object: ViewModelStoreOwner {override val viewModelStore=store}
        val vm=ViewModelProvider(owner,ViewModelProvider.AndroidViewModelFactory.getInstance(app))[PlayerViewModel::class.java]
        try {
            assertEquals("内嵌歌词",vm.lyricsFor(track).first().text)
            assertEquals("次の行",vm.lyricsFor(track).last().text)
            assertTrue(vm.lyricText(track).contains("内嵌歌词"))
            assertTrue(vm.lyricsFor(track.copy(lyrics="none:")).isEmpty())
            val manual=File(source.parentFile,"manual.lrc").apply {writeText("[00:00]手动优先")}
            assertEquals("手动优先",vm.lyricsFor(track.copy(lyrics=manual.absolutePath)).first().text)
        } finally {InstrumentationRegistry.getInstrumentation().runOnMainSync {store.clear()}}
    }
    @Test fun embeddedArtworkAndSeparateLyricsAreImported() = runBlocking {
        val app=ApplicationProvider.getApplicationContext<MusicApplication>()
        val bitmap=Bitmap.createBitmap(32,32,Bitmap.Config.ARGB_8888).apply {eraseColor(Color.rgb(80,130,200))}
        val bytes=ByteArrayOutputStream().also {bitmap.compress(Bitmap.CompressFormat.PNG,100,it)}.toByteArray();bitmap.recycle()
        val payload=byteArrayOf(0)+"image/png".toByteArray(Charsets.US_ASCII)+byteArrayOf(0,3,0)+bytes
        val lyricPayload=byteArrayOf(3)+"eng".toByteArray()+byteArrayOf(0)+"[00:00.000]Embedded lyric".toByteArray()
        val lyricFrame="USLT".toByteArray()+ByteBuffer.allocate(4).putInt(lyricPayload.size).array()+byteArrayOf(0,0)+lyricPayload
        val frame="APIC".toByteArray()+ByteBuffer.allocate(4).putInt(payload.size).array()+byteArrayOf(0,0)+payload+lyricFrame
        val size=frame.size
        val header="ID3".toByteArray()+byteArrayOf(3,0,0,(size shr 21 and 127).toByte(),(size shr 14 and 127).toByte(),(size shr 7 and 127).toByte(),(size and 127).toByte())
        val file=File(app.filesDir,"metadata-fixture-${java.util.UUID.randomUUID()}.mp3")
        val audio=InstrumentationRegistry.getInstrumentation().context.assets.open("tone.mp3").use {it.readBytes()}
        file.outputStream().use {it.write(header);it.write(frame);it.write(audio)}
        val importer=AudioImporter(app)
        val track=importer.read(Uri.fromFile(file),false,"metadata-fixture.mp3")
        assertEquals("embedded:[00:00.000]Embedded lyric",track.lyrics)
        assertFalse(File(app.filesDir,"Lyrics/${track.id}.lrc").exists())
        assertTrue(track.durationMs>0);assertFalse(track.inLibrary);assertTrue(File(track.cover).isFile)
        val cover=BitmapFactory.decodeFile(track.cover);assertEquals(32,cover.width);assertEquals(32,cover.height);cover.recycle()
        val lyric=File(app.filesDir,"metadata-fixture.lrc").apply {writeText("[00:00.000]第一行\n[00:01.234]翻译测试")}
        val imported=File(importer.importLyrics(track.id,Uri.fromFile(lyric)))
        assertEquals("Lyrics",imported.parentFile!!.name);assertTrue(imported.readText().contains("翻译测试"))
    }
}
