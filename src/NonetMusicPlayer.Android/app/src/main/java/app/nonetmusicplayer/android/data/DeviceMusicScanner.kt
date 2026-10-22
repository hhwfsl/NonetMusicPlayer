package app.nonetmusicplayer.android.data

import android.content.ContentUris
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.DocumentsContract
import android.provider.MediaStore
import androidx.documentfile.provider.DocumentFile
import app.nonetmusicplayer.android.domain.*
import kotlinx.coroutines.*

/** 本地存储扫描：有包含目录时仅遍历授权树；排除目录在读取音频元数据之前生效。 */
class DeviceMusicScanner(private val context: Context, private val importer: AudioImporter) {
    companion object {
        fun location(uri: Uri): ScanLocation? = runCatching {
            if(uri.authority!="com.android.externalstorage.documents") return null
            val id=runCatching {DocumentsContract.getDocumentId(uri)}.getOrElse {DocumentsContract.getTreeDocumentId(uri)}
            ScanLocation.documentId(id)
        }.getOrNull()
        fun label(value: String): String = location(Uri.parse(value))?.let {"${it.volume}:/${it.path}"} ?: Uri.decode(value)
    }
    suspend fun scan(options: ScanOptions): List<Track> = withContext(Dispatchers.IO) {
        val excludes=options.excludedFolders.map {requireNotNull(location(Uri.parse(it))) {"Invalid excluded folder"}}
        fun excluded(uri: Uri): Boolean = location(uri)?.let {key ->excludes.any {it.contains(key)}} == true
        if(options.includedFolders.isNotEmpty()) {
            val roots=options.includedFolders.distinct().map {value ->Uri.parse(value) to requireNotNull(location(Uri.parse(value)))}
            // 重叠的包含目录只遍历一次；权限失效时中止，不回退为全设备扫描。
            val minimal=roots.filter {(_,key) ->roots.none {(_,other) ->other!=key && other.contains(key)}}.distinctBy {it.second}
            val result=mutableListOf<Track>()
            for((uri,_) in minimal) {
                ensureActive()
                if(excluded(uri)) continue
                val root=requireNotNull(DocumentFile.fromTreeUri(context,uri));check(root.canRead() && root.isDirectory) {"Scan folder permission unavailable"}
                result+=importer.scanFolder(root,options.minimumSeconds*1000L,::excluded)
                check(result.size<=10000) {"Scan limit reached"}
            }
            return@withContext result.distinctBy {it.id}
        }
        val media=MediaStore.Audio.Media.EXTERNAL_CONTENT_URI
        val columns=if(Build.VERSION.SDK_INT>=29) arrayOf(MediaStore.Audio.Media._ID,MediaStore.Audio.Media.RELATIVE_PATH,MediaStore.Audio.Media.VOLUME_NAME)
            else arrayOf(MediaStore.Audio.Media._ID,MediaStore.Audio.Media.DATA)
        val result=mutableListOf<Track>()
        val selection=MediaStore.Audio.Media.IS_MUSIC+" != 0 AND "+MediaStore.Audio.Media.DURATION+" >= ?"
        val cursor=context.contentResolver.query(media,columns,selection,arrayOf((options.minimumSeconds*1000L).toString()),null)
            ?: error("Device media index unavailable")
        cursor.use {c ->while(c.moveToNext()) {
            ensureActive()
            val location=if(Build.VERSION.SDK_INT>=29) ScanLocation.documentId(c.getString(2)+":"+c.getString(1).orEmpty()) else {
                val path=c.getString(1).orEmpty();val primary=Environment.getExternalStorageDirectory().absolutePath
                when {path.startsWith("$primary/") ->ScanLocation.documentId("primary:"+path.removePrefix("$primary/").substringBeforeLast('/',""))
                    path.startsWith("/storage/") ->path.removePrefix("/storage/").let {ScanLocation.documentId(it.substringBefore('/')+":"+it.substringAfter('/').substringBeforeLast('/',""))};else ->null}
            }
            // 无法证明不在排除目录内的条目不导入，不能静默忽略用户设置的排除条件。
            if(excludes.isNotEmpty() && (location==null || excludes.any {it.contains(location)})) continue
            check(result.size<10000) {"Scan limit reached"}
            val uri=ContentUris.withAppendedId(media,c.getLong(0))
            try {result+=importer.read(uri,minimumDurationMs=options.minimumSeconds*1000L)}
            catch(e: CancellationException) {throw e}
            catch(_: Exception) { /* 损坏或不符合实际时长的单首音频不阻断其余扫描。 */ }
        }}
        result
    }
}
