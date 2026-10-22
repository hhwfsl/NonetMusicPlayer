package app.nonetmusicplayer.android.data

import android.content.Context
import android.net.Uri
import app.nonetmusicplayer.android.domain.readLimited
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.*
import java.io.File
import java.util.UUID
import java.util.zip.*

/** ZIP 条目从不直接作为本机路径，恢复过程限制总量并使用事务替换元数据。 */
class LibraryBackup(private val context: Context,private val repository: MusicRepository) {
    suspend fun export(uri: Uri) = withContext(Dispatchers.IO) {
        val json=repository.exportMetadata()
        val references=linkedMapOf<String,String>()
        fun inspect(value: Any?) {
            when(value) {
                is JSONObject ->value.keys().forEach {inspect(value.get(it))}
                is JSONArray ->for(i in 0 until value.length()) inspect(value.get(i))
                is String -> {
                    val file=File(value)
                    if(file.isAbsolute && file.isFile && file.canonicalPath.startsWith(context.filesDir.canonicalPath+File.separator))
                        references.putIfAbsent(value,"assets/${references.size}.${file.extension.take(8)}")
                }
            }
        }
        inspect(json)
        // 设置表里的 JSON 是字符串，需要额外检查其图片引用。
        repository.state.value.covers.values.forEach {inspect(it)}
        inspect(repository.state.value.preferences.background)
        val assets=JSONObject();references.forEach {(path,key)->assets.put(path,key)}
        json.put("assets",assets)
        val metadata=json.toString().toByteArray();require(metadata.size<=16_000_000)
        require(references.size<20000 && references.keys.sumOf {File(it).length()}<=100_000_000)
        var total=0L
        ZipOutputStream(context.contentResolver.openOutputStream(uri,"wt") ?: error("Output unavailable")).use {zip ->
            zip.putNextEntry(ZipEntry("metadata.json"));zip.write(metadata);zip.closeEntry()
            references.forEach {(path,key) ->
                val file=File(path);total+=file.length();require(total<=100_000_000)
                zip.putNextEntry(ZipEntry(key));file.inputStream().use {it.copyTo(zip)};zip.closeEntry()
            }
        }
    }
    suspend fun restore(uri: Uri) = withContext(Dispatchers.IO) {
        val staging=File(context.filesDir,"Images/restore-${UUID.randomUUID()}").apply {mkdirs()}
        var committed=false
        try {
            val files=mutableMapOf<String,String>();var manifest: JSONObject?=null;var total=0L;var count=0
            ZipInputStream(context.contentResolver.openInputStream(uri) ?: error("Input unavailable")).use {zip ->
                while(true) {
                    val entry=zip.nextEntry ?: break
                    require(++count<=20000 && !entry.isDirectory)
                    val bytes=zip.readLimited(16_000_000);total+=bytes.size;require(total<=100_000_000)
                    if(entry.name=="metadata.json") {require(manifest==null);manifest=JSONObject(bytes.toString(Charsets.UTF_8))}
                    else {
                        require(Regex("assets/[0-9]+\\.[A-Za-z0-9]{0,8}").matches(entry.name) && entry.name !in files)
                        val file=File(staging,"${files.size}.${entry.name.substringAfterLast('.')}")
                        file.writeBytes(bytes);files[entry.name]=file.absolutePath
                    }
                    zip.closeEntry()
                }
            }
            val json=manifest ?: error("Metadata missing")
            val assets=json.optJSONObject("assets") ?: JSONObject()
            val mapping=assets.keys().asSequence().associateWith {files[assets.getString(it)] ?: error("Asset missing")}
            fun replace(value: Any?) {
                when(value) {
                    is JSONObject ->value.keys().asSequence().toList().forEach {key ->
                        val item=value.get(key);if(item is String && item in mapping) value.put(key,mapping[item]) else replace(item)
                    }
                    is JSONArray ->for(i in 0 until value.length()) {
                        val item=value.get(i);if(item is String && item in mapping) value.put(i,mapping[item]) else replace(item)
                    }
                }
            }
            val tables=json.getJSONObject("tables")
            replace(tables)
            val settings=tables.getJSONArray("settings")
            for(i in 0 until settings.length()) {
                val row=settings.getJSONArray(i)
                if(row.getString(0) in listOf("preferences","covers")) {
                    val config=JSONObject(row.getString(1));replace(config);row.put(1,config.toString())
                }
            }
            repository.restoreMetadata(json);committed=true
        } finally {
            // staging 为本方法生成的固定子目录，失败后仅删除这一份恢复文件。
            if(!committed && staging.canonicalPath.startsWith(File(context.filesDir,"Images").canonicalPath+File.separator)) staging.deleteRecursively()
        }
    }
}
