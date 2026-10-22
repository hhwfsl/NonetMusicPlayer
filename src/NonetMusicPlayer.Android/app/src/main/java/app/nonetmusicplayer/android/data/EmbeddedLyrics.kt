package app.nonetmusicplayer.android.data

import android.content.Context
import android.net.Uri
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.Locale

/** 只读取歌词元数据；封面和音频直接跳过，不把整首音频载入内存或另存 LRC。 */
object EmbeddedLyrics {
    private const val MAX_TEXT=2_000_000
    private const val MAX_TAG=64_000_000L
    private val keys=setOf("LYRICS","UNSYNCEDLYRICS","UNSYNCED LYRICS","LYRICS_UNSYNCED","SYNCEDLYRICS")
    private val timed=Regex("(?m)^\\s*\\[\\d{1,4}:\\d{2}")
    fun read(context: Context,uri: Uri): String = runCatching {
        context.contentResolver.openInputStream(uri)?.use {read(it)}.orEmpty()
    }.getOrDefault("")

    /** 流入口供普通文件、content URI 和纯解析测试复用，损坏标签退化为无歌词。 */
    internal fun read(input: InputStream): String = runCatching {
        val stream=input.buffered();stream.mark(16)
        val prefix=stream.exact(4);stream.reset()
        when {
            prefix.copyOfRange(0,3).toString(Charsets.ISO_8859_1)=="ID3" -> {
                val result=id3(stream,stream.exact(10))
                if(result.isNotBlank()) result else if(stream.exact(4).toString(Charsets.US_ASCII)=="fLaC") flac(stream) else ""
            }
            prefix.toString(Charsets.US_ASCII)=="fLaC" -> {stream.exact(4);flac(stream)}
            prefix.toString(Charsets.US_ASCII)=="OggS" -> ogg(stream)
            prefix.toString(Charsets.US_ASCII)=="RIFF" -> wave(stream)
            else -> {stream.mark(12);val header=stream.exact(8);stream.reset();if(header.copyOfRange(4,8).toString(Charsets.US_ASCII)=="ftyp") mp4(stream,Long.MAX_VALUE,0) else ""}
        }
    }.getOrDefault("")

    private fun InputStream.exact(count: Int): ByteArray {
        require(count in 0..MAX_TEXT)
        val result=ByteArray(count);var position=0
        while(position<count) {val n=read(result,position,count-position);require(n>0);position+=n}
        return result
    }
    private fun InputStream.discard(count: Long) {
        require(count>=0);var left=count
        while(left>0) {val n=skip(left);if(n>0) left-=n else {require(read()>=0);left--}}
    }
    private fun uint(bytes: ByteArray,offset: Int=0): Long = ByteBuffer.wrap(bytes,offset,4).int.toLong() and 0xffffffffL
    private fun sync(bytes: ByteArray,offset: Int): Int {var n=0;repeat(4) {require(bytes[offset+it].toInt() and 128==0);n=(n shl 7) or (bytes[offset+it].toInt() and 127)};return n}

