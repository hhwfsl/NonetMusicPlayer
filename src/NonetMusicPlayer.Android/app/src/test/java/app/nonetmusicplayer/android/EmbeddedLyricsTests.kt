package app.nonetmusicplayer.android

import app.nonetmusicplayer.android.data.EmbeddedLyrics
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** 合成标签只测试格式，不带受版权保护的歌曲；大封面/音频必须跳过而不是整块读取。 */
class EmbeddedLyricsTests {
    private val text="[00:00.000]光の道標\n[00:02.500]第二行"
    private fun big(n: Int)=ByteBuffer.allocate(4).putInt(n).array()
    private fun little(n: Int)=ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(n).array()
    private fun sync(n: Int)=byteArrayOf((n shr 21 and 127).toByte(),(n shr 14 and 127).toByte(),(n shr 7 and 127).toByte(),(n and 127).toByte())
    private fun frame(id: String,payload: ByteArray,version: Int=3)=id.toByteArray()+ (if(version==4) sync(payload.size) else big(payload.size))+byteArrayOf(0,0)+payload
    private fun tag(body: ByteArray,version: Int=3,flags: Int=0)="ID3".toByteArray()+byteArrayOf(version.toByte(),0,flags.toByte())+sync(body.size)+body
    private fun uslt(value: String=text)=byteArrayOf(3)+"eng".toByteArray()+byteArrayOf(0)+value.toByteArray()
    private fun comments(value: String=text)=little(0)+little(2)+little(12)+"TITLE=sample".toByteArray()+("uNsYnCeDlYrIcS=$value".toByteArray().let {little(it.size)+it})
    private fun parse(bytes: ByteArray)=EmbeddedLyrics.read(bytes.inputStream())
    @Test fun id3WithLargeArtworkAndUtf16() {
        val payload=byteArrayOf(1)+"jpn".toByteArray()+byteArrayOf(-1,-2,0,0)+text.toByteArray(Charsets.UTF_16LE)
        assertEquals(text,parse(tag(frame("APIC",ByteArray(3_000_000))+frame("USLT",payload))))
    }
    @Test fun id3Version22And24ExtendedHeader() {
        val payload=uslt("[00:00]Old tag");val size=payload.size
        assertEquals("[00:00]Old tag",parse(tag("ULT".toByteArray()+byteArrayOf((size shr 16).toByte(),(size shr 8).toByte(),size.toByte())+payload,2)))
        assertEquals(text,parse(tag(sync(6)+byteArrayOf(1,0)+frame("USLT",uslt(),4),4,64)))
    }
    @Test fun id3UnsynchronisationAndCustomLyricsFrame() {
        val value="[00:00]ÿá";val payload=byteArrayOf(0)+"eng".toByteArray()+byteArrayOf(0)+value.toByteArray(Charsets.ISO_8859_1)
        val raw=frame("USLT",payload).flatMap {if(it==(-1).toByte()) listOf(it,0.toByte()) else listOf(it)}.toByteArray()
        assertEquals(value,parse(tag(raw,3,128)))
        assertEquals(text,parse(tag(frame("TXXX",byteArrayOf(3)+"LYRICS".toByteArray()+byteArrayOf(0)+text.toByteArray()))))
    }
    @Test fun flacSkipsLargePictureAndReadsCaseInsensitiveComment() {
        val content=comments();val picture=ByteArray(3_000_000)
        fun block(type: Int,bytes: ByteArray)=byteArrayOf(type.toByte(),(bytes.size shr 16).toByte(),(bytes.size shr 8).toByte(),bytes.size.toByte())+bytes
        assertEquals(text,parse("fLaC".toByteArray()+block(6,picture)+block(132,content)))
        assertEquals(text,parse(tag(ByteArray(16))+"fLaC".toByteArray()+block(132,content)))
    }
    @Test fun mp4LyricsAtomSkipsAudioAndDoesNotReadOtherText() {
        fun atom(type: String,bytes: ByteArray)=big(bytes.size+8)+type.toByteArray(Charsets.ISO_8859_1)+bytes
        val body=atom("ftyp","M4A ".toByteArray())+atom("mdat",ByteArray(4_000_000))+atom("moov",atom("udta",atom("meta",ByteArray(4)+atom("ilst",atom("©nam",atom("data",big(1)+big(0)+"Not lyrics".toByteArray()))+atom("©lyr",atom("data",big(1)+big(0)+text.toByteArray()))))))
        var reads=0
        val stream=object: ByteArrayInputStream(body) {override fun read(b: ByteArray,off: Int,len: Int): Int=super.read(b,off,len).also {if(it>0) reads+=it}}
        assertEquals(text,EmbeddedLyrics.read(stream));assertTrue("Audio must be skipped",reads<30_000)
    }
    @Test fun opusCommentContinuesAcrossPages() {
        fun page(bytes: ByteArray,segments: ByteArray,continued: Boolean=false)= "OggS".toByteArray()+byteArrayOf(0,if(continued) 1 else 0)+ByteArray(20)+byteArrayOf(segments.size.toByte())+segments+bytes
        val tags="OpusTags".toByteArray()+comments(text+"x".repeat(260))
        val data=page("OpusHead".toByteArray()+ByteArray(11),byteArrayOf(19))+page(tags.copyOfRange(0,255),byteArrayOf(-1))+page(tags.copyOfRange(255,tags.size),byteArrayOf((tags.size-255).toByte()),true)
        assertEquals(text+"x".repeat(260),parse(data))
    }
    @Test fun waveId3ChunkAndMalformedBounds() {
        val body=tag(frame("USLT",uslt()));val chunks="id3 ".toByteArray()+little(body.size)+body+(if(body.size%2==1) byteArrayOf(0) else byteArrayOf())
        assertEquals(text,parse("RIFF".toByteArray()+little(chunks.size+4)+"WAVE".toByteArray()+chunks))
        assertEquals("",parse("ID3".toByteArray()+byteArrayOf(4,0,0)+sync(100_000_000)))
        assertEquals("",parse("fLaC".toByteArray()+byteArrayOf(-124,0,0,8)+little(Int.MAX_VALUE)))
        assertEquals("",parse(byteArrayOf(0,0,0)))
    }
}
