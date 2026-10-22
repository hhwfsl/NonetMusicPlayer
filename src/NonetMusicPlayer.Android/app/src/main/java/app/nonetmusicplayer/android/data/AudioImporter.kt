package app.nonetmusicplayer.android.data

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.provider.OpenableColumns
import androidx.documentfile.provider.DocumentFile
import app.nonetmusicplayer.android.domain.LrcParser
import app.nonetmusicplayer.android.domain.Track
import app.nonetmusicplayer.android.domain.readLimited
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.currentCoroutineContext
import java.io.File
import java.security.MessageDigest

/** 标签和图片处理在 IO 线程执行；只保存小封面，不复制整首音频。 */
class AudioImporter(private val context: Context) {
    private val formats=setOf("mp3","flac","wav","ogg","opus","m4a","aac","mp4","aiff","aif")
    fun accepts(name: String) = name.substringAfterLast('.').lowercase() in formats
    suspend fun read(uri: Uri, managed: Boolean = true, name: String = "", minimumDurationMs: Long = 0): Track = withContext(Dispatchers.IO) {
        val id=MessageDigest.getInstance("SHA-256").digest(uri.toString().toByteArray()).joinToString("") { "%02x".format(it) }
        val display=if(name.isNotBlank()) name else context.contentResolver.query(uri,arrayOf(OpenableColumns.DISPLAY_NAME),null,null,null)?.use { if(it.moveToFirst()) it.getString(0) else "" }.orEmpty()
        val metadata=MediaMetadataRetriever()
        try {
            metadata.setDataSource(context,uri)
            val title=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE)?.takeIf { it.isNotBlank() } ?: display.substringBeforeLast('.').ifBlank { "Audio" }
            val artist=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ARTIST).orEmpty()
            val album=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_ALBUM).orEmpty()
            val duration=metadata.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull() ?: 0
            // 时长过滤必须先于封面解码和内嵌歌词读取，过滤掉的音频不产生应用文件。
            require(minimumDurationMs<=0 || duration>=minimumDurationMs) {"Audio shorter than scan threshold"}
            val cover=runCatching { saveCover(id,metadata.embeddedPicture) }.getOrDefault("")
            val embedded=EmbeddedLyrics.read(context,uri)
            Track(id,uri.toString(),title,artist,album,duration,cover,if(embedded.isBlank()) "" else "embedded:"+embedded,inLibrary=managed)
        } finally { metadata.release() }
    }
    private fun saveCover(id: String, bytes: ByteArray?): String {
        if(bytes==null || bytes.size>20_000_000) return ""
        val size=BitmapFactory.Options().apply { inJustDecodeBounds=true };BitmapFactory.decodeByteArray(bytes,0,bytes.size,size)
        if(size.outWidth<=0 || size.outHeight<=0) return ""
        var sample=1;while(size.outWidth/sample>1024 || size.outHeight/sample>1024) sample*=2
        val bitmap=BitmapFactory.decodeByteArray(bytes,0,bytes.size,BitmapFactory.Options().apply { inSampleSize=sample;inPreferredConfig=Bitmap.Config.RGB_565 }) ?: return ""
        val file=File(context.filesDir,"Artwork/$id.jpg").apply { parentFile?.mkdirs() }
        try { file.outputStream().use { bitmap.compress(Bitmap.CompressFormat.JPEG,88,it) } } finally { bitmap.recycle() }
        return file.absolutePath
    }
    suspend fun scanFolder(uri: Uri): List<Track> = withContext(Dispatchers.IO) {
        val root=DocumentFile.fromTreeUri(context,uri) ?: error("Folder inaccessible")
        scanFolder(root)
    }
    /** 独立歌词优先，但没有独立文件时必须保留已读到的内嵌歌词。 */
    internal suspend fun scanFolder(root: DocumentFile, minimumDurationMs: Long = 0, excluded: (Uri)->Boolean = {false}): List<Track> = withContext(Dispatchers.IO) {
        val result=mutableListOf<Track>()
        suspend fun visit(folder: DocumentFile, depth: Int) {
            currentCoroutineContext().ensureActive()
            if(excluded(folder.uri)) return
            check(depth<=64 && result.size<10000) {"Scan limit reached"}
            check(folder.canRead()) {"Folder inaccessible"}
            val children=folder.listFiles();val lyrics=children.filter { it.isFile && it.name?.substringAfterLast('.')?.lowercase() in setOf("lrc","txt") }.associateBy { it.name!!.substringBeforeLast('.').lowercase() }
            children.forEach { file ->
                currentCoroutineContext().ensureActive()
                check(result.size<10000) {"Scan limit reached"}
                if(excluded(file.uri)) return@forEach
                if(file.isDirectory) visit(file,depth+1)
                else if(accepts(file.name.orEmpty())) {
                    val track=runCatching {
                        read(file.uri,true,file.name.orEmpty(),minimumDurationMs)
                    }.getOrNull() ?: return@forEach
                    val path=lyrics[file.name!!.substringBeforeLast('.').lowercase()]?.let { runCatching { importLyricsBlocking(track.id,it.uri) }.getOrNull() }.orEmpty()
                    result+=if(path.isNotBlank()) track.copy(lyrics=path) else track
                }
            }
        }
        visit(root,0);result
    }
    suspend fun importLyrics(trackId: String, uri: Uri): String = withContext(Dispatchers.IO) { importLyricsBlocking(trackId,uri) }
    private fun importLyricsBlocking(trackId: String, uri: Uri): String {
        val name=context.contentResolver.query(uri,arrayOf(OpenableColumns.DISPLAY_NAME),null,null,null)?.use {if(it.moveToFirst()) it.getString(0) else ""}.orEmpty()
        require(name.isBlank() || name.substringAfterLast('.').lowercase() in setOf("lrc","txt"))
        val bytes=context.contentResolver.openInputStream(uri)?.use { it.readLimited(2_000_000) } ?: error("Lyrics inaccessible")
        require(bytes.size<=2_000_000)
        val text=when {
            bytes.size>=2 && bytes[0]==0xFF.toByte() && bytes[1]==0xFE.toByte() ->bytes.toString(Charsets.UTF_16LE).removePrefix("\uFEFF")
            bytes.size>=2 && bytes[0]==0xFE.toByte() && bytes[1]==0xFF.toByte() ->bytes.toString(Charsets.UTF_16BE).removePrefix("\uFEFF")
            else ->runCatching {Charsets.UTF_8.newDecoder().decode(java.nio.ByteBuffer.wrap(bytes)).toString()}.getOrElse {bytes.toString(charset("GB18030"))}
        }
        require(LrcParser.parse(text).isNotEmpty()) { "Empty or unsupported lyrics" }
        val safeId=trackId.replace(Regex("[^a-zA-Z0-9_-]"),"_")
        val file=File(context.filesDir,"Lyrics/$safeId-${java.util.UUID.randomUUID()}.lrc").apply { parentFile?.mkdirs() };file.writeText(text.removePrefix("\uFEFF"))
        return file.absolutePath
    }
}