    /** 子流保留真实边界；跳过大封面不分配与封面等大的 byte[]。 */
    private class Slice(private val source: InputStream,var left: Long): InputStream() {
        override fun read(): Int {if(left==0L) return -1;val b=source.read();if(b>=0) left--;return b}
        override fun read(b: ByteArray,off: Int,len: Int): Int {if(len==0) return 0;if(left==0L) return -1;val n=source.read(b,off,minOf(left,len.toLong()).toInt());if(n>0) left-=n;return n}
        override fun skip(n: Long): Long {val skipped=source.skip(minOf(n.coerceAtLeast(0),left));left-=skipped;return skipped}
        fun finish() {discard(left)}
    }
    private class Unsync(private val source: InputStream): InputStream() {
        private var ff=false
        override fun read(): Int {var b=source.read();if(ff && b==0) b=source.read();ff=b==255;return b}
    }
    private fun choose(old: String,candidate: String): String {
        val value=candidate.trim('\u0000','\uFEFF',' ','\r','\n')
        return if(value.isBlank()) old else if(old.isBlank() || (!timed.containsMatchIn(old) && timed.containsMatchIn(value))) value else old
    }
    private fun decode(bytes: ByteArray,start: Int,encoding: Int,hint: Int=start): String {
        if(start>=bytes.size) return ""
        val charset=when(encoding) {
            1 -> when {bytes[start]==0xff.toByte() || bytes[start]==0xfe.toByte()->Charsets.UTF_16;hint+1<bytes.size && bytes[hint]==0xfe.toByte()->Charsets.UTF_16BE;else->Charsets.UTF_16LE}
            2->Charsets.UTF_16BE;3->Charsets.UTF_8;else->Charsets.ISO_8859_1
        }
        return bytes.copyOfRange(start,bytes.size).toString(charset).trim('\u0000','\uFEFF')
    }
    private fun terminator(bytes: ByteArray,start: Int,encoding: Int): Int {
        val unit=if(encoding in 1..2) 2 else 1;var end=start
        while(end+unit<=bytes.size) {if(bytes[end]==0.toByte() && (unit==1 || bytes[end+1]==0.toByte())) return end;end+=unit}
        return bytes.size
    }
    private fun id3(input: InputStream,header: ByteArray): String {
        val version=header[3].toInt();require(version in 2..4)
        val length=sync(header,6);require(length.toLong()<=MAX_TAG)
        val raw=Slice(input,length.toLong());val flags=header[5].toInt() and 255
        val stream=if(version<4 && flags and 128!=0) Unsync(raw) else raw
        if(version==2 && flags and 64!=0) {raw.finish();return ""}
        if(version>=3 && flags and 64!=0) {
            val ext=stream.exact(4);val size=if(version==4) sync(ext,0).toLong()-4 else uint(ext)
            require(size in 0..MAX_TAG);stream.discard(size)
        }
        var result="";var frames=0
        while(raw.left>=(if(version==2) 6 else 10) && frames++<10000) {
            val frame=stream.exact(if(version==2) 6 else 10)
            val id=frame.copyOfRange(0,if(version==2) 3 else 4).toString(Charsets.US_ASCII)
            if(!id.matches(Regex("[A-Z0-9]{3,4}"))) break
            val size=if(version==2) ((frame[3].toInt() and 255) shl 16 or ((frame[4].toInt() and 255) shl 8) or (frame[5].toInt() and 255)).toLong() else if(version==4) sync(frame,4).toLong() else uint(frame,4)
            require(size in 0..MAX_TAG)
            val payload=Slice(stream,size)
            val frameFlags=if(version==2) 0 else frame[9].toInt() and 255
            val unsupported=if(version==4) frameFlags and 12!=0 else frameFlags and 192!=0
            if(!unsupported && id in setOf("USLT","ULT","TXXX","TXX") && size in 1..MAX_TEXT.toLong()) {
                var bytes=payload.exact(size.toInt())
                if(version==4 && (frameFlags and 2!=0 || flags and 128!=0)) bytes=Unsync(bytes.inputStream()).readBytes()
                val prefix=if(version==4) (if(frameFlags and 64!=0) 1 else 0)+(if(frameFlags and 1!=0) 4 else 0) else if(frameFlags and 32!=0) 1 else 0
                if(prefix>0) bytes=bytes.copyOfRange(prefix.coerceAtMost(bytes.size),bytes.size)
                if(bytes.isNotEmpty()) {
                    val encoding=bytes[0].toInt();val start=if(id=="USLT" || id=="ULT") 4 else 1
                    val end=terminator(bytes,start,encoding);val unit=if(encoding in 1..2) 2 else 1
                    if(id in setOf("USLT","ULT") || decode(bytes.copyOfRange(0,end),start,encoding).uppercase(Locale.ROOT) in keys)
                        result=choose(result,decode(bytes,end+unit,encoding,start))
                }
            }
            payload.finish()
        }
        raw.finish();if(version==4 && flags and 16!=0) input.discard(10)
        return result
    }
    private fun comments(input: InputStream): String {
        fun little()=ByteBuffer.wrap(input.exact(4)).order(ByteOrder.LITTLE_ENDIAN).int
        val vendor=little();require(vendor in 0..MAX_TEXT);input.discard(vendor.toLong())
        val count=little();require(count in 0..10000);var result=""
        repeat(count) {
            val length=little();require(length.toLong() in 0..MAX_TAG)
            val prefix=input.exact(minOf(length,64));val key=prefix.toString(Charsets.UTF_8).substringBefore('=').uppercase(Locale.ROOT)
            if(key in keys && prefix.contains('='.code.toByte()) && length<=MAX_TEXT)
                result=choose(result,(prefix+input.exact(length-prefix.size)).toString(Charsets.UTF_8).substringAfter('='))
            else input.discard((length-prefix.size).toLong())
        }
        return result
    }
    private fun flac(input: InputStream): String {
        var metadata=0L;var result=""
        repeat(10000) {
            val h=input.exact(4);val size=((h[1].toInt() and 255) shl 16) or ((h[2].toInt() and 255) shl 8) or (h[3].toInt() and 255)
            metadata+=size+4;require(metadata<=MAX_TAG)
            val block=Slice(input,size.toLong())
            if(h[0].toInt() and 127==4) result=choose(result,comments(block))
            block.finish();if(h[0].toInt() and 128!=0) return result
        }
        return result
    }
    private fun ogg(input: InputStream): String {
        val packet=java.io.ByteArrayOutputStream();var serial: Long?=null;var total=0L
        repeat(1024) {
            val h=input.exact(27);require(h.copyOfRange(0,4).toString(Charsets.US_ASCII)=="OggS")
            val segments=input.exact(h[26].toInt() and 255);val pageSerial=uint(h,14)
            for(segment in segments) {
                val size=segment.toInt() and 255;total+=size;require(total<=MAX_TAG)
                val bytes=input.exact(size)
                if(serial==null || serial==pageSerial) {
                    require(packet.size()+size<=MAX_TEXT);packet.write(bytes)
                    if(size<255) {
                        val data=packet.toByteArray();packet.reset()
                        if(data.size>=8 && data.copyOfRange(0,8).toString(Charsets.US_ASCII)=="OpusTags") return comments(data.inputStream().apply {discard(8)})
                        if(data.size>=7 && data[0]==3.toByte() && data.copyOfRange(1,7).toString(Charsets.US_ASCII)=="vorbis") return comments(data.inputStream().apply {discard(7)})
                        if(data.size>=8 && (data.copyOfRange(0,8).toString(Charsets.US_ASCII)=="OpusHead" || data[0]==1.toByte() && data.copyOfRange(1,7).toString(Charsets.US_ASCII)=="vorbis")) serial=pageSerial
                        else if(serial!=null) return ""
                    }
                }
            }
        }
        return ""
    }
    private fun mp4(input: InputStream,limit: Long,depth: Int,lyrics: Boolean=false): String {
        require(depth<10);var left=limit;var result=""
        repeat(4096) {
            if(left<8) return result
            val first=input.read();if(first<0) return result
            val h=byteArrayOf(first.toByte())+input.exact(7);val kind=h.copyOfRange(4,8).toString(Charsets.ISO_8859_1)
            var size=uint(h);var header=8L
            if(size==1L) {size=ByteBuffer.wrap(input.exact(8)).long;header=16}
            if(size==0L) size=left
            require(size>=header && size<=left);val body=Slice(input,size-header)
            if(kind in setOf("moov","udta","meta","ilst","©lyr")) {
                if(kind=="meta") body.discard(4)
                result=choose(result,mp4(body,body.left,depth+1,kind=="©lyr"))
            } else if(kind=="data" && lyrics && body.left in 8..MAX_TEXT.toLong()) {
                // 只有 ©lyr 的 data 才是歌词，避免把封面或其他字符串元数据误认为歌词。
                val type=uint(body.exact(4)).toInt() and 0xffffff;body.discard(4)
                val bytes=body.exact(body.left.toInt())
                result=choose(result,bytes.toString(if(type==2) Charsets.UTF_16BE else Charsets.UTF_8))
            }
            body.finish();left-=size
            if(result.isNotBlank()) return result
        }
        return result
    }
    private fun wave(input: InputStream): String {
        val h=input.exact(12);require(h.copyOfRange(8,12).toString(Charsets.US_ASCII)=="WAVE")
        var left=ByteBuffer.wrap(h,4,4).order(ByteOrder.LITTLE_ENDIAN).int.toLong().and(0xffffffffL)-4
        repeat(4096) {
            if(left<8) return ""
            val chunk=input.exact(8);val size=ByteBuffer.wrap(chunk,4,4).order(ByteOrder.LITTLE_ENDIAN).int.toLong().and(0xffffffffL)
            require(size<=left-8);val body=Slice(input,size)
            if(chunk.copyOfRange(0,4).toString(Charsets.US_ASCII).trim().equals("id3",true)) {val result=id3(body,body.exact(10));if(result.isNotBlank()) return result}
            body.finish();if(size%2==1L) input.discard(1);left-=8+size+size%2
        }
        return ""
    }
}
